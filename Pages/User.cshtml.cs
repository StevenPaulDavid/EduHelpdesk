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

    public IActionResult OnPostSave(Guid id, string name, string email, string? department, string? location, int[]? selectedNumbers)
    {
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
        var user = new UserRecord(id, name.Trim(), email.Trim(), (department ?? string.Empty).Trim(), (location ?? string.Empty).Trim());
        Message = store.UpdateUserAndTickets(user, selectedNumbers ?? []) ? "User and linked tickets updated." : "User was not found.";
        return RedirectToPage(new { id });
    }
}
