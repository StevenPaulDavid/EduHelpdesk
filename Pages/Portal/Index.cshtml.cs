using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

public class IndexModel(HelpdeskStore store) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        var id = PortalIdentity.Resolve(Request, store);
        CurrentUser = id is { } userId ? store.Users.FirstOrDefault(x => x.Id == userId) : null;
    }

    public IActionResult OnPostLogin()
    {
        var user = store.Users.FirstOrDefault(x => string.Equals(x.Email, (Email ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        // Same generic message whether the account is missing, inactive, or has no password set yet - avoids leaking which.
        if (user is null || !user.IsActive || !PasswordHasher.Verify(user.PasswordHash, Password ?? ""))
        {
            ModelState.AddModelError("", "Incorrect email or password.");
            return Page();
        }
        PortalIdentity.Set(Response, user.Id);
        return RedirectToPage();
    }

    public IActionResult OnPostSwitch()
    {
        PortalIdentity.Clear(Response);
        return RedirectToPage();
    }
}
