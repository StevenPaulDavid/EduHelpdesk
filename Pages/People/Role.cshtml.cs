using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class RoleModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<(string Key, string Label, string Description)> AllPermissions => Permissions.All;
    public RoleRecord? Role { get; private set; }
    public string? CurrentName { get; private set; }

    public IActionResult OnGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Page();
        Role = store.Roles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (Role is null) return NotFound();
        if (Role.IsProtected)
        {
            TempData["Message"] = "The Administrator role cannot be changed.";
            return RedirectToPage("/People");
        }
        CurrentName = Role.Name;
        return Page();
    }

    public IActionResult OnPost(string? currentName, string name, string[]? permission)
    {
        CurrentName = currentName;
        var granted = (permission ?? []).ToHashSet();
        var role = new RoleRecord(
            (name ?? string.Empty).Trim(),
            granted.Contains(Permissions.Settings),
            granted.Contains(Permissions.ManageRoles),
            granted.Contains(Permissions.ManageStaff),
            granted.Contains(Permissions.ManageRequesters),
            granted.Contains(Permissions.ManageAssets),
            granted.Contains(Permissions.ManageSuppliers),
            granted.Contains(Permissions.ManageParts),
            granted.Contains(Permissions.TicketDestructive),
            granted.Contains(Permissions.ChangeWorkingAs));
        Role = role;

        var message = string.IsNullOrWhiteSpace(currentName) ? store.AddRole(role) : store.UpdateRole(currentName, role);
        if (message is not ("Role added." or "Role updated."))
        {
            ModelState.AddModelError("", message);
            return Page();
        }
        TempData["Message"] = message;
        return RedirectToPage("/People");
    }
}
