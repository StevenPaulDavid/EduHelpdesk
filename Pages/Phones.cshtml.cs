using System.Security.Claims;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace EduHelpdesk.Pages;

// The signed-in technician's phones (HelpdeskStore.Phones): pair one by scanning a code with the EduHelpdesk app, see
// which are paired and when each was last used, and revoke one that is lost or replaced. Someone who can edit technician
// accounts sees and can revoke everyone's. Pairing needs the current password, as a recovery key does: a phone stays
// signed in for months, so a session left open on someone's desk mustn't be enough to pair one.
public class PhonesModel(HelpdeskStore store, SignInThrottle throttle, IConfiguration configuration, IHostEnvironment environment) : PageModel
{
    public TechnicianRecord Me { get; private set; } = null!;
    public string? Refusal => store.PhoneRefusal(Me);
    public bool AppOn => store.AllowPhoneApp;
    public bool IsHttps => Request.IsHttps;
    public bool CanManageEveryone => store.UserCan(User, Modules.StaffAccounts, ModulePermission.Edit);
    public bool CanSetUp => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public IReadOnlyList<HelpdeskStore.PhoneRecord> MyPhones => store.PhonesOf(Me.Id);
    public IReadOnlyList<HelpdeskStore.PhoneRecord> EveryonesPhones => CanManageEveryone ? store.AllPhones : [];
    public string? TechnicianName(Guid id) => store.Technicians.FirstOrDefault(x => x.Id == id)?.Name;

    // The address the phone should use - the one set in Settings → Sign-in security, or the one this page came from - and
    // the server certificate's fingerprint, for anyone typing the address into the app.
    public string Address => store.SiteAddress is { Length: > 0 } set ? set.TrimEnd('/') : People.QuickStartModel.GuessAddress(Request).TrimEnd('/');
    public string? Fingerprint => ServerCertificate.Fingerprint(configuration, environment);
    // The app only connects over HTTPS and refuses a pairing code that points anywhere else, so an http:// address saved
    // in Settings → Sign-in security (often from before HTTPS was turned on) would make a code no phone can use.
    public bool AddressIsPlainHttp => Address.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    // Phones' alerts are the web bell's: with staff notifications off in Settings → Notifications there are none to send.
    public bool AlertsOff => !store.NotificationsOpenFor(HelpdeskStore.StaffAudience);

    // Shown once, straight after Add a phone.
    public string? QrCode { get; private set; }
    public DateTime? CodeExpiresAt { get; private set; }
    [BindProperty] public string CurrentPassword { get; set; } = "";
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet() => Load() ? Page() : Forbid();

    public IActionResult OnPostAdd()
    {
        if (!Load()) return Forbid();
        if (Refusal is not null || !IsHttps || AddressIsPlainHttp) return RedirectToPage();
        var address = HttpContext.Connection.RemoteIpAddress;
        if (throttle.Refusal("helpdesk", Me.Email, address) is { } refusal)
        {
            ModelState.AddModelError("", refusal);
            return Page();
        }
        if (!PasswordHasher.Verify(Me.PasswordHash, CurrentPassword ?? ""))
        {
            throttle.Failed("helpdesk", Me.Email, address);
            ModelState.AddModelError("", "That isn't your current password.");
            return Page();
        }
        throttle.Succeeded("helpdesk", Me.Email);
        var code = store.CreatePairingCode(Me.Id);
        var pairing = $"eduhelpdesk://pair?u={Uri.EscapeDataString(Address)}&c={code}" + (Fingerprint is { } fingerprint ? $"&fp={fingerprint}" : "");
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(pairing, QRCodeGenerator.ECCLevel.M);
        QrCode = "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(6));
        CodeExpiresAt = DateTime.UtcNow + HelpdeskStore.PairingCodeLifetime;
        return Page();
    }

    public IActionResult OnPostRevoke(Guid id)
    {
        if (!Load()) return Forbid();
        var phone = store.AllPhones.FirstOrDefault(x => x.Id == id);
        if (phone is null || (phone.TechnicianId != Me.Id && !CanManageEveryone)) return RedirectToPage();
        Message = store.RevokePhone(id, phone.TechnicianId == Me.Id ? "Phone revoked" : $"Phone revoked by {Me.Name}")
            ? $"{phone.Name} is signed out. It will need pairing again to be used."
            : "That phone was already signed out.";
        return RedirectToPage();
    }

    public static string PairedBy(HelpdeskStore.PhoneRecord phone) => phone.PairedBy == HelpdeskStore.PhonePairedBy.Code ? "Pairing code" : "Email and password";

    private bool Load()
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            || store.Technicians.FirstOrDefault(x => x.Id == id) is not { } me) return false;
        Me = me;
        return true;
    }
}
