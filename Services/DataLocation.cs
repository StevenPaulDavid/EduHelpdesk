namespace EduHelpdesk.Services;

// Where the helpdesk keeps its data: the database, attachments, logo, print template and sign-in keys. By default that
// is App_Data next to the app, which is fine for a server but not for a copy running from a OneDrive (or Dropbox)
// folder: a sync client copying a live SQLite file mid-write can leave a corrupt or conflicted database. Setting
// EduHelpdesk:DataPath (appsettings.json, or the EduHelpdesk__DataPath environment variable) moves it anywhere else.
//
// The first time the app starts with a new, empty data folder it copies the existing App_Data across - it never moves
// or deletes it - then renames the old database to helpdesk.db.moved and leaves a DATA-MOVED.txt note beside it, so
// the stale copy can't quietly be used again if the setting is later removed.
public sealed class DataLocation
{
    public const string ConfigKey = "EduHelpdesk:DataPath";
    private const string DatabaseName = "helpdesk.db";

    private DataLocation(string folder, string defaultFolder, string? copiedFrom)
    {
        Folder = folder;
        DefaultFolder = defaultFolder;
        CopiedFrom = copiedFrom;
        SyncedBy = SyncClientFor(folder);
    }

    public string Folder { get; }
    public string DefaultFolder { get; }
    public bool IsDefault => string.Equals(Path.GetFullPath(Folder).TrimEnd('\\', '/'), Path.GetFullPath(DefaultFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    // Set on the one start that copied the old App_Data across, so Settings can say it happened.
    public string? CopiedFrom { get; }
    // "OneDrive" etc. when the data folder sits inside a folder a sync client watches.
    public string? SyncedBy { get; }
    public string KeysFolder => Path.Combine(Folder, "keys");

    public static DataLocation Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var defaultFolder = Path.Combine(environment.ContentRootPath, "App_Data");
        var configured = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(configured)) return new DataLocation(defaultFolder, defaultFolder, null);

        var folder = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
        Directory.CreateDirectory(folder);
        var location = new DataLocation(folder, defaultFolder, null);
        if (location.IsDefault || File.Exists(Path.Combine(folder, DatabaseName)) || !File.Exists(Path.Combine(defaultFolder, DatabaseName)))
            return location;

        // First start in the new place with data still in the old one: bring it across. Backups stay where they are -
        // they can be large, and the backup folder is its own setting.
        CopyFolder(defaultFolder, folder, skip: ["backups"]);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Move(Path.Combine(defaultFolder, DatabaseName), Path.Combine(defaultFolder, $"{DatabaseName}.moved-{stamp}"));
        File.WriteAllText(Path.Combine(defaultFolder, "DATA-MOVED.txt"),
            $"The helpdesk's data was copied to {folder} on {DateTime.Now:dd MMM yyyy HH:mm} because {ConfigKey} is set.{Environment.NewLine}" +
            $"The database left here was renamed to {DatabaseName}.moved-{stamp} so it can't be used by mistake. Everything else in this folder is an old copy and can be deleted once you're happy the new location works.{Environment.NewLine}");
        return new DataLocation(folder, defaultFolder, defaultFolder);
    }

    private static void CopyFolder(string from, string to, string[] skip)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.GetDirectories(from))
        {
            var name = Path.GetFileName(directory);
            if (skip.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            CopyFolder(directory, Path.Combine(to, name), []);
        }
    }

    // A path inside a folder a sync client watches. OneDrive publishes its roots as environment variables; the others
    // are recognised by their usual folder names.
    public static string? SyncClientFor(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root
                && full.StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "OneDrive";
        foreach (var (marker, name) in new[] { ("OneDrive", "OneDrive"), ("Dropbox", "Dropbox"), ("Google Drive", "Google Drive"), ("iCloudDrive", "iCloud Drive") })
            if (full.Split(Path.DirectorySeparatorChar).Any(x => x.StartsWith(marker, StringComparison.OrdinalIgnoreCase)))
                return name;
        return null;
    }
}
