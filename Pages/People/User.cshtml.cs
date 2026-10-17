using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

// Adding a requester. Their details are changed on their own page (/User), which also handles their tickets, equipment
// and access, so this form only ever makes a new one.
public class UserModel(HelpdeskStore store, SignInThrottle throttle, TemporaryPasswords passwords) : PageModel
{
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;

    public IActionResult OnGet() => store.UserCan(User, Modules.Requesters, ModulePermission.New) ? Page() : Forbid();

    public IActionResult OnPost(string name, string email, string? department, string? location, string? password, bool active, bool canRaiseProjects, bool isProjectLead)
    {
        if (!store.UserCan(User, Modules.Requesters, ModulePermission.New)) return Forbid();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError("", "User name and email are required.");
            return Page();
        }
        var emailError = store.CheckUserEmail(email, null);
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

        // As for technicians: nothing typed means a temporary password, changed at first sign-in; a typed one is theirs
        // to keep (TemporaryPasswords).
        var newPassword = typed ? password! : TemporaryPasswords.Generate(name, email);
        var hash = PasswordHasher.Hash(newPassword);
        var item = new UserRecord(Guid.NewGuid(), name.Trim(), email.Trim(), (department ?? "").Trim(), (location ?? "").Trim(), hash, active)
        {
            CanRaiseProjects = canRaiseProjects, IsProjectLead = isProjectLead,
            RequirePasswordChange = !typed
        };
        store.AddUser(item);
        passwords.Remember(item.Id, newPassword, hash);
        throttle.Clear("portal", item.Email);
        QuickStartModel.MarkJustAdded(TempData, item.Id);
        return RedirectToPage("/People/QuickStart", new { user = item.Id });
    }
}
