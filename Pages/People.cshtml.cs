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

    // One entry per thing the role is granted. The level is kept separate from the label so the view can set it in its
    // own type rather than running the two together - "Tickets Edit" reads as a phrase and scans badly in a long list.
    public sealed record Grant(string Label, string? Level);

    public static IReadOnlyList<Grant> Grants(RoleRecord role)
    {
        if (role.IsProtected) return [new Grant("All permissions (protected)", null)];
        return
        [
            .. Modules.All.Where(m => role.LevelFor(m.Key) != PermissionLevel.None)
                .Select(m => new Grant(m.Label, PermissionLevels.Label(role.LevelFor(m.Key)))),
            .. Modules.Flags.All.Where(f => role.Has(f.Key)).Select(f => new Grant(f.Label, null)),
        ];
    }
}
