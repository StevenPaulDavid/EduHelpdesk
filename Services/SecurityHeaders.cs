namespace EduHelpdesk.Services;

// Headers every response carries unless the endpoint set its own (the logo sends a stricter policy of its own, and
// static files their own caching).
// - The content security policy lets the pages load scripts, styles, images and fonts only from the helpdesk itself,
//   post forms only back to it, and never be framed by another site (clickjacking). Scripts must be files under
//   wwwroot/js: an inline <script> or an onclick= attribute is refused, so text that slipped past encoding into a page
//   still could not run. The pages declare their behaviour with data- attributes instead (wwwroot/js/actions.js).
//   Inline styles are still allowed - the branding colours and a few widths are set that way - and a style cannot run
//   code.
// - The one site that may frame the pages is the school's EduDashboard, once its address is given in Settings →
//   Sign-in security (HelpdeskStore.DashboardAddress), so the helpdesk can be a tile shown inside it. Until then
//   nothing may. X-Frame-Options can't name another site, so it is only sent while nothing may frame the pages;
//   browsers obey frame-ancestors ahead of it anyway.
// - Pages aren't cached, so after signing out on a shared PC the Back button can't bring a ticket back up.
public static class SecurityHeaders
{
    public const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    // Who may frame a response: nobody, or the dashboard. For a response that sends a policy of its own.
    public static string FrameAncestors(string? dashboardOrigin) =>
        "frame-ancestors " + (string.IsNullOrEmpty(dashboardOrigin) ? "'none'" : dashboardOrigin);

    // The policy with the dashboard allowed to frame the pages, or the usual one when there is no dashboard address.
    public static string PolicyFor(string? dashboardOrigin) => Policy.Replace("frame-ancestors 'none'", FrameAncestors(dashboardOrigin));

    // "https://dashboard.school.org.uk/anything" → "https://dashboard.school.org.uk": frame-ancestors names a site, so
    // only the scheme, server and port are kept. Anything else (no scheme, a query, a user name) is refused rather than
    // guessed at, since a wrong value either opens the door too wide or silently keeps the dashboard out.
    public static string? NormaliseOrigin(string? address)
    {
        if (!Uri.TryCreate((address ?? "").Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host) || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0) return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var dashboard = context.RequestServices.GetService<HelpdeskStore>()?.DashboardAddress;
            var headers = context.Response.Headers;
            if (!headers.ContainsKey("Content-Security-Policy")) headers.ContentSecurityPolicy = PolicyFor(dashboard);
            headers.XContentTypeOptions = "nosniff";
            // Antiforgery adds SAMEORIGIN to every page with a form; with a dashboard allowed, frame-ancestors alone decides.
            if (string.IsNullOrEmpty(dashboard)) headers.XFrameOptions = "DENY";
            else headers.Remove("X-Frame-Options");
            headers["Referrer-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!headers.ContainsKey("Cache-Control")) headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await next();
    });
}
