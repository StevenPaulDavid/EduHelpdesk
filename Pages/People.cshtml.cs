using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class PeopleModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostAddUser(string name, string email, string department, string location)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            Message = "User name and email are required.";
            return RedirectToPage();
        }

        store.AddUser(new UserRecord(Guid.NewGuid(), name.Trim(), email.Trim(), department.Trim(), location.Trim()));
        Message = "User added.";
        return RedirectToPage();
    }

    public IActionResult OnPostSaveUser(Guid id, string name, string email, string department, string location)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            Message = "User name and email are required.";
            return RedirectToPage();
        }

        Message = store.UpdateUser(new UserRecord(id, name.Trim(), email.Trim(), department.Trim(), location.Trim()))
            ? "User updated."
            : "User was not found.";
        return RedirectToPage();
    }

    public IActionResult OnPostSaveTechnician(Guid id, string name, string email, string team)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            Message = "Technician name and email are required.";
            return RedirectToPage();
        }

        Message = store.UpdateTechnician(new TechnicianRecord(id, name.Trim(), email.Trim(), team.Trim()))
            ? "Technician updated."
            : "Technician was not found.";
        return RedirectToPage();
    }

    public IActionResult OnPostDeleteUser(Guid id)
    {
        Message = store.DeleteUser(id) ?? "User deleted.";
        return RedirectToPage();
    }

    public IActionResult OnPostDeleteTechnician(Guid id)
    {
        Message = store.DeleteTechnician(id) ?? "Technician deleted.";
        return RedirectToPage();
    }
}
