namespace EduHelpdesk.Services;

// Headers every response carries unless the endpoint set its own (the logo sends a stricter policy of its own, and
// static files their own caching).
// - The content security policy lets the pages load scripts, styles, images and fonts only from the helpdesk itself,
//   post forms only back to it, and never be framed by another site (clickjacking). Scripts must be files under
//   wwwroot/js: an inline <script> or an onclick= attribute is refused, so text that slipped past encoding into a page
//   still could not run. The pages declare their behaviour with data- attributes instead (wwwroot/js/actions.js).
//   Inline styles are still allowed - the branding colours and a few widths are set that way - and a style cannot run
//   code.
// - Pages aren't cached, so after signing out on a shared PC the Back button can't bring a ticket back up.
public static class SecurityHeaders
{
    public const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            if (!headers.ContainsKey("Content-Security-Policy")) headers.ContentSecurityPolicy = Policy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!headers.ContainsKey("Cache-Control")) headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await next();
    });
}
