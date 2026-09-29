using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

public class IndexModel(HelpdeskStore store, PortalIdentity portal, SignInThrottle throttle) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    // How the current portal session started - a technician's has no portal password to change.
    public PortalIdentity.Kind? SessionKind { get; private set; }
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    // Unticked by default: a staffroom PC is the case to protect. See PortalIdentity for the lifetimes.
    [BindProperty] public bool Remember { get; set; }
    [TempData] public string? Message { get; set; }

    // Set when a technician is signed in to the helpdesk in this browser, so the portal can offer to use that instead
    // of asking for a portal password they may never have been given.
    public TechnicianRecord? SignedInTechnician { get; private set; }

    // Someone who raised projects before losing the tick can still follow them.
    public bool HasProjects { get; private set; }
    // For the project lead: open projects nobody has been given yet.
    public int AwaitingAssignment { get; private set; }
    // Their tickets with a reply or status change from IT they haven't opened yet.
    public int TicketsWithNews { get; private set; }

    public void OnGet()
    {
        var session = portal.Validate(Request, store);
        SessionKind = session?.Kind;
        CurrentUser = session is not null ? store.Users.FirstOrDefault(x => x.Id == session.UserId) : null;
        HasProjects = CurrentUser is not null && store.Projects.Any(x => x.RequesterId == CurrentUser.Id);
        if (CurrentUser is not null) TicketsWithNews = store.PortalTickets(CurrentUser.Id).Count(HelpdeskStore.HasUpdateForRequester);
        if (CurrentUser is { IsProjectLead: true }) AwaitingAssignment = store.Projects.Count(x => x.IsActive && x.TechnicianId is null);
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
        portal.SignInTechnician(Response, store, user, technician.Id);
        return RedirectToPage();
    }

    private TechnicianRecord? Technician() =>
        User.Identity?.IsAuthenticated == true
        && Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            ? store.Technicians.FirstOrDefault(x => x.Id == id && x.IsActive)
            : null;

    public IActionResult OnPostLogin()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (throttle.Refusal("portal", Email, address) is { } refusal)
        {
            ModelState.AddModelError("", refusal);
            return Page();
        }
        var user = store.Users.FirstOrDefault(x => string.Equals(x.Email, (Email ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        // Same generic message, and the same hashing time, whether the account is missing, inactive, or has no password
        // set yet - avoids leaking which.
        var passwordOk = PasswordHasher.Verify(user?.PasswordHash, Password ?? "");
        if (user is null || !user.IsActive || !passwordOk)
        {
            throttle.Failed("portal", Email, address);
            ModelState.AddModelError("", "Incorrect email or password.");
            return Page();
        }
        throttle.Succeeded("portal", Email);
        portal.SignIn(Response, store, user, Remember);
        // A password a technician set goes straight to choosing their own (PasswordChangeFilter would send them anyway).
        return user.RequirePasswordChange ? RedirectToPage("/Portal/Password") : RedirectToPage();
    }

    public IActionResult OnPostSwitch()
    {
        portal.SignOut(HttpContext, store);
        return RedirectToPage();
    }
}
