using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class PeopleModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<RoleRecord> Roles => store.Roles;
    public IReadOnlyList<string> TechnicianTeams => store.TechnicianTeams;
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    [TempData] public string? Message { get; set; }

    // Adding/editing/deleting users, technicians and roles is handled by the dedicated pages under /People (linked
    // from the view below), each with its own authorization - so this page only reads.
    public void OnGet() { }

    public static string Summary(RoleRecord role)
    {
        if (role.IsProtected) return "All permissions (protected)";
        var granted = Permissions.All.Where(p => role.Grants(p.Key)).Select(p => p.Label).ToList();
        return granted.Count == 0 ? "No permissions" : string.Join(", ", granted);
    }
}
