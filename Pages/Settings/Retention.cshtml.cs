using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Data retention. Needs Settings: Edit, like every page under /Settings except the audit log (Program.cs).
public class RetentionModel(HelpdeskStore store) : PageModel
{
    public HelpdeskStore.RetentionSettings Rules { get; private set; } = null!;
    public HelpdeskStore.RetentionPreview Preview { get; private set; } = null!;
    public bool WaitingForBackup { get; private set; }
    public HelpdeskStore.BackupSettings Backups => store.Backups;
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        Rules = store.Retention;
        Preview = store.PreviewRetention();
        WaitingForBackup = store.RetentionWaitingForBackup;
    }

    public IActionResult OnPostSave(int ticketMonths, int leaverMonths, int auditMonths)
    {
        Message = store.SetRetention(ticketMonths, leaverMonths, auditMonths);
        return RedirectToPage();
    }

    // Runs the rules straight away. A backup is taken first, so whatever is removed can still be got back until that
    // backup ages out; if it can't be taken, nothing is removed.
    public IActionResult OnPostApply()
    {
        if (!store.Retention.AnyOn) { Message = "No retention rules are switched on."; return RedirectToPage(); }
        var (backedUp, backupMessage) = store.CreateBackup(manual: true);
        if (!backedUp)
        {
            Message = $"Nothing was removed: the backup taken first didn't work ({backupMessage}).";
            return RedirectToPage();
        }
        Message = store.ApplyRetention().Message;
        return RedirectToPage();
    }
}
