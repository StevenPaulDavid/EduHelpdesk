using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace EduHelpdesk.Services;

// The "who are you" cookie used by the staff portal (Pages/Portal) - separate from the technician login (Program.cs's
// cookie-auth scheme). Set by a portal password sign-in, or by a technician's "Open staff portal".
// The cookie holds the requester's id encrypted and signed with ASP.NET Core Data Protection. It used to hold the bare
// GUID, and ids are visible to technicians in addresses like /User?id=..., so anyone could write the cookie by hand and
// read, reply to and raise tickets as someone else. Anything that doesn't decrypt - including every cookie written
// before this change - is treated as signed out.
public sealed class PortalIdentity(IDataProtectionProvider provider)
{
    public const string CookieName = "portal_who";

    private readonly IDataProtector _protector = provider.CreateProtector("EduHelpdesk.PortalIdentity");

    // The requester the cookie names, if it is genuine. Doesn't check the directory - see Resolve for that.
    public Guid? Read(HttpRequest request)
    {
        var value = request.Cookies[CookieName];
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return Guid.TryParse(_protector.Unprotect(value), out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            // Tampered with, written by hand, from before the cookie was protected, or made with keys that no longer
            // exist. All of them mean the same thing: not signed in.
            return null;
        }
    }

    // The signed-in portal user: a genuine cookie naming someone who is still in the directory and still active.
    // Deactivating a requester therefore ends their portal session straight away, not whenever the cookie expires.
    public Guid? Resolve(HttpRequest request, HelpdeskStore store) =>
        Read(request) is { } id && store.Users.Any(x => x.Id == id && x.IsActive) ? id : null;

    public void Set(HttpResponse response, Guid userId) =>
        response.Cookies.Append(CookieName, _protector.Protect(userId.ToString()), new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true
        });

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);
}
