using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class UserModel(HelpdeskStore store) : PageModel
{
    public UserRecord? Person { get; private set; }
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(Guid id)
    {
        Person = store.Users.FirstOrDefault(x => x.Id == id);
        return Person is null ? NotFound() : Page();
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
        var user = existing with
        {
            Name = name.Trim(),
            Email = email.Trim(),
            Department = (department ?? string.Empty).Trim(),
            Location = (location ?? string.Empty).Trim(),
            PasswordHash = !string.IsNullOrWhiteSpace(password) ? PasswordHasher.Hash(password) : existing.PasswordHash,
            IsActive = active,
            CanRaiseProjects = canRaiseProjects,
            IsProjectLead = isProjectLead
        };
        Message = store.UpdateUserAndTickets(user, selectedNumbers ?? []) ? "User and linked tickets updated." : "User was not found.";
        return RedirectToPage(new { id });
    }
}
