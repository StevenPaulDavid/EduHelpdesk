using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class TechnicianModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Teams => store.TechnicianTeams;
    public IReadOnlyList<string> Roles => StaffRoles.All;
    public TechnicianRecord? Technician { get; private set; }

    public void OnGet(Guid? id)
    {
        if (id.HasValue) Technician = store.Technicians.FirstOrDefault(x => x.Id == id);
    }

    public IActionResult OnPost(Guid? id, string name, string email, string team, string role, string? password, bool active)
    {
        var existing = id.HasValue ? store.Technicians.FirstOrDefault(x => x.Id == id) : null;
        Technician = existing;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError("", "Technician name and email are required.");
            return Page();
        }
        var emailError = store.CheckTechnicianEmail(email, id);
        if (emailError is not null)
        {
            ModelState.AddModelError("", emailError);
            return Page();
        }
        var normalizedRole = StaffRoles.Normalize(role);
        if (existing is { Role: StaffRoles.Administrator } && (normalizedRole != StaffRoles.Administrator || !active)
            && !store.Technicians.Any(x => x.Id != existing.Id && x.Role == StaffRoles.Administrator && x.IsActive))
        {
            ModelState.AddModelError("", "At least one active Administrator must remain.");
            return Page();
        }

        var item = new TechnicianRecord(id ?? Guid.NewGuid(), name.Trim(), email.Trim(), team.Trim(), normalizedRole,
            !string.IsNullOrWhiteSpace(password) ? PasswordHasher.Hash(password) : existing?.PasswordHash,
            !string.IsNullOrWhiteSpace(password) || (existing?.RequirePasswordChange ?? false),
            active);
        if (id.HasValue) store.UpdateTechnician(item); else store.AddTechnician(item);
        TempData["Message"] = id.HasValue ? "Technician updated." : "Technician added.";
        return RedirectToPage("/People");
    }
}
