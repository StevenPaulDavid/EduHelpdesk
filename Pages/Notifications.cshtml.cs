using System.Security.Claims;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Switching pings and pop-ups on for this browser (see wwwroot/js/notifications.js). Any signed-in staff account can
// open it: what a ping may say is decided where it is made, not here.
public class NotificationsModel(HelpdeskStore store) : PageModel
{
    public void OnGet() { }

    // The test goes through the same store and the same route as a real notification, so it proves the lot: the
    // server, the connection, the browser's permission and the sound. Sent in the background by the page's script.
    public IActionResult OnPostTest()
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)) return Forbid();
        store.AddNotification(HelpdeskStore.StaffAudience, id, HelpdeskStore.NotificationKinds.Test);
        return Request.Headers["X-Requested-With"] == "fetch" ? new NoContentResult() : RedirectToPage();
    }
}
