using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class UserModel(HelpdeskStore store, SignInThrottle throttle, TemporaryPasswords passwords) : PageModel
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
        var typed = !string.IsNullOrWhiteSpace(password);
        if (typed && PasswordRules.Problem(password, name, email) is { } problem)
        {
            ModelState.AddModelError("", problem);
            return Page();
        }

        // As for technicians: nothing typed on a new account means a temporary password, changed at first sign-in; a
        // typed one is theirs to keep (TemporaryPasswords).
        var temporary = !id.HasValue && !typed ? TemporaryPasswords.Generate(name, email) : null;
        var newPassword = typed ? password! : temporary;
        var hash = newPassword is null ? existing?.PasswordHash : PasswordHasher.Hash(newPassword);
        var item = new UserRecord(id ?? Guid.NewGuid(), name.Trim(), email.Trim(), (department ?? "").Trim(), (location ?? "").Trim(), hash, active)
        {
            CanRaiseProjects = canRaiseProjects, IsProjectLead = isProjectLead,
            RequirePasswordChange = temporary is not null || (newPassword is null && (existing?.RequirePasswordChange ?? false))
        };
        if (id.HasValue) store.UpdateUser(item); else store.AddUser(item);
        if (newPassword is not null)
        {
            passwords.Remember(item.Id, newPassword, hash!);
            throttle.Clear("portal", item.Email);
        }
        if (!id.HasValue)
        {
            QuickStartModel.MarkJustAdded(TempData, item.Id);
            return RedirectToPage("/People/QuickStart", new { user = item.Id });
        }
        TempData["Message"] = "User updated.";
        // Marking someone as having left is the moment to deal with what they still have (the leaver check on /User).
        if (existing is { IsActive: true } && !active && store.GetHoldings(item.Id) is { EquipmentCount: > 0 } held)
        {
            TempData["Message"] = $"User updated. They still hold {held.EquipmentSummary()} - the leaver check on their page can book it all back in.";
            return RedirectToPage("/User", new { id = item.Id });
        }
        return RedirectToPage("/People");
    }
}
