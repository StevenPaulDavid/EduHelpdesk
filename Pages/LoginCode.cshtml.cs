using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The second step of signing in, for an account with two-step sign-in: the code from the authenticator app, or a
// recovery code. Reached only from /Login with the right password (TwoFactorPending). Wrong codes count towards a
// lockout of their own, just as wrong passwords do.
public class LoginCodeModel(HelpdeskStore store, SignInThrottle throttle, TwoFactorPending pending) : PageModel
{
    public const string ThrottleForm = "helpdesk-code";
    [BindProperty] public string Code { get; set; } = "";
    public string? ReturnUrl { get; set; }
    public string Name { get; private set; } = "";
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(string? returnUrl)
    {
        ReturnUrl = returnUrl;
        if (pending.Read(Request, store) is not { } technician) return RedirectToPage("/Login", new { returnUrl });
        Name = technician.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        ReturnUrl = returnUrl;
        if (pending.Read(Request, store) is not { } technician)
        {
            Message = "That took too long. Sign in again.";
            return RedirectToPage("/Login", new { returnUrl });
        }
        Name = technician.Name;
        var address = HttpContext.Connection.RemoteIpAddress;
        if (throttle.Refusal(ThrottleForm, technician.Email, address) is { } refusal)
        {
            TwoFactorPending.Clear(Response);
            ModelState.AddModelError("", refusal);
            return Page();
        }
        var check = store.CheckTwoFactor(technician.Id, Code);
        if (!check.Ok)
        {
            throttle.Failed(ThrottleForm, technician.Email, address);
            ModelState.AddModelError("", check.UsedRecoveryCode ? "That recovery code isn't one of yours, or it has been used." : "That code didn't match. Type the code the app shows now.");
            return Page();
        }
        throttle.Succeeded(ThrottleForm, technician.Email);
        TwoFactorPending.Clear(Response);
        await TechnicianSession.SignInAsync(HttpContext, store.Technicians.First(x => x.Id == technician.Id));
        if (check.UsedRecoveryCode)
            Message = check.RecoveryCodesLeft == 0
                ? "You signed in with your last recovery code. Make new ones now, under Two-step sign-in."
                : $"You signed in with a recovery code. {check.RecoveryCodesLeft} left - if your phone is gone, set two-step sign-in up again on a new one.";
        if (technician.RequirePasswordChange) return RedirectToPage("/ChangePassword");
        if (check.UsedRecoveryCode) return RedirectToPage("/TwoFactor");
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Index");
    }
}
