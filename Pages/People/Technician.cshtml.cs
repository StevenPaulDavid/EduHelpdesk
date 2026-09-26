using System.Security.Claims;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class TechnicianModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Teams => store.TechnicianTeams;
    // Administrator is only offered to Administrators - or kept on the list for an account that already holds it, so
    // the form shows the truth.
    public IReadOnlyList<string> Roles => store.Roles.Select(x => x.Name)
        .Where(x => IsAdministrator || x != StaffRoles.Administrator || Technician?.Role == StaffRoles.Administrator).ToList();
    public TechnicianRecord? Technician { get; private set; }
    public bool IsAdministrator => string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase);
    public bool IsSelf => Technician is not null && Technician.Id == SignedInId;
    private Guid? SignedInId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    // One page serves adding and editing, so the convention lets in anyone with either permission and the handlers
    // sort out which of the two this actually is. See Program.cs.
    private ModulePermission Needed(Guid? id) => id.HasValue ? ModulePermission.Edit : ModulePermission.New;

    public IActionResult OnGet(Guid? id)
    {
        if (!store.UserCan(User, Modules.StaffAccounts, Needed(id))) return Forbid();
        if (id.HasValue) Technician = store.Technicians.FirstOrDefault(x => x.Id == id);
        if (Technician is { Role: StaffRoles.Administrator } && !IsAdministrator)
        {
            TempData["Message"] = "Only an Administrator can change an Administrator's account.";
            return RedirectToPage("/People", new { tab = "technicians" });
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id, string name, string email, string? team, string role, string? password, bool active)
    {
        if (!store.UserCan(User, Modules.StaffAccounts, Needed(id))) return Forbid();
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
        var normalizedRole = store.NormalizeRoleName(role);
        // Staff accounts: Edit is about looking after colleagues' accounts, not a way up. Only an Administrator can hand
        // out Administrator or touch an Administrator's account (a password reset there is as good as the role itself),
        // and nobody changes their own role or switches their own account off.
        if (existing is { Role: StaffRoles.Administrator } && !IsAdministrator)
        {
            ModelState.AddModelError("", "Only an Administrator can change an Administrator's account.");
            return Page();
        }
        if (normalizedRole == StaffRoles.Administrator && existing?.Role != StaffRoles.Administrator && !IsAdministrator)
        {
            ModelState.AddModelError("", "Only an Administrator can give someone the Administrator role.");
            return Page();
        }
        if (IsSelf && normalizedRole != existing!.Role)
        {
            ModelState.AddModelError("", "You can't change your own role. Ask another administrator to do it.");
            return Page();
        }
        if (IsSelf && !active)
        {
            ModelState.AddModelError("", "You can't deactivate your own account.");
            return Page();
        }
        if (existing is { Role: StaffRoles.Administrator } && (normalizedRole != StaffRoles.Administrator || !active)
            && !store.Technicians.Any(x => x.Id != existing.Id && x.Role == StaffRoles.Administrator && x.IsActive))
        {
            ModelState.AddModelError("", "At least one active Administrator must remain.");
            return Page();
        }

        var item = new TechnicianRecord(id ?? Guid.NewGuid(), name.Trim(), email.Trim(), (team ?? string.Empty).Trim(), normalizedRole,
            !string.IsNullOrWhiteSpace(password) ? PasswordHasher.Hash(password) : existing?.PasswordHash,
            // Someone else setting a password means it has been shared, so it must be changed; your own choice needn't be.
            (!IsSelf && !string.IsNullOrWhiteSpace(password)) || (existing?.RequirePasswordChange ?? false),
            active);
        if (id.HasValue) store.UpdateTechnician(item); else store.AddTechnician(item);
        // A new password ends the account's sessions. Resetting your own here keeps you signed in on this one.
        if (IsSelf && !string.IsNullOrWhiteSpace(password)) await TechnicianSession.SignInAsync(HttpContext, item);
        TempData["Message"] = id.HasValue ? "Technician updated." : "Technician added.";
        return RedirectToPage("/People", new { tab = "technicians" });
    }
}
