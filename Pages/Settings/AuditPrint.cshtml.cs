using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Settings;

// Print view of the audit log. It lives in Pages/Settings on purpose: Program.cs authorises that folder with
// RequireSettings, so its position in the tree is what keeps the log behind the same permission as the screen version.
// Anywhere else and it would be readable by any signed-in technician.
public class AuditPrintModel(HelpdeskStore store) : AuditModel(store)
{
    // Paper has no "next page" link, so the print view takes the whole filtered log rather than the 50 on screen.
    public override int PageSize => int.MaxValue;
}
