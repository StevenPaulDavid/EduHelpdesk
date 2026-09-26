using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Backups & data. Needs Settings: Edit, like the other pages under /Settings (Program.cs).
public class BackupsModel(HelpdeskStore store) : PageModel
{
    public HelpdeskStore.BackupSettings Backups { get; private set; } = null!;
    public IReadOnlyList<HelpdeskStore.BackupFile> Files { get; private set; } = [];
    public DataLocation? Location => store.Location;
    public string DataFolder => store.DataFolder;
    public string DefaultBackupFolder => store.DefaultBackupFolder;
    public string? BackupSyncedBy { get; private set; }
    // Backups on the same drive as the data survive a deleted file or a bad update, but not the drive failing.
    public bool SameDriveAsData { get; private set; }
    public string RestoreInstructions => store.RestoreInstructions(Files.FirstOrDefault()?.Name ?? "a backup");
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        Backups = store.Backups;
        Files = store.ListBackups();
        BackupSyncedBy = DataLocation.SyncClientFor(Backups.Folder);
        SameDriveAsData = string.Equals(Path.GetPathRoot(Path.GetFullPath(Backups.Folder)), Path.GetPathRoot(Path.GetFullPath(DataFolder)), StringComparison.OrdinalIgnoreCase);
    }

    public IActionResult OnPostSave(bool enabled, string? folder, int keepDays, int hour)
    {
        Message = store.SetBackupSettings(enabled, folder, keepDays, hour).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostBackupNow()
    {
        Message = store.CreateBackup(manual: true).Message;
        return RedirectToPage();
    }
}
