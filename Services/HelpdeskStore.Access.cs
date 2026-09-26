using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Roles and permissions: the seeded roles, what each role may do, and who is making the current change.
public sealed partial class HelpdeskStore
{
    // Bootstrap credentials for a brand-new install, or an existing database with no login configured yet.
    // Documented in README.md - change the password immediately after the first sign-in (RequirePasswordChange enforces this).
    public const string BootstrapAdminEmail = "admin@eduhelpdesk.local";
    public const string BootstrapAdminPassword = "ChangeMe123!";

    // Guarantees there is always at least one way to sign in as an Administrator - whether this is a brand-new
    // database (nothing seeded yet) or an existing one being upgraded to include logins for the first time.
    private void EnsureBootstrapAdministrator()
    {
        lock (_sync)
        {
            if (_data.Technicians.Any(x => x.Role == StaffRoles.Administrator)) return;
            _data.Technicians.Add(new TechnicianRecord(Guid.NewGuid(), "Administrator", BootstrapAdminEmail, "", StaffRoles.Administrator, PasswordHasher.Hash(BootstrapAdminPassword), true, true));
            SaveBaseline();
        }
    }

    // Seeds the built-in Administrator role plus the three starter roles. These are the permissions the school settled
    // on for its own three roles and asked to have as the default, so a fresh install and a factory reset both start
    // from a working desk rather than from four empty roles. Runs before EnsureBootstrapAdministrator so the
    // Administrator role row already exists when the bootstrap account is created.
    private void EnsureSeedRoles()
    {
        lock (_sync)
        {
            if (_data.Roles.Count > 0)
            {
                // A module or flag added since this database was set up is missing from the stored Administrator row.
                // It passes every check regardless, but the role editor and audit log should show what it really holds.
                // Nothing else is topped up: a new module starts unticked for every other role, so upgrading never
                // quietly grants anyone something new.
                foreach (var administrator in _data.Roles.Where(x => x.IsProtected))
                {
                    foreach (var module in Modules.All) administrator.Grants[module.Key] = module.Supports;
                    foreach (var flag in Modules.Flags.All) administrator.Flags.Add(flag.Key);
                }
                return;
            }

            // Administrator's grants are never consulted - UserCan short-circuits on the name - but they are filled in
            // anyway so the role editor and the audit log show the truth rather than an empty grid.
            var everything = Modules.All.ToDictionary(x => x.Key, x => x.Supports, StringComparer.OrdinalIgnoreCase);
            var allFlags = Modules.Flags.All.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _data.Roles.Add(new RoleRecord(StaffRoles.Administrator, IsProtected: true) { Grants = everything, Flags = allFlags });

            // Runs the whole desk, including staff accounts and roles, but Settings and the audit log stay with the
            // Administrator - configuring the system and reading who did what are deliberately not part of the job.
            _data.Roles.Add(Role("Senior Technician",
                new()
                {
                    [Modules.Tickets] = Full, [Modules.Projects] = Full, [Modules.Assets] = Full,
                    [Modules.Kits] = Full, [Modules.Loans] = Full,
                    [Modules.Parts] = Full, [Modules.Suppliers] = Full,
                    [Modules.Requesters] = Full, [Modules.StaffAccounts] = Full,
                    [Modules.Roles] = Full, [Modules.Reports] = ModulePermission.Access
                },
                Modules.Flags.WorkingAs, Modules.Flags.AssignProjects, Modules.Flags.ReportAssets, Modules.Flags.ReportTickets,
                Modules.Flags.ReportParts, Modules.Flags.ReportLoans, Modules.Flags.ReportFinance, Modules.Flags.ReportProjects,
                Modules.Flags.ReportExport));

            // Runs the inventory outright, reads the supplier directory, and cannot reach staff accounts, roles,
            // settings or the audit log.
            _data.Roles.Add(Role("Technician",
                new()
                {
                    // Works the projects they are given, but the lead decides who gets which.
                    [Modules.Tickets] = Full, [Modules.Projects] = Read | ModulePermission.Edit, [Modules.Assets] = Full,
                    [Modules.Kits] = Full, [Modules.Loans] = Full,
                    [Modules.Parts] = Full, [Modules.Suppliers] = Read,
                    [Modules.Requesters] = Write, [Modules.Reports] = ModulePermission.Access
                },
                Modules.Flags.ReportAssets, Modules.Flags.ReportTickets, Modules.Flags.ReportParts,
                Modules.Flags.ReportLoans));

            // The shape of a new starter on the desk: works tickets fully, adds and changes inventory but deletes
            // none of it, and only reads the requester directory.
            _data.Roles.Add(Role("Junior Technician",
                new()
                {
                    [Modules.Tickets] = Full, [Modules.Projects] = Read, [Modules.Assets] = Write,
                    // Can change what is in a kit but not create or scrap one.
                    [Modules.Kits] = Read | ModulePermission.Edit, [Modules.Loans] = Write,
                    [Modules.Parts] = Write, [Modules.Requesters] = Read,
                    [Modules.Reports] = ModulePermission.Access
                },
                Modules.Flags.ReportAssets, Modules.Flags.ReportParts, Modules.Flags.ReportLoans));

            SaveBaseline();
        }
    }

