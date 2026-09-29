using System.Security.Claims;
using System.Text;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The signed-in technician's recovery key (Services/RecoveryKeys.cs): make one, save it as a recovery file, replace it or
// remove it. Making one needs the current password, so a session left open on someone's desk can't be used to plant a
// key that gets its finder back in later.
public class RecoveryKeyModel(HelpdeskStore store, SignInThrottle throttle) : PageModel
{
    public TechnicianRecord Me { get; private set; } = null!;
    public bool Allowed => store.AllowRecoveryKeys;
    // Shown once, straight after it is made.
    public string? NewKey { get; private set; }
    [BindProperty] public string CurrentPassword { get; set; } = "";
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet() => Load() ? Page() : Forbid();

    public IActionResult OnPostCreate()
    {
        if (!Load()) return Forbid();
        if (!Allowed) return RedirectToPage();
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
        NewKey = store.CreateRecoveryKey(Me.Id);
        Load();
        return Page();
    }

    // The recovery file for the key just made. The key comes back from the page, and is checked against the stored hash,
    // since only the hash is kept.
    public IActionResult OnPostDownload(string? key)
    {
        if (!Load()) return Forbid();
        if (!store.RecoveryKeyMatches(Me.Id, key))
        {
            Message = "That key is no longer your recovery key, so there's no file to download. Make a new key.";
            return RedirectToPage();
        }
        var address = store.SiteAddress is { Length: > 0 } set ? set : People.QuickStartModel.GuessAddress(Request);
        var text = RecoveryKeys.RecoveryFile(store.Branding.BrandName, Me.Email, RecoveryKeys.Format(key!), address, Me.RecoveryKey!.CreatedAt);
        return File(Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n")), "text/plain", $"helpdesk-recovery-key-{FileSafe(Me.Email)}.txt");
    }

    public IActionResult OnPostRemove()
    {
        if (!Load()) return Forbid();
        Message = store.RemoveRecoveryKey(Me.Id).Ok ? "Your recovery key was removed. It no longer resets your password." : "You don't have a recovery key.";
        return RedirectToPage();
    }

    private bool Load()
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            || store.Technicians.FirstOrDefault(x => x.Id == id) is not { } me) return false;
        Me = me;
        return true;
    }

    private static string FileSafe(string email) => new string(email.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray());
}
