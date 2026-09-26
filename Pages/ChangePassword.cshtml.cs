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

    public IActionResult OnGet()
    {
        var technician = CurrentTechnician();
        if (technician is null) return NotFound();
        Forced = technician.RequirePasswordChange;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var technician = CurrentTechnician();
        if (technician is null) return NotFound();
        Forced = technician.RequirePasswordChange;

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
        TempData["Message"] = "Password changed.";
        return RedirectToPage("/Index");
    }

    private TechnicianRecord? CurrentTechnician()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(id, out var guid) ? store.Technicians.FirstOrDefault(x => x.Id == guid) : null;
    }
}
