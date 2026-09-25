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

    // Set when a technician is signed in to the helpdesk in this browser, so the portal can offer to use that instead
    // of asking for a portal password they may never have been given.
    public TechnicianRecord? SignedInTechnician { get; private set; }

    public void OnGet()
    {
        var id = PortalIdentity.Resolve(Request, store);
        CurrentUser = id is { } userId ? store.Users.FirstOrDefault(x => x.Id == userId) : null;
        SignedInTechnician = Technician();
    }

    // "Open staff portal" from the helpdesk's account menu lands here. A plain link (GET) rather than a form, because it
    // opens in a new tab: a form posted into a new tab is turned into a GET by some browsers and embedded views, and
    // middle-click or "open in new tab" never posts at all. That is safe here - the only thing a stray link can do is
    // sign the technician into the portal as themselves (creating their requester record once, if it is missing).
    public IActionResult OnGetTechnician()
    {
        if (Technician() is not { } technician) return RedirectToPage("/Login", new { returnUrl = Url.Page("/Portal/Index") });
        var (user, error) = store.PortalRequesterForTechnician(technician.Id);
        if (user is null)
        {
            Message = error;
            return RedirectToPage();
        }
        PortalIdentity.Set(Response, user.Id);
        return RedirectToPage();
    }

    private TechnicianRecord? Technician() =>
        User.Identity?.IsAuthenticated == true
        && Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            ? store.Technicians.FirstOrDefault(x => x.Id == id && x.IsActive)
            : null;

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
