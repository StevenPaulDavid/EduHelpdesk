using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class LoginModel(HelpdeskStore store, SignInThrottle throttle, TwoFactorPending pending) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
    // Carries the outcome of a factory reset, which signs the user out and lands them here.
    [TempData] public string? Message { get; set; }

    public void OnGet(string? returnUrl) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        ReturnUrl = returnUrl;
        var address = HttpContext.Connection.RemoteIpAddress;
        if (throttle.Refusal("helpdesk", Email, address) is { } refusal)
        {
            ModelState.AddModelError("", refusal);
            return Page();
        }
        var technician = store.Technicians.FirstOrDefault(x => string.Equals(x.Email, (Email ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        // Same generic message, and the same hashing time, whether the account is missing, inactive, or has no password
        // set yet - avoids leaking which.
        var passwordOk = PasswordHasher.Verify(technician?.PasswordHash, Password ?? "");
        if (technician is null || !technician.IsActive || !passwordOk)
        {
            throttle.Failed("helpdesk", Email, address);
            ModelState.AddModelError("", "Incorrect email or password.");
            return Page();
        }
        throttle.Succeeded("helpdesk", Email);

        // With two-step sign-in the password is only half of it: nothing is signed in until the code (Pages/LoginCode).
        if (technician.TwoFactor is not null)
        {
            pending.Start(Response, technician);
            return RedirectToPage("/LoginCode", new { returnUrl });
        }

        await TechnicianSession.SignInAsync(HttpContext, technician);

        if (technician.RequirePasswordChange) return RedirectToPage("/ChangePassword");
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Index");
    }
}
