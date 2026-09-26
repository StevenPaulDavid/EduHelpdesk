using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EduHelpdesk.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EduHelpdesk.Services;

// The technician sign-in cookie, and the check that keeps it honest. Permissions are read from the role claim, so a
// cookie that only reflected the account as it was at sign-in let a demoted technician keep their old role, and a
// deactivated one keep working, until it expired. Validate runs on every request: it ends the session if the account
// has gone, been deactivated or had its password reset, and otherwise refreshes the claims so a changed role or name
// applies straight away.
public static class TechnicianSession
{
    public const string StampClaim = "eduhelpdesk:stamp";
    public const string SignedInAtClaim = "eduhelpdesk:signed_in_at";
    // However actively it is used, a session ends this long after the password was typed.
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(14);

    public static Task SignInAsync(HttpContext context, TechnicianRecord technician) =>
        context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal(technician, DateTimeOffset.UtcNow),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var store = context.HttpContext.RequestServices.GetRequiredService<HelpdeskStore>();
        var technician = Guid.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? store.Technicians.FirstOrDefault(x => x.Id == id)
            : null;
        // The sign-in time lives in a claim because sliding renewal moves IssuedUtc forward, which is why the old
        // 14-day cap measured from IssuedUtc never actually ended an active session.
        var signedInAt = long.TryParse(principal?.FindFirst(SignedInAtClaim)?.Value, out var ticks) ? new DateTimeOffset(ticks, TimeSpan.Zero) : (DateTimeOffset?)null;
        if (technician is null || !technician.IsActive || signedInAt is null
            || DateTimeOffset.UtcNow - signedInAt.Value > AbsoluteLifetime
            || principal?.FindFirst(StampClaim)?.Value != Stamp(technician))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        if (principal!.FindFirst(ClaimTypes.Role)?.Value != technician.Role
            || principal.FindFirst(ClaimTypes.Name)?.Value != technician.Name
            || principal.FindFirst(ClaimTypes.Email)?.Value != technician.Email)
        {
            context.ReplacePrincipal(Principal(technician, signedInAt.Value));
            context.ShouldRenew = true;
        }
    }

    private static ClaimsPrincipal Principal(TechnicianRecord technician, DateTimeOffset signedInAt) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, technician.Id.ToString()),
            new Claim(ClaimTypes.Name, technician.Name),
            new Claim(ClaimTypes.Email, technician.Email),
            new Claim(ClaimTypes.Role, technician.Role),
            new Claim(StampClaim, Stamp(technician)),
            new Claim(SignedInAtClaim, signedInAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    // Changes whenever the password does, so resetting someone's password signs them out everywhere. A short digest of
    // the stored hash rather than the hash itself, which never needs to leave the server.
    private static string Stamp(TechnicianRecord technician) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(technician.PasswordHash ?? "")))[..16];
}
