using System.Security.Claims;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The notification centre's full list (the bell in the header shows the newest), and switching pings and pop-ups on for
// this browser (see wwwroot/js/notifications.js). Any signed-in staff account can open it: what a ping may say is
// decided where it is made, and who hears about new tickets by NotificationEndpoints.Broadcast.
public class NotificationsModel(HelpdeskStore store, LinkGenerator links) : PageModel
{
    // Everything still on file is a fortnight at most; this only stops a very busy fortnight making a very long page.
    private const int ListSize = 200;

    public HelpdeskStore.NotificationInbox? Inbox { get; private set; }

    public void OnGet()
    {
        if (AccountId() is { } id && store.NotificationsOpenFor(HelpdeskStore.StaffAudience))
            Inbox = store.InboxFor(id, NotificationEndpoints.Broadcast(store, User), ListSize);
    }

    // A row in the bell or the list: marks that one read and goes where it points. Opening the ticket would mark it read
    // anyway (Job OnGet); this covers the ones that don't lead to a ticket, and a ticket that has since been deleted.
    public IActionResult OnGetOpen(long id)
    {
        if (AccountId() is not { } account) return Forbid();
        store.MarkNotificationRead(account, id);
        var target = store.FindNotification(id) is { } found && found.Audience == HelpdeskStore.StaffAudience
            ? NotificationEndpoints.Describe(found, links, HttpContext).Url
            : null;
        return target is not null && Url.IsLocalUrl(target) ? LocalRedirect(target) : RedirectToPage();
    }

    public IActionResult OnPostReadAll(string? returnUrl)
    {
        if (AccountId() is not { } account) return Forbid();
        store.MarkAllNotificationsRead(account);
        if (Request.Headers["X-Requested-With"] == "fetch") return new JsonResult(new { unread = 0 });
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage();
    }

    // The test goes through the same store and the same route as a real notification, so it proves the lot: the
    // server, the connection, the browser's permission and the sound. Sent in the background by the page's script.
    public IActionResult OnPostTest()
    {
        if (AccountId() is not { } id) return Forbid();
        store.AddNotification(HelpdeskStore.StaffAudience, id, HelpdeskStore.NotificationKinds.Test);
        return Request.Headers["X-Requested-With"] == "fetch" ? new NoContentResult() : RedirectToPage();
    }

    private Guid? AccountId() => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