    // Shorthands for the seed roles only. They are not a hierarchy the rest of the code knows about - every check asks
    // for one exact action.
    private const ModulePermission Read = ModulePermission.Access | ModulePermission.View;
    private const ModulePermission Write = Read | ModulePermission.New | ModulePermission.Edit;
    private const ModulePermission Full = Write | ModulePermission.Delete;

    private static RoleRecord Role(string name, Dictionary<string, ModulePermission> grants, params string[] flags) =>
        new(name) { Grants = new(grants, StringComparer.OrdinalIgnoreCase), Flags = flags.ToHashSet(StringComparer.OrdinalIgnoreCase) };

    // 0 is the original nine on/off permissions, 2 was one stacked level per module, 3 is the current model: five
    // independent ticks per module. Anything that creates a database already in the current model stamps this, so the
    // conversion below never runs against it.
    public const int PermissionModelVersion = 3;

    // Moving to independent ticks, every existing role starts blank and is set up again by hand. That was the school's
    // choice over converting: the stacked levels granted read access generously on upgrade, so converting them would
    // have carried that generosity into a model meant to be deliberate. Administrator is left alone - it is the way
    // back in, and blanking it would lock the system.
    // Blanking is destructive, so it is gated on the stored version and does not consult the rows themselves: "this
    // role holds nothing" cannot tell a role that was blanked from one that was never converted, and re-running would
    // wipe whatever had been set up since.
    private void MigrateRolePermissions()
    {
        if (_data.PermissionModelVersion >= PermissionModelVersion || _data.Roles.Count == 0)
        {
            _data.PermissionModelVersion = PermissionModelVersion;
            return;
        }
        _data.PermissionModelVersion = PermissionModelVersion;

        foreach (var role in _data.Roles)
        {
            if (role.IsProtected)
            {
                // Administrator's grants are never consulted, but leaving the old model's rows behind would have the
                // stored data claim it holds less than it does.
                foreach (var module in Modules.All) role.Grants[module.Key] = module.Supports;
                foreach (var flag in Modules.Flags.All) role.Flags.Add(flag.Key);
                continue;
            }
            var had = ModulePermissions.Summarise(role);
            role.Grants.Clear();
            role.Flags.Clear();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Roles", "Role", role.Name, role.Name, "Permissions reset",
                "Permissions now tick each action separately, so this role was emptied and needs setting up again. It previously held: "
                + (had.Length == 0 ? "nothing" : had) + "."));
        }
    }
    public (bool Ok, string Message) AddRole(RoleRecord role)
    {
        lock (_sync)
        {
            var name = (role.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return (false, "Role name is required.");
            if (string.Equals(name, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase)) return (false, "That name is reserved for the built-in Administrator role.");
            if (_data.Roles.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return (false, "That role already exists.");
            _data.Roles.Add(role with { Name = name, IsProtected = false });
            Save();
            return (true, "Role added.");
        }
    }
    // Renaming a role cascades onto every technician holding it, the same way UpdateTechnicianTeam does for teams.
    public (bool Ok, string Message) UpdateRole(string currentName, RoleRecord role)
    {
        lock (_sync)
        {
            var oldValue = (currentName ?? string.Empty).Trim();
            var newValue = (role.Name ?? string.Empty).Trim();
            var index = _data.Roles.FindIndex(x => string.Equals(x.Name, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return (false, "Role was not found.");
            if (_data.Roles[index].IsProtected) return (false, "The Administrator role cannot be changed.");
            if (string.IsNullOrWhiteSpace(newValue)) return (false, "Role name is required.");
            var renamed = !string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
            if (renamed && string.Equals(newValue, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase)) return (false, "That name is reserved for the built-in Administrator role.");
            if (renamed && _data.Roles.Any(x => string.Equals(x.Name, newValue, StringComparison.OrdinalIgnoreCase))) return (false, "That role already exists.");
            _data.Roles[index] = role with { Name = newValue, IsProtected = false };
            if (renamed)
            {
                for (var i = 0; i < _data.Technicians.Count; i++)
                    if (string.Equals(_data.Technicians[i].Role, oldValue, StringComparison.OrdinalIgnoreCase))
                        _data.Technicians[i] = _data.Technicians[i] with { Role = newValue };
            }
            Save();
            return (true, "Role updated.");
        }
    }
    public string DeleteRole(string name)
    {
        lock (_sync)
        {
            var value = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Role name is required.";
            var index = _data.Roles.FindIndex(x => string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Role was not found.";
            if (_data.Roles[index].IsProtected) return "The Administrator role cannot be deleted.";
            if (_data.Technicians.Any(x => string.Equals(x.Role, value, StringComparison.OrdinalIgnoreCase))) return "That role cannot be deleted because technicians use it.";
            _data.Roles.RemoveAt(index);
            Save();
            return "Role deleted.";
        }
    }
    // Administrator always has every permission, regardless of what the Roles table says - the one hardcoded exception,
    // and the reason a mistake in the permission model can never lock everybody out.
    public bool RoleAllows(string? roleName, string module, ModulePermission action)
    {
        lock (_sync)
        {
            if (string.Equals(roleName, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase)) return true;
            var role = _data.Roles.FirstOrDefault(x => string.Equals(x.Name, roleName, StringComparison.OrdinalIgnoreCase));
            return role?.Allows(module, action) ?? false;
        }
    }
    public bool RoleHasFlag(string? roleName, string flag)
    {
        lock (_sync)
        {
            if (string.Equals(roleName, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase)) return true;
            var role = _data.Roles.FirstOrDefault(x => string.Equals(x.Name, roleName, StringComparison.OrdinalIgnoreCase));
            return role?.Has(flag) ?? false;
        }
    }

    // What every page, handler and nav link asks. The role name comes off the sign-in cookie and the permissions are
    // resolved live on each request, so editing a role takes effect immediately without anyone signing out.
    public bool UserCan(System.Security.Claims.ClaimsPrincipal user, string module, ModulePermission action) =>
        RoleAllows(RoleOf(user), module, action);
    // Satisfied by any one of the actions in the mask - what the combined add/edit pages need, since one page serves
    // both and either permission is enough to be on it.
    public bool UserCanAny(System.Security.Claims.ClaimsPrincipal user, string module, ModulePermission actions) =>
        ModulePermissions.Split(actions).Any(x => UserCan(user, module, x));
    public bool UserHasFlag(System.Security.Claims.ClaimsPrincipal user, string flag) =>
        RoleHasFlag(RoleOf(user), flag);
    // Everything this account holds on one module, for a page that draws several controls and would otherwise ask five
    // separate questions.
    public ModulePermission UserGrants(System.Security.Claims.ClaimsPrincipal user, string module)
    {
        if (string.Equals(RoleOf(user), StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase))
            return Modules.Find(module)?.Supports ?? ModulePermission.None;
        lock (_sync) return _data.Roles.FirstOrDefault(x => string.Equals(x.Name, RoleOf(user), StringComparison.OrdinalIgnoreCase))?.GrantsFor(module) ?? ModulePermission.None;
    }
    private static string? RoleOf(System.Security.Claims.ClaimsPrincipal user) =>
        user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

    // Who is behind the request being handled right now. Everything the store records - history lines, comments and
    // audit entries - is stamped with this, so no mutator needs an extra parameter and nothing can be recorded
    // anonymously by accident. Outside a request (startup, seeding, migrations) there is no actor and it reads "System".
    public Actor CurrentActor()
    {
        var context = _httpContext?.HttpContext;
        if (context is null) return Actor.System;
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var name = user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            if (!string.IsNullOrWhiteSpace(name))
                return new Actor(Guid.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null, name.Trim());
        }
        // A portal visitor isn't signed in to the helpdesk, so a ticket raised there would otherwise be attributed to
        // nobody. The name comes from the portal cookie, read through PortalIdentity so only a genuine one counts; the
        // actor still carries no id, because ids here are technician accounts and a requester isn't one.
        if (_portalIdentity?.Read(context.Request) is { } portalId)
        {
            lock (_sync)
            {
                if (_data.Users.FirstOrDefault(x => x.Id == portalId) is { } requester)
                    return new Actor(null, requester.Name);
            }
        }
        return Actor.System;
    }

    // Stamps the current actor onto history lines added since `from`. The activity builders stay focused on working out
    // what changed; this puts the same name on everything one action produced, without touching each line individually.
    private void StampActor(List<TicketActivity> history, int from)
    {
        var actor = CurrentActor();
        for (var i = from; i < history.Count; i++) history[i] = history[i] with { By = actor };
    }
    private void StampActor(List<AssetActivity> history, int from)
    {
        var actor = CurrentActor();
        for (var i = from; i < history.Count; i++) history[i] = history[i] with { By = actor };
    }
    // The matching stored role name (case-insensitive), or the default role for anything unrecognized (e.g. legacy data).
    public string NormalizeRoleName(string? value) { lock (_sync) return NormalizeRoleNameCore(_data, value); }
    private static string NormalizeRoleNameCore(StoreData data, string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return data.Roles.FirstOrDefault(x => string.Equals(x.Name, v, StringComparison.OrdinalIgnoreCase))?.Name ?? StaffRoles.DefaultRole;
    }
}
