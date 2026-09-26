using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class LoginModel(HelpdeskStore store) : PageModel
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
        var technician = store.Technicians.FirstOrDefault(x => string.Equals(x.Email, (Email ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        // Same generic message whether the account is missing, inactive, or has no password set yet - avoids leaking which.
        if (technician is null || !technician.IsActive || !PasswordHasher.Verify(technician.PasswordHash, Password ?? ""))
        {
            ModelState.AddModelError("", "Incorrect email or password.");
            return Page();
        }

        await TechnicianSession.SignInAsync(HttpContext, technician);

        if (technician.RequirePasswordChange) return RedirectToPage("/ChangePassword");
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Index");
    }
}
