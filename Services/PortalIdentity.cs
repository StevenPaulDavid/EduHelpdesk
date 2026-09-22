using Microsoft.AspNetCore.Http;

namespace EduHelpdesk.Services;

// The "who are you" cookie used by the staff portal (Pages/Portal). A plain, non-authenticating identity -
// separate from the real technician login (Program.cs's cookie-auth scheme) - so staff can self-identify with
// no password. Mirrors the pattern already used for the technician "Working as" cookie in Pages/Jobs.cshtml.cs.
public static class PortalIdentity
{
    public const string CookieName = "portal_who";

    public static Guid? Resolve(HttpRequest request, HelpdeskStore store) =>
        Guid.TryParse(request.Cookies[CookieName], out var id) && store.Users.Any(x => x.Id == id) ? id : null;

    public static void Set(HttpResponse response, Guid userId) =>
        response.Cookies.Append(CookieName, userId.ToString(), new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);
}
