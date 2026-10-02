using System.Security.Claims;

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
    // Below the 30 seconds that proxies and load balancers commonly allow an idle request.
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(25);

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(PollPath, PollAsync).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> PollAsync(HttpContext context, HelpdeskStore store, PortalIdentity portal, LinkGenerator links,
        IHostApplicationLifetime lifetime, string? audience, long? after, int? wait)
    {
        if (Account(context, store, portal, audience) is not { } account) return Results.Json(new { signedIn = false }, statusCode: StatusCodes.Status401Unauthorized);
        // The request ends when the browser goes away, and when the app is stopping - a service restart for an upgrade
        // shouldn't wait out the longest hold.
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        var seconds = Math.Clamp(wait ?? 0, 0, (int)LongestWait.TotalSeconds);
        var batch = await store.WaitForNotificationsAsync(audience!, account, after ?? -1, TimeSpan.FromSeconds(seconds), ending.Token);
        return Results.Json(new
        {
            signedIn = true,
            cursor = batch.Cursor,
            items = batch.Items.Select(x => Describe(x, links, context))
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

    // What the toast says and where clicking it goes. The words are generic on purpose and chosen here, not stored, so
    // a notification never carries anything about a ticket beyond its number.
    private static object Describe(HelpdeskStore.Notification notification, LinkGenerator links, HttpContext context)
    {
        var settings = links.GetPathByPage(context, notification.Audience == HelpdeskStore.PortalAudience ? "/Portal/Notifications" : "/Notifications");
        var (title, body, url) = notification.Kind switch
        {
            HelpdeskStore.NotificationKinds.Test => ("Test notification", "Notifications are working on this computer.", settings),
            _ => ("Helpdesk", "There is something new.", settings)
        };
        return new { id = notification.Id, kind = notification.Kind, title, body, url };
    }
}
