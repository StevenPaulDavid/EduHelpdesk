using System.Globalization;
using System.Security.Cryptography;
using EduHelpdesk.Models;
using Microsoft.AspNetCore.DataProtection;

namespace EduHelpdesk.Services;

// Between the two steps of signing in: the password was right, the code is still to come. A short-lived encrypted
// value remembers who got that far, for ten minutes, and only as long as their password hasn't changed since. It signs
// nobody in. In a browser it is a cookie, and no page opens with it except the code page (Pages/LoginCode). The phone
// app has no cookies, so it is handed the value as its pendingId instead (PhoneEndpoints), under a protector of its own
// so neither can be passed off as the other.
public sealed class TwoFactorPending(IDataProtectionProvider provider)
{
    public const string CookieName = "eduhelpdesk_2fa";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = provider.CreateProtector("EduHelpdesk.TwoFactorPending.v1");
    private readonly IDataProtector _phoneProtector = provider.CreateProtector("EduHelpdesk.TwoFactorPending.Phone.v1");

    public void Start(HttpResponse response, TechnicianRecord technician)
    {
        var expires = DateTimeOffset.UtcNow + Lifetime;
        response.Cookies.Append(CookieName, _protector.Protect(Payload(technician, expires)), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = response.HttpContext.Request.IsHttps,
            IsEssential = true,
            Expires = expires
        });
    }

    // The technician half-way through signing in, if there is one and nothing has changed since.
    public TechnicianRecord? Read(HttpRequest request, HelpdeskStore store) => Open(_protector, request.Cookies[CookieName], store);

    // The phone app's version of Start and Read.
    public string StartForPhone(TechnicianRecord technician) => _phoneProtector.Protect(Payload(technician, DateTimeOffset.UtcNow + Lifetime));

    public TechnicianRecord? ReadForPhone(string? pendingId, HelpdeskStore store) => Open(_phoneProtector, pendingId, store);

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);

    private static string Payload(TechnicianRecord technician, DateTimeOffset expires) =>
        string.Join('|', technician.Id, TechnicianSession.Stamp(technician), expires.UtcTicks.ToString(CultureInfo.InvariantCulture));

    private static TechnicianRecord? Open(IDataProtector protector, string? value, HelpdeskStore store)
    {
        if (string.IsNullOrEmpty(value)) return null;
        string payload;
        try { payload = protector.Unprotect(value); }
        catch (CryptographicException) { return null; }
        var parts = payload.Split('|');
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var id)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || new DateTimeOffset(ticks, TimeSpan.Zero) < DateTimeOffset.UtcNow) return null;
        var technician = store.Technicians.FirstOrDefault(x => x.Id == id && x.IsActive);
        return technician is not null && technician.TwoFactor is not null && TechnicianSession.Stamp(technician) == parts[1] ? technician : null;
    }
}
