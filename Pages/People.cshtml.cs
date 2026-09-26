using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The directory: requesters, technician accounts and roles. It used to draw all three lists in full, one under the
// other, which stops working once a school imports its staff - hundreds of rows before the technicians even start. Now
// each is a tab, only the open one is drawn, and the two that grow (users and technicians) are searched and paged.
public class PeopleModel(HelpdeskStore store) : PageModel
{
    public const string UsersTab = "users";
    public const string TechniciansTab = "technicians";
    public const string RolesTab = "roles";
    public static readonly int[] PageSizes = [25, 50, 100];
    public const int DefaultSize = 25;

    // Short query-string names. "page" is reserved by Razor Pages routing, hence "p".
    [BindProperty(SupportsGet = true, Name = "tab")] public string? Tab { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    // Department for users, team for technicians - one field, since only one list is ever open.
    [BindProperty(SupportsGet = true, Name = "group")] public string? Group { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public string? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int Size { get; set; } = DefaultSize;

    public IReadOnlyList<RoleRecord> Roles => store.Roles;
    public IReadOnlyList<string> TechnicianTeams => store.TechnicianTeams;
    public IReadOnlyList<string> Departments => store.Departments;
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

    public sealed record TabLink(string Key, string Label, int Count);
    // Only the tabs this role can open, in a fixed order.
    public IReadOnlyList<TabLink> Tabs { get; private set; } = [];

    public IReadOnlyList<UserRecord> UserRows { get; private set; } = [];
    // People marked inactive who still have assets or kits, and how many - flagged on their row, and the "Left, still
    // holding equipment" filter finds them all.
    public IReadOnlyDictionary<Guid, int> LeaversHolding { get; private set; } = new Dictionary<Guid, int>();
    public IReadOnlyList<TechnicianRecord> TechnicianRows { get; private set; } = [];
    public int Total { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Group) || !string.IsNullOrWhiteSpace(Status);

    // Adding/editing/deleting users, technicians and roles is handled by the dedicated pages under /People (linked
    // from the view below), each with its own authorization - so this page only reads.
    public void OnGet()
    {
        var tabs = new List<TabLink>();
        if (CanSeeUsers) tabs.Add(new(UsersTab, "Users", store.Users.Count));
        if (CanSeeTechnicians) tabs.Add(new(TechniciansTab, "Technicians", store.Technicians.Count));
        if (CanSeeRoles) tabs.Add(new(RolesTab, "Roles", store.Roles.Count));
        Tabs = tabs;
        // An unknown or forbidden tab falls back to the first one this role has, rather than showing nothing.
        Tab = tabs.FirstOrDefault(x => string.Equals(x.Key, Tab, StringComparison.OrdinalIgnoreCase))?.Key ?? tabs.FirstOrDefault()?.Key;
        if (!PageSizes.Contains(Size)) Size = DefaultSize;

        if (Tab == UsersTab)
        {
            var all = store.Users;
            Total = all.Count;
            LeaversHolding = store.LeaversStillHolding();
            UserRows = Paged(all
                .Where(x => Matches(Search, x.Name, x.Email, x.Department, x.Location))
                .Where(x => string.IsNullOrWhiteSpace(Group) || string.Equals(x.Department, Group, StringComparison.OrdinalIgnoreCase))
                .Where(x => Status switch { "active" => x.IsActive, "inactive" => !x.IsActive, "leavers" => LeaversHolding.ContainsKey(x.Id), _ => true })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
        else if (Tab == TechniciansTab)
        {
            var all = store.Technicians;
            Total = all.Count;
            TechnicianRows = Paged(all
                .Where(x => Matches(Search, x.Name, x.Email, x.Team, x.Role))
                .Where(x => string.IsNullOrWhiteSpace(Group) || string.Equals(x.Team, Group, StringComparison.OrdinalIgnoreCase))
                .Where(x => Status switch { "active" => x.IsActive, "inactive" => !x.IsActive, _ => true })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
    }

    // Every word has to appear in one of the fields, so "smith science" narrows to the Smiths in Science.
    private static bool Matches(string? search, params string?[] fields)
    {
        var terms = (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(t => fields.Any(f => f?.Contains(t, StringComparison.OrdinalIgnoreCase) == true));
    }

    private List<T> Paged<T>(List<T> matches)
    {
        MatchCount = matches.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)Size));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        return matches.Skip((PageNumber - 1) * Size).Take(Size).ToList();
    }

    // Defaults are left out of the URL so a plain tab link stays short.
    public string? TabUrl(string tab) => Url.Page("/People", new { tab });
    public string? PageUrl(int page) => Url.Page("/People", new
    {
        tab = Tab,
        q = string.IsNullOrWhiteSpace(Search) ? null : Search,
        group = string.IsNullOrWhiteSpace(Group) ? null : Group,
        status = string.IsNullOrWhiteSpace(Status) ? null : Status,
        p = page == 1 ? (int?)null : page,
        size = Size == DefaultSize ? (int?)null : Size
    });

    // How many technician accounts hold each role - worth seeing before editing or deleting one.
    public int MemberCount(RoleRecord role) => store.Technicians.Count(x => string.Equals(x.Role, role.Name, StringComparison.OrdinalIgnoreCase));

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
