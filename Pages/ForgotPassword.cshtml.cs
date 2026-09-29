using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// A technician who has forgotten their password sets a new one with their recovery key, typed or as the recovery file
// (Services/RecoveryKeys.cs). Open to anyone, like /Login, so it is throttled like /Login: wrong keys count against the
// email and the computer, and a locked account or computer isn't checked at all.
//
// The key is used up, every sign-in of the account ends, and nobody is signed in here: they go to /Login with the new
// password, where two-step sign-in still applies.
public class ForgotPasswordModel(HelpdeskStore store, SignInThrottle throttle) : PageModel
{
    // A recovery file is a few hundred bytes; anything much bigger isn't one.
    private const long MaxFileBytes = 16 * 1024;

    public bool Allowed => store.AllowRecoveryKeys;
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string? Key { get; set; }
    [BindProperty] public IFormFile? KeyFile { get; set; }
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Allowed) return Page();
        var address = HttpContext.Connection.RemoteIpAddress;
        if (throttle.Refusal(RecoveryKeys.ThrottleForm, Email, address) is { } refusal)
        {
            ModelState.AddModelError("", refusal);
            return Page();
        }

        var key = Key;
        if (KeyFile is { Length: > 0 and <= MaxFileBytes })
        {
            using var reader = new StreamReader(KeyFile.OpenReadStream());
            key = RecoveryKeys.FindIn(await reader.ReadToEndAsync());
            if (key is null)
            {
                ModelState.AddModelError("", "That file doesn't have a recovery key in it. Choose the recovery file you downloaded, or type the key instead.");
                return Page();
            }
        }
        else if (KeyFile is { Length: > MaxFileBytes })
        {
            ModelState.AddModelError("", "That file is too big to be a recovery file.");
            return Page();
        }
        if (!RecoveryKeys.LooksValid(key))
        {
            ModelState.AddModelError("", $"A recovery key is {RecoveryKeys.Length} letters and numbers, such as K7QP-M4XR-2DTA-9WEB-HN3C-5VYF. Type it, or choose your recovery file.");
            return Page();
        }
        if (PasswordRules.Problem(NewPassword, Email) is { } problem)
        {
            ModelState.AddModelError("", problem);
            return Page();
        }
        if (NewPassword != ConfirmPassword)
        {
            ModelState.AddModelError("", "The new password and its confirmation don't match.");
            return Page();
        }

        // Hashed before the key is checked (outside the store's lock), so a right and a wrong key take the same time.
        var hash = PasswordHasher.Hash(NewPassword);
        var (technician, nameProblem) = store.ResetPasswordWithRecoveryKey(Email, key, hash, account => PasswordRules.Problem(NewPassword, account.Name, account.Email));
        if (nameProblem is not null)
        {
            // Only reachable with the right key, so this says nothing to anyone without one. The key is still unused.
            ModelState.AddModelError("", nameProblem);
            return Page();
        }
        if (technician is null)
        {
            throttle.Failed(RecoveryKeys.ThrottleForm, Email, address);
            ModelState.AddModelError("", "That email and recovery key don't match. Check both, or ask an administrator to reset your password.");
            return Page();
        }
        throttle.Succeeded(RecoveryKeys.ThrottleForm, Email);
        throttle.Clear("helpdesk", technician.Email);
        store.EndSessionsFor(technician.Id);
        TempData["Message"] = "Your password has been changed. Sign in with it now. Your recovery key is used up, so make a new one from Recovery key in your account menu.";
        return RedirectToPage("/Login");
    }
}
