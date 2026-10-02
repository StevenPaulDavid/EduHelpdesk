using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// A requester switching pings and pop-ups on for this browser (see wwwroot/js/notifications.js). Like the rest of the
// portal it is reachable by anyone, so what it does is checked against the portal sign-in.
public class NotificationsModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public IActionResult OnGet() => portal.Resolve(Request, store) is null ? RedirectToPage("/Portal/Index") : Page();

    public IActionResult OnPostTest()
    {
        if (portal.Resolve(Request, store) is not { } id) return Request.Headers["X-Requested-With"] == "fetch" ? Unauthorized() : RedirectToPage("/Portal/Index");
        store.AddNotification(HelpdeskStore.PortalAudience, id, HelpdeskStore.NotificationKinds.Test);
        return Request.Headers["X-Requested-With"] == "fetch" ? new NoContentResult() : RedirectToPage();
    }
}
