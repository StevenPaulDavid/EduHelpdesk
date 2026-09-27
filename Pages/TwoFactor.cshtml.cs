using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace EduHelpdesk.Pages;

// Two-step sign-in for the signed-in technician: set it up with an authenticator app, make new recovery codes, or turn
// it off. When Settings requires it, TwoFactorSetupFilter sends anyone without it here until it is done.
public class TwoFactorModel(HelpdeskStore store, IDataProtectionProvider protection) : PageModel
{
    private static readonly TimeSpan SetupLifetime = TimeSpan.FromMinutes(30);
    private readonly IDataProtector _protector = protection.CreateProtector("EduHelpdesk.TwoFactorSetup.v1");

    public TechnicianRecord Me { get; private set; } = null!;
    public bool Required => store.RequireTwoFactor;
    // While setting up: the key, as a QR code and as text, and the sealed copy the form sends back.
    public string? SetupKey { get; private set; }
    public string? QrCode { get; private set; }
    public string? SetupToken { get; private set; }
    // Shown once, straight after they are made.
    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];
    [BindProperty] public string Code { get; set; } = "";
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet() => Load() ? Page() : Forbid();

    public async Task<IActionResult> OnPostEnableAsync(string? setupToken)
    {
        if (!Load()) return Forbid();
        if (Me.TwoFactor is not null) return RedirectToPage();
        if (Unseal(setupToken) is not { } secret)
        {
            Message = "That set-up took too long. Scan the new code below and try again.";
            return RedirectToPage();
        }
        var (ok, message, codes) = store.EnableTwoFactor(Me.Id, secret, Code);
        if (!ok)
        {
            ModelState.AddModelError("", message);
            StartSetup(secret);
            return Page();
        }
        // Every other sign-in of this account was made with the password alone, so they end; this browser carries on.
        store.EndSessionsFor(Me.Id);
        await TechnicianSession.SignInAsync(HttpContext, store.Technicians.First(x => x.Id == Me.Id));
        Load();
        RecoveryCodes = codes;
        Message = message + " Anywhere else you were signed in has been signed out.";
        return Page();
    }

    public IActionResult OnPostRecoveryCodes()
    {
        if (!Load()) return Forbid();
        if (store.RenewRecoveryCodes(Me.Id) is not { } codes) return RedirectToPage();
        Load();
        RecoveryCodes = codes;
        Message = "Here are your new recovery codes. The old ones no longer work.";
        return Page();
    }

    public IActionResult OnPostDisable()
    {
        if (!Load()) return Forbid();
        Message = store.DisableTwoFactor(Me.Id, Code).Message;
        return RedirectToPage();
    }

    private bool Load()
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            || store.Technicians.FirstOrDefault(x => x.Id == id) is not { } me) return false;
        Me = me;
        if (me.TwoFactor is null && SetupKey is null && HttpMethods.IsGet(Request.Method)) StartSetup(Totp.NewSecret());
        return true;
    }

    private void StartSetup(string secret)
    {
        SetupKey = string.Join(" ", secret.Chunk(4).Select(x => new string(x)));
        var expires = (DateTimeOffset.UtcNow + SetupLifetime).UtcTicks.ToString(CultureInfo.InvariantCulture);
        SetupToken = _protector.Protect($"{Me.Id}|{secret}|{expires}");
        var uri = Totp.SetupUri(store.Branding.BrandName is { Length: > 0 } brand ? brand : "EduHelpdesk", Me.Email, secret);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        QrCode = "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(6));
    }

    // The secret from the form, if it was made for this technician and hasn't gone stale.
    private string? Unseal(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            return parts.Length == 3 && parts[0] == Me.Id.ToString()
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && new DateTimeOffset(ticks, TimeSpan.Zero) > DateTimeOffset.UtcNow
                ? parts[1] : null;
        }
        catch (CryptographicException) { return null; }
    }
}
