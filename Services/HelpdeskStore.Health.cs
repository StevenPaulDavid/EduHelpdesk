using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The database health check (Settings → Database): how big the database and attachments are and how fast they are
// growing, how much disk is left, and how long saves are taking - so trouble shows before the helpdesk feels slow.
// Administrators get a banner on the Overview when saves are slower than the limit set here, or the disk is nearly full.
public sealed partial class HelpdeskStore
{
    public const int DefaultSlowSaveWarningMs = 1000;
    public const int DefaultLowDiskWarningGb = 2;
    public const int MaxSlowSaveWarningMs = 60_000;
    public const int MaxLowDiskWarningGb = 10_000;
    // Enough of a day's saves for "typical" to mean something before it can raise a warning.
    public const int SavesBeforeWarning = 10;
    private const int SizeHistoryDays = 400;
    public const int GrowthDays = 30;

    // One reading a day, taken by the nightly scheduler (BackupScheduler), for the growth figures.
    public sealed record SizeSample(DateOnly Day, long DatabaseBytes, long AttachmentBytes, int AttachmentFiles);

    public sealed record DiskSpace(string Drive, string Holds, long FreeBytes, long TotalBytes);

    public sealed record SaveStats(int Count, double TypicalMs, double SlowestMs, DateTime Since);

    public sealed record HealthReport(
        long DatabaseBytes, long AttachmentBytes, int AttachmentFiles,
        SizeSample? GrowthFrom,
        IReadOnlyList<DiskSpace> Disks,
        SaveStats? Saves,
        int SlowSaveWarningMs, int LowDiskWarningGb,
        IReadOnlyList<string> Warnings);

    public int SlowSaveWarningMs { get { lock (_sync) return _data.SlowSaveWarningMs; } }
    public int LowDiskWarningGb { get { lock (_sync) return _data.LowDiskWarningGb; } }

    public (bool Ok, string Message) SetHealthLimits(int slowSaveMs, int lowDiskGb)
    {
        if (slowSaveMs is < 50 or > MaxSlowSaveWarningMs) return (false, $"Enter a save time between 50 and {MaxSlowSaveWarningMs:N0} milliseconds.");
        if (lowDiskGb is < 0 or > MaxLowDiskWarningGb) return (false, $"Enter a free space between 0 and {MaxLowDiskWarningGb:N0} GB. 0 turns the warning off.");
        lock (_sync)
        {
            _data.SlowSaveWarningMs = slowSaveMs;
            _data.LowDiskWarningGb = lowDiskGb;
            Save();
        }
        return (true, "Warning limits saved.");
    }

    // ---- Measuring ----

    // The database file with its write-ahead log - both are the database until SQLite folds the log in.
    // The log can be folded in and deleted between looking for it and measuring it, so a file that has gone counts as nothing.
    private long DatabaseBytes() =>
        new[] { _path, _path + "-wal" }.Sum(x => { try { return new FileInfo(x) is { Exists: true } file ? file.Length : 0; } catch (IOException) { return 0L; } });

    private (long Bytes, int Files) AttachmentSize()
    {
        if (!Directory.Exists(AttachmentsPath)) return (0, 0);
        long bytes = 0;
        var files = 0;
        foreach (var file in new DirectoryInfo(AttachmentsPath).EnumerateFiles())
        {
            try { bytes += file.Length; files++; }
            catch (IOException) { }
        }
        return (bytes, files);
    }

