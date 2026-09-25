using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class UserModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public UserRecord? Person { get; private set; }

    // One page serves adding and editing, so the convention lets in anyone with either permission and the handlers
    // sort out which of the two this actually is. See Program.cs.
    private ModulePermission Needed(Guid? id) => id.HasValue ? ModulePermission.Edit : ModulePermission.New;

    public IActionResult OnGet(Guid? id)
    {
        if (!store.UserCan(User, Modules.Requesters, Needed(id))) return Forbid();
        if (id.HasValue) Person = store.Users.FirstOrDefault(x => x.Id == id);
        return Page();
    }

    public IActionResult OnPost(Guid? id, string name, string email, string? department, string? location, string? password, bool active, bool canRaiseProjects, bool isProjectLead)
    {
        if (!store.UserCan(User, Modules.Requesters, Needed(id))) return Forbid();
        var existing = id.HasValue ? store.Users.FirstOrDefault(x => x.Id == id) : null;
        Person = existing;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError("", "User name and email are required.");
            return Page();
        }
        var emailError = store.CheckUserEmail(email, id);
        if (emailError is not null)
        {
            ModelState.AddModelError("", emailError);
            return Page();
        }

        var item = new UserRecord(id ?? Guid.NewGuid(), name.Trim(), email.Trim(), (department ?? "").Trim(), (location ?? "").Trim(),
            !string.IsNullOrWhiteSpace(password) ? PasswordHasher.Hash(password) : existing?.PasswordHash,
            active) { CanRaiseProjects = canRaiseProjects, IsProjectLead = isProjectLead };
        if (id.HasValue) store.UpdateUser(item); else store.AddUser(item);
        TempData["Message"] = id.HasValue ? "User updated." : "User added.";
        return RedirectToPage("/People");
    }
}
