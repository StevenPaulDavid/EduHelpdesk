using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace EduHelpdesk.Pages;

// Reached both when RequirePasswordChange forces it after sign-in, and voluntarily from anywhere while signed in.
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

    public IActionResult OnPost()
    {
        var technician = CurrentTechnician();
        if (technician is null) return NotFound();
        Forced = technician.RequirePasswordChange;

        if (!PasswordHasher.Verify(technician.PasswordHash, CurrentPassword ?? ""))
        {
            ModelState.AddModelError("", "Current password is incorrect.");
            return Page();
        }
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 8)
        {
            ModelState.AddModelError("", "Choose a new password of at least 8 characters.");
            return Page();
        }
        if (NewPassword != ConfirmPassword)
        {
            ModelState.AddModelError("", "New password and confirmation do not match.");
            return Page();
        }

        store.UpdateTechnician(technician with { PasswordHash = PasswordHasher.Hash(NewPassword), RequirePasswordChange = false });
        TempData["Message"] = "Password changed.";
        return RedirectToPage("/Index");
    }

    private TechnicianRecord? CurrentTechnician()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(id, out var guid) ? store.Technicians.FirstOrDefault(x => x.Id == guid) : null;
    }
}