    // The drives holding the data and the backups (once, if they are the same drive). A network share that can't be
    // measured is left out rather than shown as full.
    private IReadOnlyList<DiskSpace> Disks()
    {
        var disks = new List<DiskSpace>();
        foreach (var (folder, holds) in new[] { (DataFolder, "the data"), (BackupFolder, "the backups") })
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(folder));
                if (string.IsNullOrEmpty(root)) continue;
                if (disks.FirstOrDefault(x => string.Equals(x.Drive, root, StringComparison.OrdinalIgnoreCase)) is { } same)
                {
                    disks[disks.IndexOf(same)] = same with { Holds = "the data and the backups" };
                    continue;
                }
                var drive = new DriveInfo(root);
                if (drive.IsReady) disks.Add(new DiskSpace(root, holds, drive.AvailableFreeSpace, drive.TotalSize));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return disks;
    }

    // Saves in the last day - or since starting, if that was more recently. Held in memory, so a restart starts afresh.
    private SaveStats? SaveStatsFor(DateTime nowUtc)
    {
        var since = nowUtc.AddDays(-1);
        var saves = _saveTimings.Where(x => x.At >= since).Select(x => x.Milliseconds).Order().ToList();
        if (saves.Count == 0) return null;
        return new SaveStats(saves.Count, saves[saves.Count / 2], saves[^1], _saveTimings.Where(x => x.At >= since).Min(x => x.At));
    }

    private List<string> WarningsFor(SaveStats? saves, IReadOnlyList<DiskSpace> disks, int slowMs, int lowGb)
    {
        var warnings = new List<string>();
        if (saves is { Count: >= SavesBeforeWarning } && saves.TypicalMs > slowMs)
            warnings.Add($"Saves are slow: a typical save is taking {Seconds(saves.TypicalMs)}, over the {Seconds(slowMs)} limit. Deleting old closed tickets (Settings → Data retention) keeps them quick.");
        if (lowGb > 0)
            foreach (var disk in disks.Where(x => x.FreeBytes < lowGb * 1024L * 1024 * 1024))
                warnings.Add($"The disk holding {disk.Holds} ({disk.Drive}) has only {FormatSize(disk.FreeBytes)} free, under the {lowGb} GB limit.");
        return warnings;
    }

    private static string Seconds(double ms) => ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.0} s";

    // The whole check, for Settings → Database. Walks the attachments folder, so it's for that page, not every page.
    public HealthReport DatabaseHealth(DateTime nowUtc)
    {
        var (attachmentBytes, attachmentFiles) = AttachmentSize();
        var disks = Disks();
        lock (_sync)
        {
            var saves = SaveStatsFor(nowUtc);
            var today = DateOnly.FromDateTime(nowUtc.ToLocalTime());
            // The reading nearest to GrowthDays ago - or the oldest there is, if the history is shorter.
            var from = _data.SizeHistory.Where(x => x.Day >= today.AddDays(-GrowthDays) && x.Day < today).OrderBy(x => x.Day).FirstOrDefault();
            return new HealthReport(DatabaseBytes(), attachmentBytes, attachmentFiles, from, disks, saves,
                _data.SlowSaveWarningMs, _data.LowDiskWarningGb, WarningsFor(saves, disks, _data.SlowSaveWarningMs, _data.LowDiskWarningGb));
        }
    }

    // Just the warnings, for the Overview banner: cheap enough for every page view.
    public IReadOnlyList<string> HealthWarnings(DateTime nowUtc)
    {
        var disks = Disks();
        lock (_sync) return WarningsFor(SaveStatsFor(nowUtc), disks, _data.SlowSaveWarningMs, _data.LowDiskWarningGb);
    }

    // Today's size reading, if it hasn't been taken yet. Kept for SizeHistoryDays.
    public bool RecordSizeIfDue(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        lock (_sync) if (_data.SizeHistory.Any(x => x.Day == today)) return false;
        var (attachmentBytes, attachmentFiles) = AttachmentSize();
        lock (_sync)
        {
            if (_data.SizeHistory.Any(x => x.Day == today)) return false;
            _data.SizeHistory.Add(new SizeSample(today, DatabaseBytes(), attachmentBytes, attachmentFiles));
            _data.SizeHistory.RemoveAll(x => x.Day < today.AddDays(-SizeHistoryDays));
            SaveBaseline();
            return true;
        }
    }

    // ---- Storage ----

    private static void EnsureHealthSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS SizeHistory (Day TEXT PRIMARY KEY, DatabaseBytes INTEGER NOT NULL, AttachmentBytes INTEGER NOT NULL, AttachmentFiles INTEGER NOT NULL);";
        command.ExecuteNonQuery();
    }

    private static void ReadHealth(SqliteConnection connection, StoreData data)
    {
        string? Value(string key) => ExecuteScalar(connection, $"SELECT Value FROM Metadata WHERE Key = '{key}';") as string;
        if (int.TryParse(Value("SlowSaveWarningMs"), out var slow) && slow is >= 50 and <= MaxSlowSaveWarningMs) data.SlowSaveWarningMs = slow;
        if (int.TryParse(Value("LowDiskWarningGb"), out var disk) && disk is >= 0 and <= MaxLowDiskWarningGb) data.LowDiskWarningGb = disk;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Day, DatabaseBytes, AttachmentBytes, AttachmentFiles FROM SizeHistory ORDER BY Day;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (NullableDateOnly(reader, 0) is { } day)
                data.SizeHistory.Add(new SizeSample(day, reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3)));
    }

    private static void WriteHealth(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "SlowSaveWarningMs", data.SlowSaveWarningMs.ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "LowDiskWarningGb", data.LowDiskWarningGb.ToString(CultureInfo.InvariantCulture));
        foreach (var sample in data.SizeHistory.DistinctBy(x => x.Day))
            Execute(connection, transaction, "INSERT INTO SizeHistory (Day, DatabaseBytes, AttachmentBytes, AttachmentFiles) VALUES ($day,$db,$attachments,$files);",
                ("$day", IsoDay(sample.Day)), ("$db", sample.DatabaseBytes), ("$attachments", sample.AttachmentBytes), ("$files", sample.AttachmentFiles));
    }

    // A factory reset clears the school's data, not the record of how big the files have been or the warning limits.
    private static void CarryHealthSettings(StoreData from, StoreData to)
    {
        to.SlowSaveWarningMs = from.SlowSaveWarningMs;
        to.LowDiskWarningGb = from.LowDiskWarningGb;
        to.SizeHistory = [.. from.SizeHistory];
    }
}
