using System.Security.Claims;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The one address a browser asks "is there anything new for me?". It answers at once if there is, and otherwise holds
// the request open for up to 25 seconds and answers the moment something arrives, so a toast follows a submission
// within a second without the browser asking every few seconds - and without a background tab's timers being slowed
// down, which is what browsers do to a page nobody is looking at.
//
// Deliberately not a Razor Page, for two reasons. The portal's pages move a requester's idle clock on every request
// (PortalSessionFilter), and so does the helpdesk cookie's sliding expiry (Program.cs); a tab left open on a staffroom
// PC asking every 25 seconds would keep both signed in for ever. Neither happens here. And the staff portal is open to
// anyone who can reach the site, so who is asking is worked out from whichever sign-in the caller says they are using.
public static class NotificationEndpoints
{
    public const string PollPath = "/notifications/poll";
    // The bell's drop-down list (the notification centre), for helpdesk accounts only.
    public const string InboxPath = "/notifications/inbox";
    // Below the 30 seconds that proxies and load balancers commonly allow an idle request.
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(25);
    // What the drop-down holds; the Notifications page lists the rest.
    private const int InboxSize = 15;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(PollPath, PollAsync).AllowAnonymous();
        app.MapGet(InboxPath, Inbox).AllowAnonymous();
        return app;
    }

    // Whether this account's bell hears about new tickets and replies: the same rule as the pop-ups.
    public static bool Broadcast(HelpdeskStore store, ClaimsPrincipal user) => store.UserCan(user, Modules.Tickets, ModulePermission.Access);

    private static IResult Inbox(HttpContext context, HelpdeskStore store, PortalIdentity portal, LinkGenerator links)
    {
        if (Account(context, store, portal, HelpdeskStore.StaffAudience) is not { } account) return Results.Json(new { signedIn = false }, statusCode: StatusCodes.Status401Unauthorized);
        if (!store.NotificationsOpenFor(HelpdeskStore.StaffAudience)) return Results.Json(new { signedIn = true, disabled = true });
        var inbox = store.InboxFor(account, Broadcast(store, context.User), InboxSize);
        return Results.Json(new
        {
            signedIn = true,
            unread = inbox.Unread,
            items = inbox.Entries.Select(x =>
            {
                var text = Describe(x.Notification, links, context, forCentre: true);
                return new { id = x.Notification.Id, title = text.Title, when = When(x.Notification.CreatedAt), unread = x.Unread, url = OpenUrl(x.Notification, links, context) };
            })
        });
    }

    // Through the Notifications page, which marks it read on the way to the ticket.
    public static string? OpenUrl(HelpdeskStore.Notification notification, LinkGenerator links, HttpContext context) =>
        links.GetPathByPage(context, "/Notifications", "Open", new { id = notification.Id });

    // "Just now", "5 min ago", "3 h ago" today; the day and time before that. In the server's time zone, the school's.
    public static string When(DateTime createdAtUtc)
    {
        var age = DateTime.UtcNow - createdAtUtc;
        var local = createdAtUtc.ToLocalTime();
        if (age < TimeSpan.FromMinutes(1)) return "Just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (local.Date == DateTime.Now.Date) return $"{(int)age.TotalHours} h ago";
        if (local.Date == DateTime.Now.Date.AddDays(-1)) return $"Yesterday, {local:HH:mm}";
        return local.ToString("ddd d MMM, HH:mm");
    }

    private static async Task<IResult> PollAsync(HttpContext context, HelpdeskStore store, PortalIdentity portal, LinkGenerator links,
        IHostApplicationLifetime lifetime, string? audience, long? after, int? wait, int? unread)
    {
        if (Account(context, store, portal, audience) is not { } account) return Results.Json(new { signedIn = false }, statusCode: StatusCodes.Status401Unauthorized);
        // Switched off in Settings → Notifications: say so at once, and the browser stops asking.
        if (!store.NotificationsOpenFor(audience!)) return Results.Json(new { signedIn = true, disabled = true });
        // The request ends when the browser goes away, and when the app is stopping - a service restart for an upgrade
        // shouldn't wait out the longest hold.
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        var seconds = Math.Clamp(wait ?? 0, 0, (int)LongestWait.TotalSeconds);
        // A new ticket or a reply is news for whoever can see tickets, and for nobody else; this is checked on every ask,
        // so a role changed mid-shift takes effect at once. The portal has no broadcasts, only messages for the requester.
        var staff = audience == HelpdeskStore.StaffAudience;
        var broadcast = staff && Broadcast(store, context.User);
        // A page with the bell sends the count it is showing (-1 when it doesn't know), and hears back when that changes.
        int? knownUnread = staff && unread is not null ? unread : null;
        var batch = await store.WaitForNotificationsAsync(audience!, account, after ?? -1, TimeSpan.FromSeconds(seconds), ending.Token, broadcast, knownUnread);
        return Results.Json(new
        {
            signedIn = true,
            cursor = batch.Cursor,
            unread = batch.Unread,
            items = batch.Items.Select(x =>
            {
                var text = Describe(x, links, context);
                return new { id = x.Id, kind = x.Kind, title = text.Title, body = text.Body, url = text.Url };
            })
        });
    }

    // Who is asking, for the sign-in they named: a helpdesk account, or the portal's requester. Null for neither - and
    // for an account that has since been deactivated, which the cookie check has already refused.
    private static Guid? Account(HttpContext context, HelpdeskStore store, PortalIdentity portal, string? audience)
    {
        switch (audience)
        {
            case HelpdeskStore.StaffAudience:
                return context.User.Identity?.IsAuthenticated == true
                    && Guid.TryParse(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
                    && store.Technicians.Any(x => x.Id == id && x.IsActive) ? id : null;
            case HelpdeskStore.PortalAudience:
                return portal.Resolve(context.Request, store);
            default:
                return null;
        }
    }

    public sealed record NotificationText(string Title, string Body, string? Url);

    // What the toast says and where clicking it goes. The words are generic on purpose and chosen here, not stored, so
    // a notification never carries anything about a ticket beyond its number. forCentre: the bell's list, where a row
    // of identical "New ticket received" would tell nobody anything, so a new ticket gives its number there too.
    public static NotificationText Describe(HelpdeskStore.Notification notification, LinkGenerator links, HttpContext context, bool forCentre = false)
    {
        var settings = links.GetPathByPage(context, notification.Audience == HelpdeskStore.PortalAudience ? "/Portal/Notifications" : "/Notifications");
        var ticket = notification.TicketNumber is { } number
            ? links.GetPathByPage(context, notification.Audience == HelpdeskStore.PortalAudience ? "/Portal/Ticket" : "/Job", values: new { number })
            : null;
        var (title, body, url) = notification.Kind switch
        {
            HelpdeskStore.NotificationKinds.Test => ("Test notification", "Notifications are working on this computer.", settings),
            HelpdeskStore.NotificationKinds.NewTicket => (forCentre ? $"New ticket #{notification.TicketNumber}" : "New ticket received", "Click to open it.", ticket ?? settings),
            HelpdeskStore.NotificationKinds.Reply => ($"Reply on ticket #{notification.TicketNumber}", "Click to open it.", ticket ?? settings),
            HelpdeskStore.NotificationKinds.StaffComment => ($"Update on your ticket #{notification.TicketNumber}", "Click to read it.", ticket ?? settings),
            // The one that names someone: the requester needs to know who to expect at the door. A technician's name
            // is no secret on a projected screen, unlike the ticket's words.
            HelpdeskStore.NotificationKinds.OnMyWay => ($"{TechnicianName(notification.ActorId, context) ?? "A technician"} is on their way", $"About your ticket #{notification.TicketNumber}. Click to open it.", ticket ?? settings),
            _ => ("Helpdesk", "There is something new.", settings)
        };
        return new NotificationText(title, body, url);
    }

    private static string? TechnicianName(Guid? id, HttpContext context) =>
        id is { } technicianId ? context.RequestServices.GetRequiredService<HelpdeskStore>().Technicians.FirstOrDefault(x => x.Id == technicianId)?.Name : null;
}
