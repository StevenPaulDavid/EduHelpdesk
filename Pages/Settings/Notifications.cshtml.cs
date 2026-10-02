using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Notifications: pings and pop-ups on or off for everyone, for staff or for requesters, and whether people who
// haven't switched them on are reminded to. Needs Settings: Edit, like the other pages under /Settings (Program.cs).
public class NotificationsModel(HelpdeskStore store) : PageModel
{
    public HelpdeskStore.NotificationSettingsView Settings => store.NotificationSettings;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    // An unticked box isn't sent, so each parameter is simply whether its box was ticked.
    public IActionResult OnPostSave(bool enabled, bool staff, bool requesters, bool prompt)
    {
        Message = store.SetNotificationSettings(enabled, staff, requesters, prompt);
        return RedirectToPage();
    }
}
