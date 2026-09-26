using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EduHelpdesk.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace EduHelpdesk.Services;

// The "who are you" cookie used by the staff portal (Pages/Portal) - separate from the technician login (Program.cs's
// cookie-auth scheme). Set by a portal password sign-in, or by a technician's "Open staff portal".
// The cookie is encrypted and signed with ASP.NET Core Data Protection, so it can't be written by hand. Anything that
// doesn't decrypt or parse - including every cookie from before sessions had lifetimes - is treated as signed out.
//
// How long a session lasts depends on how it started:
// - Password, "keep me signed in" ticked: 30 days from sign-in, for someone's own laptop.
// - Password, not ticked (the default, and the right choice on a staffroom PC): gone when the browser closes, and
//   after 2 hours without using the portal or 12 hours in all, whichever comes first - browsers that restore their
//   tabs can keep "session" cookies for days.
// - A technician's "Open staff portal": only while that technician is signed in to the helpdesk in the same browser.
// Every kind ends when the requester is deactivated, and the password kinds when the password changes.
public sealed class PortalIdentity(IDataProtectionProvider provider)
{
    public const string CookieName = "portal_who";
    public static readonly TimeSpan RememberedLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan SessionIdle = TimeSpan.FromHours(2);
    // The idle clock is only rewritten when it's at least this stale, rather than on every page.
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(1);

    private readonly IDataProtector _protector = provider.CreateProtector("EduHelpdesk.PortalIdentity.v2");

    public enum Kind { Session, Remembered, Technician }

    // What the cookie says. Stamp is a digest of the password hash (see TechnicianSession); TechnicianId is set only for
    // the Technician kind.
    public sealed record Session(Guid UserId, Kind Kind, DateTimeOffset SignedInAt, DateTimeOffset LastSeenAt, string Stamp, Guid? TechnicianId);

    // The requester the cookie names, if it is genuine and hasn't run out. Doesn't check the directory - see Resolve.
    // Used on its own only to put a name to changes made from the portal (HelpdeskStore.CurrentActor).
    public Guid? Read(HttpRequest request) => Current(request)?.UserId;

    // The signed-in portal user: a genuine, unexpired cookie naming someone who is still in the directory and active,
    // whose password hasn't changed since - or, for a technician's session, whose technician is still signed in here.
    public Guid? Resolve(HttpRequest request, HelpdeskStore store) => Validate(request, store)?.UserId;

    public Session? Validate(HttpRequest request, HelpdeskStore store)
    {
        if (Current(request) is not { } session) return null;
        var user = store.Users.FirstOrDefault(x => x.Id == session.UserId && x.IsActive);
        if (user is null) return null;
        if (session.Kind == Kind.Technician)
        {
            // The helpdesk cookie has already been checked by its own OnValidatePrincipal before any page runs, so a
            // technician who has signed out, been deactivated or had their password reset is no longer this User.
            var principal = request.HttpContext.User;
            return principal.Identity?.IsAuthenticated == true
                && principal.FindFirst(ClaimTypes.NameIdentifier)?.Value == session.TechnicianId?.ToString() ? session : null;
        }
        return session.Stamp == Stamp(user.PasswordHash) ? session : null;
    }

    public void SignIn(HttpResponse response, UserRecord user, bool remember)
    {
        var now = DateTimeOffset.UtcNow;
        Write(response, new Session(user.Id, remember ? Kind.Remembered : Kind.Session, now, now, Stamp(user.PasswordHash), null));
    }

    public void SignInTechnician(HttpResponse response, UserRecord user, Guid technicianId)
    {
        var now = DateTimeOffset.UtcNow;
        Write(response, new Session(user.Id, Kind.Technician, now, now, "", technicianId));
    }

    // After the requester changes their own password: same session, new stamp, so it carries on while any other
    // browser signed in with the old password is signed out.
    public void Restamp(HttpResponse response, Session session, UserRecord user) =>
        Write(response, session with { Stamp = Stamp(user.PasswordHash), LastSeenAt = DateTimeOffset.UtcNow });

    // Moves the idle clock on. Called for every portal page (PortalSessionFilter); only session-kind cookies idle out.
    public void Touch(HttpResponse response, Session session)
    {
        if (session.Kind == Kind.Remembered || DateTimeOffset.UtcNow - session.LastSeenAt < RefreshAfter) return;
        Write(response, session with { LastSeenAt = DateTimeOffset.UtcNow });
    }

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);

    private Session? Current(HttpRequest request)
    {
        var value = request.Cookies[CookieName];
        if (string.IsNullOrEmpty(value)) return null;
        string payload;
        try
        {
            payload = _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Tampered with, written by hand, from an older version, or made with keys that no longer exist. All of
            // them mean the same thing: not signed in.
            return null;
        }
        var parts = payload.Split('|');
        if (parts.Length != 6 || !Guid.TryParse(parts[0], out var userId) || !Enum.TryParse<Kind>(parts[1], out var kind)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var signedIn)
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var lastSeen)) return null;
        var session = new Session(userId, kind, new DateTimeOffset(signedIn, TimeSpan.Zero), new DateTimeOffset(lastSeen, TimeSpan.Zero), parts[4],
            Guid.TryParse(parts[5], out var technicianId) ? technicianId : null);
        var now = DateTimeOffset.UtcNow;
        var expired = session.Kind == Kind.Remembered
            ? now - session.SignedInAt > RememberedLifetime
            : now - session.SignedInAt > SessionLifetime || now - session.LastSeenAt > SessionIdle;
        return expired ? null : session;
    }

    private void Write(HttpResponse response, Session session)
    {
        var payload = string.Join('|', session.UserId, session.Kind, session.SignedInAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            session.LastSeenAt.UtcTicks.ToString(CultureInfo.InvariantCulture), session.Stamp, session.TechnicianId?.ToString() ?? "");
        response.Cookies.Append(CookieName, _protector.Protect(payload), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // Sent only over HTTPS when the page came over HTTPS, so a sign-in there can't leak onto a plain-HTTP request.
            Secure = response.HttpContext.Request.IsHttps,
            IsEssential = true,
            // No expiry makes it a browser-session cookie; the lifetimes above are enforced from inside the cookie.
            Expires = session.Kind == Kind.Remembered ? session.SignedInAt + RememberedLifetime : null
        });
    }

    private static string Stamp(string? passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? "")))[..16];
}
