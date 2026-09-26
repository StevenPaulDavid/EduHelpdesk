using System.Globalization;
using System.IO.Compression;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Automatic backups: once a day (BackupScheduler) and on demand (Settings → Backups), one zip holding everything needed
// to rebuild the helpdesk - the database, every attachment, the logo and the print template. The database copy is
// taken with SQLite's online backup under the store's lock, so it is a consistent moment even while people work, and
// it is integrity-checked before the zip is kept. Zips are written under a temporary name and renamed when complete, so
// a backup folder synced to OneDrive or a network share never holds half a backup.
public sealed partial class HelpdeskStore
{
    public const int MaxBackupKeepDays = 365;
    // However old they are, the newest few are never pruned - a long holiday with the server off must not come back to
    // an empty backup folder.
    public const int MinimumBackupsKept = 3;
    private const string BackupPrefix = "EduHelpdesk-backup-";
    private static readonly SemaphoreSlim BackupGate = new(1, 1);

    public sealed record BackupFile(string Name, string FullPath, long Size, DateTime CreatedAt);
    public sealed record BackupSettings(bool Enabled, string Folder, bool FolderIsDefault, int KeepDays, int Hour,
        DateTime? LastSuccessAt, string LastFile, string LastSummary, DateTime? LastAttemptAt, string LastError)
    {
        // Switched off, failing, or two missed nights (one could just be the server being off) is worth someone's attention.
        public bool NeedsAttention(DateTime utcNow) => !Enabled || LastSuccessAt is null || utcNow - LastSuccessAt.Value > TimeSpan.FromHours(48) || LastError.Length > 0;
    }

    public string DefaultBackupFolder => Path.Combine(DataFolder, "backups");
    public string BackupFolder { get { lock (_sync) return string.IsNullOrWhiteSpace(_data.BackupFolderSetting) ? DefaultBackupFolder : _data.BackupFolderSetting; } }

    public BackupSettings Backups
    {
        get
        {
            lock (_sync)
                return new BackupSettings(_data.BackupsEnabled, BackupFolder, string.IsNullOrWhiteSpace(_data.BackupFolderSetting), _data.BackupKeepDays, _data.BackupHour,
                    _data.LastBackupAt, _data.LastBackupFile, _data.LastBackupSummary, _data.LastBackupAttemptAt, _data.LastBackupError);
        }
    }

