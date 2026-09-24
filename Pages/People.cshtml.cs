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

    // Three directories on one page, each its own module. The page opens for anyone allowed any of them, so every
    // section has to ask for itself - without this a role with no staff-account access still saw every technician,
    // their email and their role, which is the sort of thing the permission is there to stop.
    public bool CanSeeUsers => store.UserCan(User, Modules.Requesters, ModulePermission.Access);
    public bool CanOpenUser => store.UserCan(User, Modules.Requesters, ModulePermission.View);
    public bool CanAddUsers => store.UserCan(User, Modules.Requesters, ModulePermission.New);
    public bool CanDeleteUsers => store.UserCan(User, Modules.Requesters, ModulePermission.Delete);
    public bool CanSeeTechnicians => store.UserCan(User, Modules.StaffAccounts, ModulePermission.Access);
    public bool CanEditTechnicians => store.UserCan(User, Modules.StaffAccounts, ModulePermission.Edit);
    public bool CanAddTechnicians => store.UserCan(User, Modules.StaffAccounts, ModulePermission.New);
    public bool CanDeleteTechnicians => store.UserCan(User, Modules.StaffAccounts, ModulePermission.Delete);
    public bool CanSeeRoles => store.UserCan(User, Modules.Roles, ModulePermission.Access);
    public bool CanEditRoles => store.UserCan(User, Modules.Roles, ModulePermission.Edit);
    public bool CanAddRoles => store.UserCan(User, Modules.Roles, ModulePermission.New);
    public bool CanDeleteRoles => store.UserCan(User, Modules.Roles, ModulePermission.Delete);

    // Adding/editing/deleting users, technicians and roles is handled by the dedicated pages under /People (linked
    // from the view below), each with its own authorization - so this page only reads.
    public void OnGet() { }

    // One chip per module the role can reach, listing its ticked actions, plus one per flag. The actions are kept
    // separate from the label so the view can set them in their own type rather than running the two together.
    public sealed record Grant(string Label, string? Actions);

    public static IReadOnlyList<Grant> Grants(RoleRecord role)
    {
        if (role.IsProtected) return [new Grant("All permissions (protected)", null)];
        return
        [
            .. Modules.All.Where(m => role.GrantsFor(m.Key) != ModulePermission.None)
                .Select(m => new Grant(m.Label, string.Join(" · ", ModulePermissions.Split(role.GrantsFor(m.Key)).Select(ModulePermissions.Label)))),
            .. Modules.Flags.All.Where(f => role.Has(f.Key)).Select(f => new Grant(f.Label, null)),
        ];
    }
}
