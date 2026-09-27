using System.Globalization;
using System.Security.Cryptography;
using EduHelpdesk.Models;
using Microsoft.AspNetCore.DataProtection;

namespace EduHelpdesk.Services;

// Between the two steps of signing in: the password was right, the code is still to come. A short-lived encrypted
// cookie remembers who got that far, for ten minutes, and only as long as their password hasn't changed since. It
// signs nobody in - no page opens with it except the code page (Pages/LoginCode).
public sealed class TwoFactorPending(IDataProtectionProvider provider)
{
    public const string CookieName = "eduhelpdesk_2fa";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = provider.CreateProtector("EduHelpdesk.TwoFactorPending.v1");

    public void Start(HttpResponse response, TechnicianRecord technician)
    {
        var expires = DateTimeOffset.UtcNow + Lifetime;
        var payload = string.Join('|', technician.Id, TechnicianSession.Stamp(technician), expires.UtcTicks.ToString(CultureInfo.InvariantCulture));
        response.Cookies.Append(CookieName, _protector.Protect(payload), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = response.HttpContext.Request.IsHttps,
            IsEssential = true,
            Expires = expires
        });
    }

    // The technician half-way through signing in, if there is one and nothing has changed since.
    public TechnicianRecord? Read(HttpRequest request, HelpdeskStore store)
    {
        var value = request.Cookies[CookieName];
        if (string.IsNullOrEmpty(value)) return null;
        string payload;
        try { payload = _protector.Unprotect(value); }
        catch (CryptographicException) { return null; }
        var parts = payload.Split('|');
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var id)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || new DateTimeOffset(ticks, TimeSpan.Zero) < DateTimeOffset.UtcNow) return null;
        var technician = store.Technicians.FirstOrDefault(x => x.Id == id && x.IsActive);
        return technician is not null && technician.TwoFactor is not null && TechnicianSession.Stamp(technician) == parts[1] ? technician : null;
    }

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);
}
