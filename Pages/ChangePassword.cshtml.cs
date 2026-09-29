using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace EduHelpdesk.Pages;

// Reached both when RequirePasswordChange forces it - PasswordChangeFilter sends every other page here until it's done -
// and voluntarily from anywhere while signed in.
public class ChangePasswordModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";
    public bool Forced { get; private set; }
    // Whether finishing a forced change will hand over a recovery key (see OnPostAsync), so the page can say so.
    public bool OffersRecoveryKey { get; private set; }

    public IActionResult OnGet()
    {
        var technician = CurrentTechnician();
        if (technician is null) return NotFound();
        Forced = technician.RequirePasswordChange;
        OffersRecoveryKey = Forced && store.AllowRecoveryKeys && technician.RecoveryKey is null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var technician = CurrentTechnician();
        if (technician is null) return NotFound();
        Forced = technician.RequirePasswordChange;
        OffersRecoveryKey = Forced && store.AllowRecoveryKeys && technician.RecoveryKey is null;

        if (!PasswordHasher.Verify(technician.PasswordHash, CurrentPassword ?? ""))
        {
            ModelState.AddModelError("", "Current password is incorrect.");
            return Page();
        }
        if (PasswordRules.Problem(NewPassword, technician.Name, technician.Email) is { } problem)
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

        var updated = technician with { PasswordHash = PasswordHasher.Hash(NewPassword), RequirePasswordChange = false };
        store.UpdateTechnician(updated);
        // A new password ends every other session (see TechnicianSession); this one carries on with a fresh cookie.
        await TechnicianSession.SignInAsync(HttpContext, updated);

        // Their first password of their own - a new account, or after a temporary one - is the moment to save a recovery
        // key, while they know the password and before they can forget it. Shown here once, like on /RecoveryKey. Someone
        // who already has a key keeps it.
        if (Forced && store.AllowRecoveryKeys && technician.RecoveryKey is null && store.CreateRecoveryKey(technician.Id, firstPassword: true) is { } key)
        {
            Forced = false;
            NewRecoveryKey = key;
            return Page();
        }
        TempData["Message"] = "Password changed.";
        return RedirectToPage("/Index");
    }

    // Set when a first password has just been chosen and a recovery key made to go with it.
    public string? NewRecoveryKey { get; private set; }

    private TechnicianRecord? CurrentTechnician()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(id, out var guid) ? store.Technicians.FirstOrDefault(x => x.Id == guid) : null;
    }
}
