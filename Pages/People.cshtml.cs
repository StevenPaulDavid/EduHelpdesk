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

    // Reads as "Tickets Edit, Assets View, ..." - the level matters as much as the module, so both are shown.
    public static string Summary(RoleRecord role)
    {
        if (role.IsProtected) return "All permissions (protected)";
        var granted = Modules.All
            .Where(m => role.LevelFor(m.Key) != PermissionLevel.None)
            .Select(m => $"{m.Label} {PermissionLevels.Label(role.LevelFor(m.Key))}")
            .ToList();
        var flags = Modules.Flags.All.Where(f => role.Has(f.Key)).Select(f => f.Label).ToList();
        var parts = granted.Concat(flags).ToList();
        return parts.Count == 0 ? "No permissions" : string.Join(", ", parts);
    }
}