    public (bool Ok, string Message) SetBackupSettings(bool enabled, string? folder, int keepDays, int hour)
    {
        var path = (folder ?? string.Empty).Trim();
        if (keepDays is < 1 or > MaxBackupKeepDays) return (false, $"Keep backups for between 1 and {MaxBackupKeepDays} days.");
        if (hour is < 0 or > 23) return (false, "Choose the hour the nightly backup runs.");
        if (path.Length > 0)
        {
            if (!Path.IsPathFullyQualified(path)) return (false, @"Enter the backup folder as a full path, such as D:\Backups\Helpdesk or \\server\share\helpdesk.");
            path = Path.GetFullPath(path).TrimEnd('\\', '/');
            // Anything under wwwroot is served to the web - a backup there would be downloadable by anyone.
            if (path.StartsWith(Path.GetFullPath(_webRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return (false, "Backups can't go inside the app's web folder, where anyone could download them.");
            try
            {
                Directory.CreateDirectory(path);
                var probe = Path.Combine(path, $".eduhelpdesk-write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "test");
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return (false, $"The helpdesk can't write to that folder ({ex.Message}). Check it exists and the account the app runs as can write there.");
            }
            if (string.Equals(path, DefaultBackupFolder, StringComparison.OrdinalIgnoreCase)) path = "";
        }
        lock (_sync)
        {
            if (_data.BackupsEnabled == enabled && _data.BackupFolderSetting == path && _data.BackupKeepDays == keepDays && _data.BackupHour == hour)
                return (true, "Nothing had changed.");
            _data.BackupsEnabled = enabled;
            _data.BackupFolderSetting = path;
            _data.BackupKeepDays = keepDays;
            _data.BackupHour = hour;
            Save();
            return (true, enabled ? $"Backup settings saved. The next one runs at {hour:00}:00." : "Backup settings saved. Automatic backups are off.");
        }
    }

    // Due once per day, at or after the chosen hour - or as soon as the app is running if it was off at that time. A
    // failed attempt is retried hourly rather than every few minutes.
    public bool BackupDue(DateTime localNow)
    {
        lock (_sync)
        {
            if (!_data.BackupsEnabled) return false;
            if (_data.LastBackupError.Length > 0 && _data.LastBackupAttemptAt is { } attempt && DateTime.UtcNow - attempt < TimeSpan.FromHours(1)) return false;
            var slot = localNow.Date.AddHours(_data.BackupHour);
            if (localNow < slot) slot = slot.AddDays(-1);
            return _data.LastBackupAt is not { } last || last.ToLocalTime() < slot;
        }
    }

    public IReadOnlyList<BackupFile> ListBackups()
    {
        var folder = BackupFolder;
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder, BackupPrefix + "*.zip")
            .Select(x => new FileInfo(x))
            .Select(x => new BackupFile(x.Name, x.FullName, x.Length, x.LastWriteTime))
            .OrderByDescending(x => x.Name, StringComparer.Ordinal)
            .ToList();
    }

    // Makes a backup now. Manual ones are recorded in the audit log with who asked; the nightly one is not, because a
    // line every night would bury everything else.
    public (bool Ok, string Message) CreateBackup(bool manual)
    {
        if (!BackupGate.Wait(0)) return (false, "A backup is already being made. Try again in a minute.");
        try
        {
            var (name, summary, size) = WriteBackup("");
            Prune();
            lock (_sync)
            {
                _data.LastBackupAt = DateTime.UtcNow;
                _data.LastBackupAttemptAt = DateTime.UtcNow;
                _data.LastBackupFile = name;
                _data.LastBackupSummary = summary;
                _data.LastBackupError = "";
                if (manual)
                    _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "Backups", "Backup made", $"{name} ({FormatSize(size)}): {summary}."));
                SaveBaseline();
            }
            return (true, $"Backup saved as {name} ({FormatSize(size)}) - {summary}.");
        }
        catch (Exception ex) when (ex is not SaveFailedException)
        {
            lock (_sync)
            {
                _data.LastBackupAttemptAt = DateTime.UtcNow;
                _data.LastBackupError = ex.Message;
                try { SaveBaseline(); } catch (SaveFailedException) { }
            }
            return (false, $"The backup couldn't be made: {ex.Message}");
        }
        finally
        {
            BackupGate.Release();
        }
    }

    // Writes one backup zip and returns its name, what it holds and its size. Throws if anything goes wrong - the
    // caller records it. The label goes in the file name ("before-reset" for the factory reset's own backup).
    private (string Name, string Summary, long Size) WriteBackup(string label)
    {
        var folder = BackupFolder;
        Directory.CreateDirectory(folder);
        var name = $"{BackupPrefix}{DateTime.Now:yyyyMMdd-HHmmss}{(label.Length > 0 ? "-" + label : "")}.zip";
        var work = Path.Combine(Path.GetTempPath(), $"eduhelpdesk-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var partial = Path.Combine(folder, name + ".partial");
        try
        {
            var database = Path.Combine(work, "helpdesk.db");
            // The database, logo and template together under the lock: one consistent moment. Attachments are named by
            // id and never change once written, so they can be read afterwards without holding everyone up.
            lock (_sync)
            {
                using (var source = new SqliteConnection($"Data Source={_path}"))
                using (var target = new SqliteConnection($"Data Source={database};Pooling=False"))
                {
                    source.Open();
                    target.Open();
                    source.BackupDatabase(target);
                }
                if (File.Exists(LogoPath)) File.Copy(LogoPath, Path.Combine(work, "logo.png"));
                if (File.Exists(_templatePath)) File.Copy(_templatePath, Path.Combine(work, "print-template.docx"));
            }
            var summary = CheckBackupDatabase(database);

            long attachmentCount = 0;
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(database, "helpdesk.db", CompressionLevel.Optimal);
                foreach (var extra in new[] { "logo.png", "print-template.docx" })
                    if (File.Exists(Path.Combine(work, extra))) zip.CreateEntryFromFile(Path.Combine(work, extra), extra, CompressionLevel.Optimal);
                if (Directory.Exists(AttachmentsPath))
                    foreach (var file in Directory.GetFiles(AttachmentsPath))
                    {
                        // Pictures and PDFs are already compressed; squeezing them again only costs time.
                        try { zip.CreateEntryFromFile(file, "attachments/" + Path.GetFileName(file), CompressionLevel.Fastest); attachmentCount++; }
                        catch (FileNotFoundException) { }
                    }
                using var readme = new StreamWriter(zip.CreateEntry("RESTORE.txt").Open());
                readme.Write(RestoreInstructions(name));
            }
            File.Move(partial, Path.Combine(folder, name));
            var size = new FileInfo(Path.Combine(folder, name)).Length;
            return (name, $"{summary}, {attachmentCount} {(attachmentCount == 1 ? "file" : "files")}", size);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (File.Exists(partial)) { try { File.Delete(partial); } catch (IOException) { } }
        }
    }

    // A backup that can't be read back is worse than none, because it is trusted. The copy is checked the way SQLite
    // checks itself, and its record counts become the summary people see.
    private static string CheckBackupDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        var integrity = ExecuteScalar(connection, "PRAGMA integrity_check;") as string;
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"the copied database failed its integrity check ({integrity})");
        long Count(string table) => Convert.ToInt64(ExecuteScalar(connection, $"SELECT COUNT(*) FROM {table};"), CultureInfo.InvariantCulture);
        return $"checked OK: {Count("Tickets")} tickets, {Count("Assets")} assets, {Count("Projects")} projects, {Count("Users")} people";
    }

    // Keeps the last KeepDays days of backups, and never fewer than the newest few. Only this app's own zips are
    // touched, so a shared backup folder is safe; leftover .partial files from an interrupted run go after a day.
    private void Prune()
    {
        int keepDays;
        lock (_sync) keepDays = _data.BackupKeepDays;
        var cutoff = DateTime.Now.AddDays(-keepDays);
        foreach (var old in ListBackups().Skip(MinimumBackupsKept).Where(x => x.CreatedAt < cutoff))
            try { File.Delete(old.FullPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var folder = BackupFolder;
        if (Directory.Exists(folder))
            foreach (var partial in Directory.GetFiles(folder, BackupPrefix + "*.zip.partial").Where(x => File.GetLastWriteTime(x) < DateTime.Now.AddDays(-1)))
                try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public string RestoreInstructions(string backupName) =>
        $"""
        Restoring EduHelpdesk from {backupName}

        1. Stop the helpdesk app.
        2. Rename the current data folder ({DataFolder}) to keep it, e.g. add "-old" to its name,
           then create an empty folder with the original name.
        3. Unzip this backup into that empty folder. It should then contain helpdesk.db and an attachments folder
           (and logo.png / print-template.docx if you had them).
        4. Copy the "keys" folder across from the old data folder, so people's sign-ins keep working.
           Without it everything still works; everyone just signs in again.
        5. Start the app and check a few recent tickets and projects.

        Everything changed after this backup was made is not in it.
        """;

    private static void ReadBackupSettings(SqliteConnection connection, StoreData data)
    {
        string? Value(string key) => ExecuteScalar(connection, $"SELECT Value FROM Metadata WHERE Key = '{key}';") as string;
        static DateTime? When(string? text) => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;
        if (Value("BackupsEnabled") is { } enabled) data.BackupsEnabled = enabled == "1";
        data.BackupFolderSetting = Value("BackupFolder") ?? "";
        if (int.TryParse(Value("BackupKeepDays"), out var keep) && keep is >= 1 and <= MaxBackupKeepDays) data.BackupKeepDays = keep;
        if (int.TryParse(Value("BackupHour"), out var hour) && hour is >= 0 and <= 23) data.BackupHour = hour;
        data.LastBackupAt = When(Value("LastBackupAt"));
        data.LastBackupFile = Value("LastBackupFile") ?? "";
        data.LastBackupSummary = Value("LastBackupSummary") ?? "";
        data.LastBackupAttemptAt = When(Value("LastBackupAttemptAt"));
        data.LastBackupError = Value("LastBackupError") ?? "";
    }

    private static void WriteBackupSettings(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "BackupsEnabled", data.BackupsEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "BackupFolder", data.BackupFolderSetting ?? "");
        SetMetadata(connection, transaction, "BackupKeepDays", data.BackupKeepDays.ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "BackupHour", data.BackupHour.ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "LastBackupAt", data.LastBackupAt is { } at ? Iso(at) : "");
        SetMetadata(connection, transaction, "LastBackupFile", data.LastBackupFile ?? "");
        SetMetadata(connection, transaction, "LastBackupSummary", data.LastBackupSummary ?? "");
        SetMetadata(connection, transaction, "LastBackupAttemptAt", data.LastBackupAttemptAt is { } attempt ? Iso(attempt) : "");
        SetMetadata(connection, transaction, "LastBackupError", data.LastBackupError ?? "");
    }

    // A factory reset clears the school's data, not where its backups go or how they have been getting on.
    private static void CarryBackupSettings(StoreData from, StoreData to)
    {
        to.BackupsEnabled = from.BackupsEnabled;
        to.BackupFolderSetting = from.BackupFolderSetting;
        to.BackupKeepDays = from.BackupKeepDays;
        to.BackupHour = from.BackupHour;
        to.LastBackupAt = from.LastBackupAt;
        to.LastBackupFile = from.LastBackupFile;
        to.LastBackupSummary = from.LastBackupSummary;
        to.LastBackupAttemptAt = from.LastBackupAttemptAt;
        to.LastBackupError = from.LastBackupError;
    }
}
