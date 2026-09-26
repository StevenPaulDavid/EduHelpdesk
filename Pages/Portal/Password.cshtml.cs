using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// A requester choosing their own portal password: voluntarily, or because a technician set the last one
// (RequirePasswordChange - PasswordChangeFilter sends every other portal page here until it's done). Changing it signs
// out every other browser using the old one; this one carries on.
public class PasswordModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    public bool Forced => CurrentUser?.RequirePasswordChange == true;
    // Someone using the portal through their helpdesk sign-in has no portal password here to change.
    public bool ViaHelpdesk { get; private set; }
    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public IActionResult OnGet() => Load(out _) ?? Page();

    public IActionResult OnPost()
    {
        if (Load(out var session) is { } redirect) return redirect;
        if (ViaHelpdesk) return Page();
        var user = CurrentUser!;
        if (!PasswordHasher.Verify(user.PasswordHash, CurrentPassword ?? ""))
        {
            ModelState.AddModelError("", "Current password is incorrect.");
            return Page();
        }
        if (PasswordRules.Problem(NewPassword, user.Name, user.Email) is { } problem)
        {
            ModelState.AddModelError("", problem);
            return Page();
        }
        if (NewPassword == CurrentPassword)
        {
            ModelState.AddModelError("", "Choose a password different from the one you have now.");
            return Page();
        }
        if (NewPassword != ConfirmPassword)
        {
            ModelState.AddModelError("", "New password and confirmation do not match.");
            return Page();
        }
        var updated = user with { PasswordHash = PasswordHasher.Hash(NewPassword), RequirePasswordChange = false };
        store.UpdateUser(updated);
        portal.Restamp(Response, session!, updated);
        TempData["Message"] = "Password changed.";
        return RedirectToPage("/Portal/Index");
    }

    private IActionResult? Load(out PortalIdentity.Session? session)
    {
        session = portal.Validate(Request, store);
        if (session is null) return RedirectToPage("/Portal/Index");
        var userId = session.UserId;
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == userId);
        ViaHelpdesk = session.Kind == PortalIdentity.Kind.Technician;
        return null;
    }
}
