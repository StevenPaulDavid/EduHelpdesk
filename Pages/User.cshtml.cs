using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class UserModel(HelpdeskStore store, SignInThrottle throttle, TemporaryPasswords passwords) : PageModel
{
    public UserRecord? Person { get; private set; }
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    [TempData] public string? Message { get; set; }
    // What they still have - assets, kits, open tickets, active projects - for the leaver check.
    public HelpdeskStore.LeaverHoldings? Holdings { get; private set; }
    public bool CanEdit => store.UserCan(User, Modules.Requesters, ModulePermission.Edit);
    // Booking equipment back in changes assets and ends loans, so it needs both.
    public bool CanBookBackIn => store.UserCan(User, Modules.Assets, ModulePermission.Edit) && store.UserCan(User, Modules.Loans, ModulePermission.Edit);
    public bool CanOpenAssets => store.UserCan(User, Modules.Assets, ModulePermission.View);
    public bool CanSeeTickets => store.UserCan(User, Modules.Tickets, ModulePermission.Access);
    public bool CanOpenProjects => store.UserCan(User, Modules.Projects, ModulePermission.View);

    public IActionResult OnGet(Guid id)
    {
        Person = store.Users.FirstOrDefault(x => x.Id == id);
        if (Person is null) return NotFound();
        Holdings = store.GetHoldings(id);
        return Page();
    }

    public IActionResult OnPostBookBackIn(Guid id)
    {
        if (!CanBookBackIn) return Forbid();
        Message = store.BookEverythingBackIn(id).Message;
        return RedirectToPage(new { id });
    }

    // Subject access: a zip of everything held about them (Services/SubjectAccessExport). It includes internal notes
    // and other people's mentions of them, so it needs Requesters: Edit, and every download is written to the audit log.
    public IActionResult OnGetExport(Guid id)
    {
        if (!CanEdit) return Forbid();
        if (store.GatherSubjectAccess(id) is not { } data) return NotFound();
        var zip = SubjectAccessExport.Build(data, store.Branding.BrandName);
        store.RecordSubjectAccessExport(data);
        return File(zip, "application/zip", SubjectAccessExport.FileName(data.Person, data.GeneratedAt));
    }

    public IActionResult OnPostSave(Guid id, string name, string email, string? department, string? location, string? password, bool active, bool canRaiseProjects, bool isProjectLead, int[]? selectedNumbers)
    {
        // The page opens for Requesters: View, but saving changes names, portal passwords, ticket ownership and who may
        // raise projects, so it needs Edit.
        if (!store.UserCan(User, Modules.Requesters, ModulePermission.Edit)) return Forbid();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            Message = "User name and email are required.";
            return RedirectToPage(new { id });
        }
        if (!string.IsNullOrWhiteSpace(department) && !store.Departments.Contains(department.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            Message = "Select a valid department.";
            return RedirectToPage(new { id });
        }
        var emailError = store.CheckUserEmail(email, id);
        if (emailError is not null)
        {
            Message = emailError;
            return RedirectToPage(new { id });
        }
        var existing = store.Users.FirstOrDefault(x => x.Id == id);
        if (existing is null)
        {
            Message = "User was not found.";
            return RedirectToPage(new { id });
        }
        var newPassword = !string.IsNullOrWhiteSpace(password);
        if (newPassword && PasswordRules.Problem(password, name, email) is { } problem)
        {
            Message = problem;
            return RedirectToPage(new { id });
        }
        var hash = newPassword ? PasswordHasher.Hash(password!) : existing.PasswordHash;
        var user = existing with
        {
            Name = name.Trim(),
            Email = email.Trim(),
            Department = (department ?? string.Empty).Trim(),
            Location = (location ?? string.Empty).Trim(),
            PasswordHash = hash,
            // A typed password is theirs to keep (TemporaryPasswords); an untouched one keeps whether it must still change.
            RequirePasswordChange = !newPassword && existing.RequirePasswordChange,
            IsActive = active,
            CanRaiseProjects = canRaiseProjects,
            IsProjectLead = isProjectLead
        };
        Message = store.UpdateUserAndTickets(user, selectedNumbers ?? []) ? "User and linked tickets updated." : "User was not found.";
        // Marking someone as having left is the moment to deal with what they still have.
        if (existing.IsActive && !active && store.GetHoldings(id) is { EquipmentCount: > 0 } held)
            Message += $" They still hold {held.EquipmentSummary()} - see the leaver check below.";
        if (newPassword)
        {
            passwords.Remember(id, password!, hash!);
            throttle.Clear("portal", user.Email);
        }
        return RedirectToPage(new { id });
    }

    // A forgotten portal password: a new temporary one, changed at their next sign-in, and the guide to hand it over on.
    public IActionResult OnPostTemporaryPassword(Guid id)
    {
        if (!CanEdit) return Forbid();
        if (store.Users.FirstOrDefault(x => x.Id == id) is not { } existing) return NotFound();
        if (existing.AnonymisedAt is not null) return RedirectToPage(new { id });
        var temporary = TemporaryPasswords.Generate(existing.Name, existing.Email);
        var hash = PasswordHasher.Hash(temporary);
        store.UpdateUser(existing with { PasswordHash = hash, RequirePasswordChange = true });
        passwords.Remember(id, temporary, hash);
        throttle.Clear("portal", existing.Email);
        return RedirectToPage("/People/QuickStart", new { user = id });
    }
}
