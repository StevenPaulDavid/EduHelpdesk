using EduHelpdesk.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

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

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, technician.Id.ToString()),
            new Claim(ClaimTypes.Name, technician.Name),
            new Claim(ClaimTypes.Email, technician.Email),
            new Claim(ClaimTypes.Role, technician.Role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });

        if (technician.RequirePasswordChange) return RedirectToPage("/ChangePassword");
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Index");
    }
}
