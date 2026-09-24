using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Text;

namespace EduHelpdesk.Services;

public sealed partial class HelpdeskStore
{
    private readonly string _path;
    private readonly string _legacyPath;
    private readonly object _sync = new();
    private StoreData _data;
    private readonly string _templatePath;
    // Audit entries recorded by comparing the data before and after each save. Ticket and asset history is read from the records themselves.
    private readonly List<AuditEntry> _audit;
    private readonly List<AuditEntry> _pendingAudit = [];
    private AuditTracker.Snapshot? _snapshot;
    // How the store works out who is making the current change (see CurrentActor). Optional so the store can still be
    // constructed outside a web request - then there is no actor and changes are recorded against the system.
    private readonly IHttpContextAccessor? _httpContext;

    public HelpdeskStore(IHostEnvironment environment, IHttpContextAccessor? httpContext = null)
    {
        _httpContext = httpContext;
        _path = Path.Combine(environment.ContentRootPath, "App_Data", "helpdesk.db");
        _legacyPath = Path.Combine(environment.ContentRootPath, "App_Data", "helpdesk.json");
        _templatePath = Path.Combine(environment.ContentRootPath, "App_Data", "print-template.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _data = Load();
        _audit = LoadAudit();
        _data.Assets = _data.Assets.Select(x => x with
        {
            Make = x.Make ?? string.Empty,
            Model = x.Model ?? string.Empty,
            Type = x.Type ?? string.Empty,
            SerialNumber = x.SerialNumber ?? string.Empty,
            Location = x.Location ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(x.Status) ? "In use" : x.Status.Trim(),
            PurchaseOrder = x.PurchaseOrder ?? string.Empty,
            QuoteReference = x.QuoteReference ?? string.Empty,
            DisposalMethod = x.DisposalMethod ?? string.Empty,
            LoanDueDate = x.AssignedUserId.HasValue ? x.LoanDueDate : null
        }).ToList();
        _data.Suppliers ??= [];
        _data.Suppliers = _data.Suppliers.Select(x => x with
        {
            Name = x.Name?.Trim() ?? string.Empty, ContactName = x.ContactName?.Trim() ?? string.Empty,
            Email = x.Email?.Trim() ?? string.Empty, Phone = x.Phone?.Trim() ?? string.Empty,
            AddressLine1 = x.AddressLine1?.Trim() ?? string.Empty, AddressLine2 = x.AddressLine2?.Trim() ?? string.Empty,
            City = x.City?.Trim() ?? string.Empty, StateRegion = x.StateRegion?.Trim() ?? string.Empty,
            PostalCode = x.PostalCode?.Trim() ?? string.Empty, Country = x.Country?.Trim() ?? string.Empty,
            Website = x.Website?.Trim() ?? string.Empty, Notes = x.Notes?.Trim() ?? string.Empty
        }).Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        _data.Parts ??= [];
        var supplierIds = _data.Suppliers.Select(x => x.Id).ToHashSet();
        _data.Parts = _data.Parts.Select(x => x with
        {
            Location = x.Location ?? string.Empty,
            SupplierIds = (x.SupplierIds ?? []).Where(supplierIds.Contains).Distinct().ToList()
        }).ToList();
        _data.Users = _data.Users.Select(x => x with
        {
            Department = x.Department ?? string.Empty,
            Location = x.Location ?? string.Empty
        }).ToList();
        _data.Branding ??= new BrandingSettings();
        _data.AssetAttributeDefinitions ??= [];
        _data.AssetAttributeValues ??= [];
        _data.Slas ??= [];
        _data.Slas = _data.Slas.Select(x => x with { Name = x.Name?.Trim() ?? string.Empty, Duration = Math.Max(1, x.Duration), DurationUnit = NormalizeDurationUnit(x.DurationUnit), Description = string.IsNullOrWhiteSpace(x.Description) ? null : x.Description.Trim() }).Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        _data.TicketAttributeDefinitions ??= [];
        _data.TicketAttributeValues ??= [];
        _data.TicketAttributeDefinitions = _data.TicketAttributeDefinitions.Select(x => x with { Name = x.Name.Trim(), Categories = NormalizeScope(x.Categories), FieldType = NormalizeAttributeType(x.FieldType), Choices = NormalizeChoices(x.Choices) }).ToList();
        _data.AssetAttributeDefinitions = _data.AssetAttributeDefinitions.Select(x => x with
        {
            AssetTypes = NormalizeScope(x.AssetTypes),
            FieldType = NormalizeAttributeType(x.FieldType),
            Choices = NormalizeChoices(x.Choices)
        }).ToList();
        foreach (var team in _data.Technicians.Select(x => x.Team).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!_data.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase))
                _data.TechnicianTeams.Add(team);
        }
        foreach (var department in _data.Users.Select(x => x.Department).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!_data.Departments.Contains(department, StringComparer.OrdinalIgnoreCase))
                _data.Departments.Add(department);
        }
        EnsureOptions(_data.Locations, _data.Users.Select(x => x.Location).Concat(_data.Assets.Select(x => x.Location)));
        EnsureOptions(_data.AssetTypes, _data.Assets.Select(x => x.Type));
        EnsureOptions(_data.AssetMakes, _data.Assets.Select(x => x.Make));
        EnsureOptions(_data.AssetModels, _data.Assets.Select(x => x.Model));
        _data.AssetModelMakes = new Dictionary<string, string>(_data.AssetModelMakes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        EnsureFactoryOptions();
        EnsureOptions(_data.AssetStatuses, _data.Assets.Select(x => x.Status));
        EnsureOptions(_data.PartCategories, _data.Parts.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x))!);
        EnsureOptions(_data.PartLocations, _data.Parts.Select(x => x.Location).Where(x => !string.IsNullOrWhiteSpace(x)));
        _data.Parts = _data.Parts.Select(x => x with { AssetTypes = NormalizeScope(x.AssetTypes).Where(t => _data.AssetTypes.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList() }).ToList();
        _data.AssetTypeLifespans = new Dictionary<string, int>(_data.AssetTypeLifespans ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        if (_data.AssetReviewDays is < 0 or > 3650) _data.AssetReviewDays = 60;
        if (_data.AcademicYearStartMonth is < 1 or > 12) _data.AcademicYearStartMonth = AcademicYear.DefaultStartMonth;
        if (_data.TicketDueSoonHours is < 0 or > 720) _data.TicketDueSoonHours = 24;
        if (_data.PartsDefaultReorderThreshold < 0) _data.PartsDefaultReorderThreshold = 5;
        if (_data.LoanRepeatCount is < 1 or > 100) _data.LoanRepeatCount = 3;
        if (_data.LoanRepeatDays is < 1 or > 3650) _data.LoanRepeatDays = 30;
        _data.LoanKits ??= [];
        _data.KitLoans ??= [];
        _data.LoanKits = _data.LoanKits.Select(x => x with { AssetIds = (x.AssetIds ?? []).Where(id => _data.Assets.Any(a => a.Id == id)).Distinct().ToList() }).ToList();
        // Assets that already had a holder before ownership was tracked get an open period with an unknown start.
        foreach (var asset in _data.Assets.Where(x => x.AssignedUserId.HasValue && !x.Assignments.Any(a => a.EndedAt is null)))
            asset.Assignments.Add(new AssetAssignment(asset.AssignedUserId, _data.Users.FirstOrDefault(u => u.Id == asset.AssignedUserId)?.Name ?? "Unknown user", null, null, asset.LoanDueDate));
        if (_data.Users.Count == 0 && _data.Technicians.Count == 0)
        {
            SeedStarterData();
        }
        else
        {
            SaveBaseline();
        }
        // Before EnsureSeedRoles, so an upgrading database converts its existing roles rather than being mistaken for
        // a fresh install, and before EnsureBootstrapAdministrator so the Administrator role is in its final shape.
        MigrateRolePermissions();
        EnsureSeedRoles();
        EnsureBootstrapAdministrator();
        SaveBaseline();
    }

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
            if (_data.Roles.Count > 0) return;

            // Administrator's grants are never consulted - UserCan short-circuits on the name - but they are filled in
            // anyway so the role editor and the audit log show the truth rather than an empty grid.
            var everything = Modules.All.ToDictionary(x => x.Key, x => x.Supports, StringComparer.OrdinalIgnoreCase);
            var allFlags = Modules.Flags.All.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _data.Roles.Add(new RoleRecord(StaffRoles.Administrator, IsProtected: true) { Grants = everything, Flags = allFlags });

            // Everything an Administrator has, but as an ordinary editable role rather than the protected one.
            _data.Roles.Add(Role("Senior Technician",
                new()
                {
                    [Modules.Tickets] = Full, [Modules.Assets] = Full,
                    [Modules.Kits] = Full, [Modules.Loans] = Full,
                    [Modules.Parts] = Full, [Modules.Suppliers] = Full,
                    [Modules.Requesters] = Full, [Modules.StaffAccounts] = Full,
                    [Modules.Roles] = Full, [Modules.Reports] = ModulePermission.Access,
                    [Modules.Settings] = ModulePermission.Access | ModulePermission.Edit,
                    [Modules.AuditLog] = ModulePermission.Access
                },
                Modules.Flags.WorkingAs, Modules.Flags.ReportAssets, Modules.Flags.ReportTickets,
                Modules.Flags.ReportParts, Modules.Flags.ReportLoans, Modules.Flags.ReportFinance,
                Modules.Flags.ReportExport));

            // Runs the inventory outright, reads the supplier directory, and cannot reach staff accounts, roles,
            // settings or the audit log.
            _data.Roles.Add(Role("Technician",
                new()
                {
                    [Modules.Tickets] = Full, [Modules.Assets] = Full,
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
                    [Modules.Tickets] = Full, [Modules.Assets] = Write,
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

    public IReadOnlyList<UserRecord> Users { get { lock (_sync) return _data.Users; } }
    public IReadOnlyList<TechnicianRecord> Technicians { get { lock (_sync) return _data.Technicians; } }
    // Administrator (protected) first, then alphabetical.
    public IReadOnlyList<RoleRecord> Roles { get { lock (_sync) return _data.Roles.OrderByDescending(x => x.IsProtected).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(); } }
    public IReadOnlyList<string> TechnicianTeams { get { lock (_sync) return _data.TechnicianTeams.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> Departments { get { lock (_sync) return _data.Departments.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> Locations { get { lock (_sync) return _data.Locations.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> AssetTypes { get { lock (_sync) return _data.AssetTypes.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> AssetMakes { get { lock (_sync) return _data.AssetMakes.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> AssetModels { get { lock (_sync) return _data.AssetModels.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> AssetStatuses { get { lock (_sync) return _data.AssetStatuses.ToList(); } }
    public IReadOnlyList<string> PartCategories { get { lock (_sync) return _data.PartCategories.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> PartLocations { get { lock (_sync) return _data.PartLocations.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> LoanReasons { get { lock (_sync) return _data.LoanReasons.ToList(); } }
    public IReadOnlyList<LoanKit> LoanKits { get { lock (_sync) return _data.LoanKits.OrderBy(x => x.Name, NaturalComparer.Instance).ToList(); } }
    public IReadOnlyList<KitLoan> KitLoans { get { lock (_sync) return _data.KitLoans.OrderByDescending(x => x.IssuedAt).ToList(); } }
    public int LoanRepeatCount { get { lock (_sync) return _data.LoanRepeatCount; } }
    public int LoanRepeatDays { get { lock (_sync) return _data.LoanRepeatDays; } }
    public IReadOnlyDictionary<string, int> AssetTypeLifespans { get { lock (_sync) return new Dictionary<string, int>(_data.AssetTypeLifespans, StringComparer.OrdinalIgnoreCase); } }
    public int AssetReviewDays { get { lock (_sync) return _data.AssetReviewDays; } }
    public int AcademicYearStartMonth { get { lock (_sync) return _data.AcademicYearStartMonth; } }
    public int TicketDueSoonHours { get { lock (_sync) return _data.TicketDueSoonHours; } }
    // Default minimum stock level used when a part has no ReorderThreshold of its own.
    public int PartsDefaultReorderThreshold { get { lock (_sync) return _data.PartsDefaultReorderThreshold; } }
    public IReadOnlyList<TicketAttributeValue> TicketAttributeValues { get { lock (_sync) return _data.TicketAttributeValues.ToList(); } }
    // Model name -> the make it belongs to. A model with no entry can be used with any make.
    public IReadOnlyDictionary<string, string> AssetModelMakes { get { lock (_sync) return new Dictionary<string, string>(_data.AssetModelMakes, StringComparer.OrdinalIgnoreCase); } }
    public bool AssetModelMatchesMake(string? model, string? make)
    {
        lock (_sync)
            return string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model)
                || !_data.AssetModelMakes.TryGetValue(model.Trim(), out var linkedMake)
                || string.Equals(linkedMake, make.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    public IReadOnlyList<string> Categories { get { lock (_sync) return _data.Categories.ToList(); } }
    public IReadOnlyList<string> Statuses { get { lock (_sync) return _data.Statuses.ToList(); } }
    public IReadOnlyDictionary<string, string> StatusDescriptions { get { lock (_sync) return new Dictionary<string, string>(_data.StatusDescriptions, StringComparer.OrdinalIgnoreCase); } }
    public IReadOnlyList<string> Priorities { get { lock (_sync) return _data.Priorities.ToList(); } }
    public IReadOnlyList<string> RequireCloseMessagePriorities { get { lock (_sync) return _data.RequireCloseMessagePriorities.ToList(); } }
    public IReadOnlyList<string> RequireCloseMessageCategories { get { lock (_sync) return _data.RequireCloseMessageCategories.ToList(); } }
    public IReadOnlyList<AssetRecord> Assets { get { lock (_sync) return _data.Assets; } }
    public IReadOnlyList<SupplierRecord> Suppliers { get { lock (_sync) return _data.Suppliers.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<PartRecord> Parts { get { lock (_sync) return _data.Parts.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<AssetAttributeDefinition> AssetAttributeDefinitions { get { lock (_sync) return _data.AssetAttributeDefinitions.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<SlaDefinition> Slas { get { lock (_sync) return _data.Slas.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributeDefinitions { get { lock (_sync) return _data.TicketAttributeDefinitions.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<TicketRecord> Tickets { get { lock (_sync) return _data.Tickets.OrderByDescending(x => x.Number).ToList(); } }
    public BrandingSettings Branding { get { lock (_sync) return _data.Branding; } }
    public bool HasPrintTemplate => File.Exists(_templatePath);
    public IReadOnlyList<TicketAttributeDefinition> GetTicketAttributes(string category) => TicketAttributeDefinitions.Where(x => x.AppliesTo(category)).ToList();
    public IReadOnlyList<AssetAttributeDefinition> GetAssetAttributes(string assetType) => AssetAttributeDefinitions.Where(x => x.AppliesTo(assetType)).ToList();
    public IReadOnlyDictionary<Guid, string> GetTicketAttributeValues(int number) { lock (_sync) return _data.TicketAttributeValues.Where(x => x.TicketNumber == number).ToDictionary(x => x.AttributeDefinitionId, x => x.Value); }
    public static bool TechnicianInTeam(TechnicianRecord technician, string? team) => string.IsNullOrWhiteSpace(team) || string.Equals(technician.Team, team.Trim(), StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<TechnicianRecord> GetTechniciansForTeam(string? team) { lock (_sync) return _data.Technicians.Where(x => TechnicianInTeam(x, team)).ToList(); }

    public void AddUser(UserRecord item) { lock (_sync) { _data.Users.Add(item); Save(); } }
    public void AddTechnician(TechnicianRecord item) { lock (_sync) { _data.Technicians.Add(item); Save(); } }
    public string AddTechnicianTeam(string team)
    {
        lock (_sync)
        {
            var value = team.Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Team name is required.";
            if (_data.TechnicianTeams.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "That team already exists.";
            _data.TechnicianTeams.Add(value);
            Save();
            return "Technician team added.";
        }
    }
    public string UpdateTechnicianTeam(string currentTeam, string team)
    {
        lock (_sync)
        {
            var oldValue = currentTeam.Trim();
            var newValue = team.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return "Team name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
                return "Technician team updated.";
            if (_data.TechnicianTeams.Contains(newValue, StringComparer.OrdinalIgnoreCase))
                return "That team already exists.";
            var index = _data.TechnicianTeams.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Technician team was not found.";
            _data.TechnicianTeams[index] = newValue;
            for (var i = 0; i < _data.Technicians.Count; i++)
            {
                if (string.Equals(_data.Technicians[i].Team, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Technicians[i] = _data.Technicians[i] with { Team = newValue };
            }
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                if (string.Equals(_data.Tickets[i].TeamName, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Tickets[i] = _data.Tickets[i] with { TeamName = newValue };
            }
            Save();
            return "Technician team updated.";
        }
    }
    public string DeleteTechnicianTeam(string team)
    {
        lock (_sync)
        {
            var value = (team ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Team name is required.";
            var index = _data.TechnicianTeams.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Technician team was not found.";
            if (_data.Technicians.Any(x => string.Equals(x.Team, value, StringComparison.OrdinalIgnoreCase))) return "That team cannot be deleted because technicians use it.";
            _data.TechnicianTeams.RemoveAt(index);
            Save();
            return "Technician team deleted.";
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
        // The staff portal has no login at all, so a ticket raised there would otherwise be attributed to nobody. Its
        // cookie is self-declared rather than authenticated, which is why the actor carries no id - see PortalIdentity.
        if (Guid.TryParse(context.Request.Cookies[PortalIdentity.CookieName], out var portalId))
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
    public string AddDepartment(string department)
    {
        lock (_sync)
        {
            var value = department.Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Department name is required.";
            if (_data.Departments.Contains(value, StringComparer.OrdinalIgnoreCase)) return "That department already exists.";
            _data.Departments.Add(value);
            Save();
            return "Department added.";
        }
    }
    public string UpdateDepartment(string currentDepartment, string department)
    {
        lock (_sync)
        {
            var oldValue = currentDepartment.Trim();
            var newValue = department.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return "Department name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return "Department updated.";
            if (_data.Departments.Contains(newValue, StringComparer.OrdinalIgnoreCase)) return "That department already exists.";
            var index = _data.Departments.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Department was not found.";
            _data.Departments[index] = newValue;
            for (var i = 0; i < _data.Users.Count; i++)
            {
                if (string.Equals(_data.Users[i].Department, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Users[i] = _data.Users[i] with { Department = newValue };
            }
            Save();
            return "Department updated.";
        }
    }
    public string DeleteDepartment(string department)
    {
        lock (_sync)
        {
            var value = (department ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Department name is required.";
            var index = _data.Departments.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Department was not found.";
            if (_data.Users.Any(x => string.Equals(x.Department, value, StringComparison.OrdinalIgnoreCase))) return "That department cannot be deleted because users belong to it.";
            _data.Departments.RemoveAt(index);
            Save();
            return "Department deleted.";
        }
    }
    public string AddManagedOption(string kind, string value)
    {
        lock (_sync)
        {
            kind = NormalizeManagedOptionKind(kind);
            if (!IsManagedOptionKind(kind)) return "Invalid managed option.";
            var options = GetManagedOptions(kind);
            var item = value.Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            if (options.Contains(item, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            options.Add(item);
            Save();
            return $"{kind} added.";
        }
    }
    public string UpdateManagedOption(string kind, string currentValue, string value)
    {
        lock (_sync)
        {
            kind = NormalizeManagedOptionKind(kind);
            if (!IsManagedOptionKind(kind)) return "Invalid managed option.";
            var options = GetManagedOptions(kind);
            var oldValue = currentValue.Trim();
            var newValue = value.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return $"{kind} name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return $"{kind} updated.";
            if (options.Contains(newValue, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            var index = options.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            options[index] = newValue;
            for (var i = 0; i < _data.Users.Count; i++)
                if (kind == "Location" && string.Equals(_data.Users[i].Location, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Users[i] = _data.Users[i] with { Location = newValue };
            for (var i = 0; i < _data.Assets.Count; i++)
            {
                var asset = _data.Assets[i];
                _data.Assets[i] = kind switch
                {
                    "Location" when string.Equals(asset.Location, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Location = newValue },
                    "Asset make" when string.Equals(asset.Make, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Make = newValue },
                    "Asset type" when string.Equals(asset.Type, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Type = newValue },
                    "Asset model" when string.Equals(asset.Model, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Model = newValue },
                    "Asset status" when string.Equals(asset.Status, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Status = newValue },
                    _ => asset
                };
            }
            if (kind == "Asset type")
            {
                for (var i = 0; i < _data.AssetAttributeDefinitions.Count; i++)
                    _data.AssetAttributeDefinitions[i] = _data.AssetAttributeDefinitions[i] with { AssetTypes = RenameInScope(_data.AssetAttributeDefinitions[i].AssetTypes, oldValue, newValue) };
                for (var i = 0; i < _data.Parts.Count; i++)
                    _data.Parts[i] = _data.Parts[i] with { AssetTypes = RenameInScope(_data.Parts[i].AssetTypes, oldValue, newValue) };
            }
            if (kind == "Asset model" && _data.AssetModelMakes.Remove(oldValue, out var linkedMake))
                _data.AssetModelMakes[newValue] = linkedMake;
            if (kind == "Asset type" && _data.AssetTypeLifespans.Remove(oldValue, out var lifespan))
                _data.AssetTypeLifespans[newValue] = lifespan;
            if (kind == "Asset make")
            {
                foreach (var model in _data.AssetModelMakes.Where(x => string.Equals(x.Value, oldValue, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToList())
                    _data.AssetModelMakes[model] = newValue;
            }
            for (var i = 0; i < _data.Parts.Count; i++)
            {
                var part = _data.Parts[i];
                _data.Parts[i] = kind switch
                {
                    "Part category" when string.Equals(part.Category, oldValue, StringComparison.OrdinalIgnoreCase) => part with { Category = newValue },
                    "Part location" when string.Equals(part.Location, oldValue, StringComparison.OrdinalIgnoreCase) => part with { Location = newValue },
                    _ => part
                };
            }
            if (kind == "Loan reason")
            {
                for (var i = 0; i < _data.KitLoans.Count; i++)
                    if (string.Equals(_data.KitLoans[i].Reason, oldValue, StringComparison.OrdinalIgnoreCase))
                        _data.KitLoans[i] = _data.KitLoans[i] with { Reason = newValue };
            }
            Save();
            return $"{kind} updated.";
        }
    }
    public string DeleteManagedOption(string kind, string value)
    {
        lock (_sync)
        {
            kind = NormalizeManagedOptionKind(kind);
            if (!IsManagedOptionKind(kind)) return "Invalid managed option.";
            var item = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            var options = GetManagedOptions(kind);
            var index = options.FindIndex(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            var inUse = kind switch
            {
                "Location" => _data.Users.Any(x => string.Equals(x.Location, item, StringComparison.OrdinalIgnoreCase)) || _data.Assets.Any(x => string.Equals(x.Location, item, StringComparison.OrdinalIgnoreCase)),
                "Asset make" => _data.Assets.Any(x => string.Equals(x.Make, item, StringComparison.OrdinalIgnoreCase)) || _data.AssetModelMakes.Values.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase)),
                "Asset type" => _data.Assets.Any(x => string.Equals(x.Type, item, StringComparison.OrdinalIgnoreCase)) || _data.AssetAttributeDefinitions.Any(x => x.AssetTypes.Contains(item, StringComparer.OrdinalIgnoreCase)) || _data.Parts.Any(x => x.AssetTypes.Contains(item, StringComparer.OrdinalIgnoreCase)),
                "Asset model" => _data.Assets.Any(x => string.Equals(x.Model, item, StringComparison.OrdinalIgnoreCase)),
                "Asset status" => _data.Assets.Any(x => string.Equals(x.Status, item, StringComparison.OrdinalIgnoreCase)),
                "Part category" => _data.Parts.Any(x => string.Equals(x.Category, item, StringComparison.OrdinalIgnoreCase)),
                "Part location" => _data.Parts.Any(x => string.Equals(x.Location, item, StringComparison.OrdinalIgnoreCase)),
                "Loan reason" => _data.KitLoans.Any(x => string.Equals(x.Reason, item, StringComparison.OrdinalIgnoreCase)),
                _ => false
            };
            if (inUse) return $"That {kind.ToLowerInvariant()} cannot be deleted because it is in use.";
            options.RemoveAt(index);
            if (kind == "Asset model") _data.AssetModelMakes.Remove(item);
            if (kind == "Asset type") _data.AssetTypeLifespans.Remove(item);
            Save();
            return $"{kind} deleted.";
        }
    }
    public string AddAssetType(string name, int? lifespanYears)
    {
        lock (_sync)
        {
            if (lifespanYears is < 1 or > 50) return "Enter a lifespan between 1 and 50 years, or leave it blank.";
            var message = AddManagedOption("Asset type", name);
            if (message != "Asset type added.") return message;
            if (!lifespanYears.HasValue) return message;
            var saved = SetAssetTypeLifespan(name, lifespanYears);
            return saved == "Asset type updated." ? message : saved;
        }
    }
    public string UpdateAssetType(string currentType, string name, int? lifespanYears)
    {
        lock (_sync)
        {
            if (lifespanYears is < 1 or > 50) return "Enter a lifespan between 1 and 50 years, or leave it blank.";
            var oldName = (currentType ?? string.Empty).Trim();
            var message = UpdateManagedOption("Asset type", oldName, name);
            if (message != "Asset type updated.") return message;
            var finalName = string.IsNullOrWhiteSpace(name) || string.Equals(oldName, name.Trim(), StringComparison.OrdinalIgnoreCase) ? oldName : name.Trim();
            return SetAssetTypeLifespan(finalName, lifespanYears);
        }
    }
    // A blank lifespan (null) removes it, so replacement dates for that type are only those typed on assets.
    public string SetAssetTypeLifespan(string assetType, int? years)
    {
        lock (_sync)
        {
            var name = _data.AssetTypes.FirstOrDefault(x => string.Equals(x, (assetType ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return "Asset type was not found.";
            if (years is < 1 or > 50) return "Enter a lifespan between 1 and 50 years, or leave it blank.";
            if (years is null) _data.AssetTypeLifespans.Remove(name);
            else _data.AssetTypeLifespans[name] = years.Value;
            Save();
            return "Asset type updated.";
        }
    }
    public string SetAssetReviewDays(int days)
    {
        lock (_sync)
        {
            if (days is < 0 or > 3650) return "Enter a number of days between 0 and 3650.";
            _data.AssetReviewDays = days;
            Save();
            return "Asset review window saved.";
        }
    }
    public string SetAcademicYearStartMonth(int month)
    {
        lock (_sync)
        {
            if (month is < 1 or > 12) return "Choose the month the academic year starts in.";
            _data.AcademicYearStartMonth = month;
            Save();
            return $"Academic year now starts in {AcademicYear.Months.First(x => x.Month == month).Name}.";
        }
    }
    public string SetTicketDueSoonHours(int hours)
    {
        lock (_sync)
        {
            if (hours is < 0 or > 720) return "Enter a number of hours between 0 and 720.";
            _data.TicketDueSoonHours = hours;
            Save();
            return "Due soon window saved.";
        }
    }
    public string SetPartsDefaultReorderThreshold(int threshold)
    {
        lock (_sync)
        {
            if (threshold < 0) return "Enter a reorder threshold of 0 or more.";
            _data.PartsDefaultReorderThreshold = threshold;
            Save();
            return "Parts reorder threshold saved.";
        }
    }
    public string AddAssetModel(string name, string? make)
    {
        lock (_sync)
        {
            var item = (name ?? string.Empty).Trim();
            if (item.Length == 0) return "Asset model name is required.";
            if (_data.AssetModels.Contains(item, StringComparer.OrdinalIgnoreCase)) return "That asset model already exists.";
            var linkedMake = FindMake(make, out var validMake);
            if (!validMake) return "Select a valid make.";
            _data.AssetModels.Add(item);
            if (linkedMake is not null) _data.AssetModelMakes[item] = linkedMake;
            Save();
            return "Asset model added.";
        }
    }
    // Renames a model and/or changes the make it belongs to. A blank make means the model can be used with any make.
    public string UpdateAssetModel(string currentModel, string name, string? make)
    {
        lock (_sync)
        {
            var oldName = (currentModel ?? string.Empty).Trim();
            FindMake(make, out var validMake);
            if (!validMake) return "Select a valid make.";
            var message = UpdateManagedOption("Asset model", oldName, name);
            if (message != "Asset model updated.") return message;
            var finalName = string.IsNullOrWhiteSpace(name) || string.Equals(oldName, name.Trim(), StringComparison.OrdinalIgnoreCase) ? oldName : name.Trim();
            return SetAssetModelMake(finalName, make);
        }
    }
    public string SetAssetModelMake(string model, string? make)
    {
        lock (_sync)
        {
            var name = _data.AssetModels.FirstOrDefault(x => string.Equals(x, (model ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return "Asset model was not found.";
            var linkedMake = FindMake(make, out var validMake);
            if (!validMake) return "Select a valid make.";
            if (linkedMake is null) _data.AssetModelMakes.Remove(name);
            else _data.AssetModelMakes[name] = linkedMake;
            Save();
            return "Asset model updated.";
        }
    }
    // Returns the configured make matching the text (null for blank). valid is false when a non-blank make is not configured.
    private string? FindMake(string? make, out bool valid)
    {
        var requested = (make ?? string.Empty).Trim();
        valid = true;
        if (requested.Length == 0) return null;
        var match = _data.AssetMakes.FirstOrDefault(x => string.Equals(x, requested, StringComparison.OrdinalIgnoreCase));
        valid = match is not null;
        return match;
    }
    public (int Imported, int Linked, int Skipped) ImportAssetModels(IEnumerable<(string Name, string? Make)> rows)
    {
        lock (_sync)
        {
            var imported = 0;
            var linked = 0;
            var skipped = 0;
            foreach (var (rawName, rawMake) in rows)
            {
                var name = (rawName ?? string.Empty).Trim();
                var make = (rawMake ?? string.Empty).Trim();
                if (name.Length == 0) { skipped++; continue; }
                var existing = _data.AssetModels.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null && (make.Length == 0 || _data.AssetModelMakes.ContainsKey(existing))) { skipped++; continue; }
                string? linkedMake = null;
                if (make.Length > 0)
                {
                    linkedMake = _data.AssetMakes.FirstOrDefault(x => string.Equals(x, make, StringComparison.OrdinalIgnoreCase));
                    if (linkedMake is null) { _data.AssetMakes.Add(make); linkedMake = make; }
                }
                if (existing is null)
                {
                    _data.AssetModels.Add(name);
                    if (linkedMake is not null) _data.AssetModelMakes[name] = linkedMake;
                    imported++;
                }
                else
                {
                    _data.AssetModelMakes[existing] = linkedMake!;
                    linked++;
                }
            }
            if (imported > 0 || linked > 0) Save();
            return (imported, linked, skipped);
        }
    }
    public string AddTicketOption(string kind, string value)
    {
        lock (_sync)
        {
            var options = GetOptions(kind);
            var item = value.Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            if (options.Contains(item, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            options.Add(item);
            Save();
            return $"{kind} added.";
        }
    }
    public string UpdateTicketOption(string kind, string currentValue, string value)
    {
        lock (_sync)
        {
            var options = GetOptions(kind);
            var oldValue = currentValue.Trim();
            var newValue = value.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return $"{kind} name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return $"{kind} updated.";
            if (options.Contains(newValue, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            var index = options.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            options[index] = newValue;
            if (kind == "Status" && _data.StatusDescriptions.Remove(oldValue, out var existingDescription))
                _data.StatusDescriptions[newValue] = existingDescription;
            if (kind == "Priority")
            {
                var priorityIndex = _data.RequireCloseMessagePriorities.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
                if (priorityIndex >= 0) _data.RequireCloseMessagePriorities[priorityIndex] = newValue;
            }
            if (kind == "Category")
            {
                var categoryIndex = _data.RequireCloseMessageCategories.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
                if (categoryIndex >= 0) _data.RequireCloseMessageCategories[categoryIndex] = newValue;
                for (var i = 0; i < _data.TicketAttributeDefinitions.Count; i++)
                    _data.TicketAttributeDefinitions[i] = _data.TicketAttributeDefinitions[i] with { Categories = RenameInScope(_data.TicketAttributeDefinitions[i].Categories, oldValue, newValue) };
            }
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                _data.Tickets[i] = kind switch
                {
                    "Category" when string.Equals(ticket.Category, oldValue, StringComparison.OrdinalIgnoreCase) => ticket with { Category = newValue },
                    "Status" when string.Equals(ticket.Status, oldValue, StringComparison.OrdinalIgnoreCase) => ticket with { Status = newValue },
                    "Priority" when string.Equals(ticket.Priority, oldValue, StringComparison.OrdinalIgnoreCase) => ticket with { Priority = newValue },
                    _ => ticket
                };
            }
            Save();
            return $"{kind} updated.";
        }
    }
    public string DeleteTicketOption(string kind, string value)
    {
        lock (_sync)
        {
            if (!IsTicketOptionKind(kind)) return "Invalid ticket option.";
            var item = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            var options = GetOptions(kind);
            var index = options.FindIndex(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            if (_data.Tickets.Any(x => kind switch
            {
                "Category" => string.Equals(x.Category, item, StringComparison.OrdinalIgnoreCase),
                "Status" => string.Equals(x.Status, item, StringComparison.OrdinalIgnoreCase),
                "Priority" => string.Equals(x.Priority, item, StringComparison.OrdinalIgnoreCase),
                _ => false
            })) return $"That {kind.ToLowerInvariant()} cannot be deleted because tickets use it.";
            if (kind == "Category" && _data.TicketAttributeDefinitions.Any(x => x.Categories.Contains(item, StringComparer.OrdinalIgnoreCase)))
                return "That category cannot be deleted because a ticket custom attribute uses it.";
            options.RemoveAt(index);
            if (kind == "Status") _data.StatusDescriptions.Remove(item);
            if (kind == "Priority") _data.RequireCloseMessagePriorities.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (kind == "Category") _data.RequireCloseMessageCategories.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            Save();
            return $"{kind} deleted.";
        }
    }
    public string SetStatusDescription(string status, string? description)
    {
        lock (_sync)
        {
            var name = (status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return "Status is required.";
            if (!_data.Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)) return "Status was not found.";
            var text = (description ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) _data.StatusDescriptions.Remove(name);
            else _data.StatusDescriptions[name] = text;
            Save();
            return "Status description saved.";
        }
    }
    public string SetCloseMessageRequirements(IEnumerable<string>? priorities, IEnumerable<string>? categories)
    {
        lock (_sync)
        {
            _data.RequireCloseMessagePriorities = (priorities ?? [])
                .Select(x => x.Trim())
                .Where(x => _data.Priorities.Contains(x, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _data.RequireCloseMessageCategories = (categories ?? [])
                .Select(x => x.Trim())
                .Where(x => _data.Categories.Contains(x, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Save();
            return "Closing requirements saved.";
        }
    }
    public bool RequiresCloseMessage(TicketRecord ticket)
    {
        lock (_sync)
        {
            return _data.RequireCloseMessagePriorities.Contains(ticket.Priority, StringComparer.OrdinalIgnoreCase)
                || _data.RequireCloseMessageCategories.Contains(ticket.Category, StringComparer.OrdinalIgnoreCase);
        }
    }
    public void AddAsset(AssetRecord item)
    {
        lock (_sync)
        {
            AddAssetCore(item);
            Save();
        }
    }
    // Adds the asset with its first ownership period. The caller holds the lock and saves.
    private void AddAssetCore(AssetRecord item)
    {
        if (string.IsNullOrWhiteSpace(item.Status)) item = item with { Status = "In use" };
        if (!item.AssignedUserId.HasValue) item = item with { LoanDueDate = null };
        var assignments = item.AssignedUserId is { } holder
            ? new List<AssetAssignment> { new(holder, UserName(holder), DateTime.UtcNow, null, item.LoanDueDate) }
            : [];
        _data.Assets.Add(item with { Assignments = assignments });
    }
    // Null when the tag is free to use; otherwise the reason it can't be. Tags are compared ignoring case and surrounding spaces.
    public string? CheckAssetTag(string? tag, Guid? excludeAssetId)
    {
        lock (_sync)
        {
            var value = (tag ?? string.Empty).Trim();
            return _data.Assets.Any(x => x.Id != excludeAssetId && string.Equals(x.AssetTag, value, StringComparison.OrdinalIgnoreCase))
                ? $"Asset tag {value} is already used by another asset."
                : null;
        }
    }
    // Null when the email is free to use as a technician login; otherwise the reason it can't be. Email doubles as the sign-in username.
    public string? CheckTechnicianEmail(string? email, Guid? excludeTechnicianId)
    {
        lock (_sync)
        {
            var value = (email ?? string.Empty).Trim();
            if (value.Length == 0) return "Email is required.";
            return _data.Technicians.Any(x => x.Id != excludeTechnicianId && string.Equals(x.Email, value, StringComparison.OrdinalIgnoreCase))
                ? $"{value} is already used by another technician."
                : null;
        }
    }
    // Null when the email is free to use as a portal login; otherwise the reason it can't be. Email doubles as the portal sign-in username.
    public string? CheckUserEmail(string? email, Guid? excludeUserId)
    {
        lock (_sync)
        {
            var value = (email ?? string.Empty).Trim();
            if (value.Length == 0) return "Email is required.";
            return _data.Users.Any(x => x.Id != excludeUserId && string.Equals(x.Email, value, StringComparison.OrdinalIgnoreCase))
                ? $"{value} is already used by another user."
                : null;
        }
    }
    // A repeated serial number is allowed but worth a warning; returns the other asset that has it.
    public AssetRecord? FindDuplicateSerial(string? serialNumber, Guid? excludeAssetId)
    {
        lock (_sync)
        {
            var value = (serialNumber ?? string.Empty).Trim();
            return value.Length == 0 ? null : _data.Assets.FirstOrDefault(x => x.Id != excludeAssetId && string.Equals(x.SerialNumber, value, StringComparison.OrdinalIgnoreCase));
        }
    }
    // A repeated part SKU is allowed but worth a warning; returns the other part that has it.
    public PartRecord? FindDuplicateSku(string? sku, Guid? excludePartId)
    {
        lock (_sync)
        {
            var value = (sku ?? string.Empty).Trim();
            return value.Length == 0 ? null : _data.Parts.FirstOrDefault(x => x.Id != excludePartId && string.Equals(x.Sku, value, StringComparison.OrdinalIgnoreCase));
        }
    }
    private string UserName(Guid userId) => _data.Users.FirstOrDefault(x => x.Id == userId)?.Name ?? "Unknown user";
    // The two statuses the loan kit feature sets by itself. Looked up rather than assumed, because a school can rename
    // or delete any asset status - if one is missing, the asset keeps the status it already had.
    private const string OnLoanStatus = "On loan";
    private const string InStockStatus = "In stock or spare";
    public const string DisposedStatus = "Disposed";
    // A disposed asset has left the estate. It stays in the register for audit, but should not turn up anywhere that
    // implies it is still usable - see the guards in LoanAsset, IssueKit and LinkAssetToTicket, and the list filters.
    public static bool IsDisposed(AssetRecord asset) => string.Equals(asset.Status, DisposedStatus, StringComparison.OrdinalIgnoreCase);
    private string? ResolveAssetStatus(string name) => _data.AssetStatuses.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    // The kit currently holding this asset, if a kit containing it is out on loan. An asset that went out inside a kit
    // has to come back the same way: booking it in on its own would leave the kit saying it still has the equipment.
    public (LoanKit Kit, KitLoan Loan)? KitLoanHolding(Guid assetId)
    {
        lock (_sync) return KitLoanHoldingCore(assetId);
    }
    // Any kit this asset belongs to, loaned out or not. Kit equipment is only ever lent as part of its kit, so the
    // per-asset loan feature is off for these entirely - not just while the kit happens to be out.
    public LoanKit? KitContaining(Guid assetId)
    {
        lock (_sync) return KitContainingCore(assetId);
    }
    private LoanKit? KitContainingCore(Guid assetId) => _data.LoanKits.FirstOrDefault(x => x.AssetIds.Contains(assetId));
    private (LoanKit Kit, KitLoan Loan)? KitLoanHoldingCore(Guid assetId)
    {
        foreach (var kit in _data.LoanKits.Where(x => x.AssetIds.Contains(assetId)))
            if (_data.KitLoans.FirstOrDefault(x => x.KitId == kit.Id && x.ReturnedAt is null) is { } loan)
                return (kit, loan);
        return null;
    }
    // A reason is required, from the same list kit loans use, so an individual loan can be told apart from a kit one in
    // the repeat-borrower report. Borrowers must be in the directory here - unlike kit loans, which accept a typed name.
    public (bool Ok, string Message) LoanAsset(Guid assetId, Guid userId, DateOnly dueBack, string? reason)
    {
        lock (_sync)
        {
            var chosenReason = _data.LoanReasons.FirstOrDefault(x => string.Equals(x, (reason ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosenReason is null) return (false, "Choose a reason for the loan.");
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null) return (false, "Asset was not found.");
            if (IsDisposed(asset)) return (false, $"{asset.AssetTag} has been disposed of and cannot be loaned out.");
            // Kit equipment is lent as a kit or not at all, whether or not the kit is currently out. Two reasons: the
            // kit would otherwise show as available while its laptop is on someone's desk, and loaning it separately
            // was also a way round the return block - loan it out, then book it back in.
            if (KitContainingCore(assetId) is { } owningKit)
                return (false, KitLoanHoldingCore(assetId) is { } held
                    ? $"This asset is out on loan with {owningKit.Name} ({held.Loan.BorrowerName}). Book the kit back in from the Loans page before loaning it separately."
                    : $"This asset is part of {owningKit.Name} and is only loaned out by issuing that kit from the Loans page. Remove it from the kit first if it needs to be loaned on its own.");
            if (!_data.Users.Any(x => x.Id == userId)) return (false, "Select who the device is loaned to.");
            if (dueBack < AssetInsights.Today) return (false, "The due-back date cannot be in the past.");
            var status = ResolveAssetStatus(OnLoanStatus) ?? asset.Status;
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            ApplyAssetUpdate(index, asset with { AssignedUserId = userId, LoanDueDate = dueBack, Status = status }, chosenReason);
            Save();
            return (true, $"{asset.AssetTag} loaned to {UserName(userId)}, due back {AssetInsights.Format(dueBack)}.");
        }
    }
    // Takes an asset out of the estate. Deliberately not a delete: DeleteAsset removes the row, and an auditor needs
    // the record to survive - what it cost, when it was bought and what became of it.
    // The holder is cleared as well, because DeleteUser refuses while any asset is assigned to somebody, and a scrapped
    // laptop still showing a leaver as its holder would block deleting them for good.
    public (bool Ok, string Message) DisposeAsset(Guid assetId, DateOnly? date, string? method, decimal? proceeds)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return (false, "Asset was not found.");
            var asset = _data.Assets[index];
            if (IsDisposed(asset)) return (false, $"{asset.AssetTag} is already recorded as disposed.");
            if (date is not { } disposedOn) return (false, "Enter the date it was disposed of.");
            if (disposedOn > AssetInsights.Today) return (false, "The disposal date cannot be in the future.");
            if (string.IsNullOrWhiteSpace(method)) return (false, "Choose how it was disposed of.");
            // Something still out with somebody, or sitting in a kit, is not ready to be written off.
            if (KitLoanHoldingCore(assetId) is { } held)
                return (false, $"{asset.AssetTag} is out on loan with {held.Kit.Name}. Book the kit back in first.");
            if (KitContainingCore(assetId) is { } kit)
                return (false, $"{asset.AssetTag} is part of {kit.Name}. Take it out of the kit before disposing of it.");
            if (asset.LoanDueDate is not null)
                return (false, $"{asset.AssetTag} is out on loan. Book it back in first.");

            var status = ResolveAssetStatus(DisposedStatus) ?? asset.Status;
            ApplyAssetUpdate(index, asset with
            {
                Status = status,
                AssignedUserId = null,
                LoanDueDate = null,
                DisposalDate = disposedOn,
                DisposalMethod = method.Trim(),
                DisposalProceeds = proceeds
            });
            Save();
            return (true, $"{asset.AssetTag} recorded as disposed on {AssetInsights.Format(disposedOn)}.");
        }
    }

    // The methods offered when disposing of an asset. Fixed rather than a managed list: they map to how a school
    // actually accounts for kit leaving, and the finance report groups on them.
    public static readonly string[] DisposalMethods = ["Sold", "Recycled (WEEE)", "Donated", "Written off", "Lost or stolen"];

    // Ends the current holder's period. The status can be set at the same time, for example back to stock.
    public string ReturnAsset(Guid assetId, string? status)
    {
        lock (_sync)
        {
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null) return "Asset was not found.";
            // Checked before the "not assigned" test: a kit issued to someone outside the directory leaves no holder on
            // the asset, so that check alone would let this one through with a misleading message.
            if (KitLoanHoldingCore(assetId) is { } held)
                return $"This asset is out on loan with {held.Kit.Name} ({held.Loan.BorrowerName}). Book the kit back in from the Loans page instead.";
            if (!asset.AssignedUserId.HasValue) return "This asset is not currently assigned to anyone.";
            var newStatus = asset.Status;
            if (!string.IsNullOrWhiteSpace(status))
            {
                var match = _data.AssetStatuses.FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return "Select a valid status.";
                newStatus = match;
            }
            var holder = UserName(asset.AssignedUserId.Value);
            UpdateAsset(asset with { AssignedUserId = null, LoanDueDate = null, Status = newStatus });
            return $"Returned by {holder}.";
        }
    }
    public void AddSupplier(SupplierRecord item) { lock (_sync) { _data.Suppliers.Add(item); Save(); } }
    public bool UpdateSupplier(SupplierRecord item) => Update(item, _data.Suppliers, x => x.Id == item.Id);
    public string? DeleteSupplier(Guid id)
    {
        lock (_sync)
        {
            if (_data.Assets.Any(x => x.SupplierId == id)) return "This supplier is linked to assets and cannot be deleted.";
            if (_data.Parts.Any(x => x.SupplierIds.Contains(id))) return "This supplier is linked to parts and cannot be deleted.";
            var item = _data.Suppliers.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Supplier was not found.";
            _data.Suppliers.Remove(item); Save(); return null;
        }
    }
    public void AddPart(PartRecord item) { lock (_sync) { _data.Parts.Add(item); Save(); } }
    public bool UpdatePart(PartRecord item) => Update(item, _data.Parts, x => x.Id == item.Id);
    public string? DeletePart(Guid id)
    {
        lock (_sync)
        {
            if (_data.TicketParts.Any(x => x.PartId == id)) return "This part is assigned to a ticket and cannot be deleted.";
            var item = _data.Parts.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Part was not found.";
            _data.Parts.Remove(item); Save(); return null;
        }
    }
    public sealed record PartBulkChange(bool ChangeCategory, string? Category, bool ChangeLocation, string? Location);

    // Applies the same change to many parts and saves once, however many there are. Parts the change would not alter are counted, not touched.
    public (int Updated, int Unchanged, string? Error) BulkUpdateParts(IEnumerable<Guid> partIds, PartBulkChange change)
    {
        lock (_sync)
        {
            if (!change.ChangeCategory && !change.ChangeLocation) return (0, 0, "Choose what to change.");
            var category = string.Empty;
            if (change.ChangeCategory && !string.IsNullOrWhiteSpace(change.Category) && change.Category != PartListQuery.None)
            {
                var match = _data.PartCategories.FirstOrDefault(x => string.Equals(x, change.Category.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return (0, 0, "Select a valid category.");
                category = match;
            }
            var location = string.Empty;
            if (change.ChangeLocation && !string.IsNullOrWhiteSpace(change.Location) && change.Location != PartListQuery.None)
            {
                var match = _data.PartLocations.FirstOrDefault(x => string.Equals(x, change.Location.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return (0, 0, "Select a valid location.");
                location = match;
            }

            var updated = 0;
            var unchanged = 0;
            foreach (var id in partIds.Distinct())
            {
                var index = _data.Parts.FindIndex(x => x.Id == id);
                if (index < 0) continue;
                var part = _data.Parts[index];
                var next = part;
                if (change.ChangeCategory) next = next with { Category = category };
                if (change.ChangeLocation) next = next with { Location = location };
                if (next == part) { unchanged++; continue; }
                _data.Parts[index] = next;
                updated++;
            }
            if (updated > 0) Save();
            return (updated, unchanged, null);
        }
    }
    // Deletes many parts and saves once. A part still assigned to a ticket is skipped, same guard as DeletePart.
    public (int Deleted, int Skipped) BulkDeleteParts(IEnumerable<Guid> partIds)
    {
        lock (_sync)
        {
            var deleted = 0;
            var skipped = 0;
            foreach (var id in partIds.Distinct())
            {
                var item = _data.Parts.FirstOrDefault(x => x.Id == id);
                if (item is null) continue;
                if (_data.TicketParts.Any(x => x.PartId == id)) { skipped++; continue; }
                _data.Parts.Remove(item);
                deleted++;
            }
            if (deleted > 0) Save();
            return (deleted, skipped);
        }
    }
    // A dedicated, reasoned way to change QuantityOnHand, logged on the part's own History - unlike every other Part
    // field, which is just quietly diffed for the audit log. Replaces free editing of the field on the Edit page.
    public string? AdjustPartStock(Guid id, int newQuantity, string? reason)
    {
        lock (_sync)
        {
            if (newQuantity < 0) return "Enter a quantity of 0 or more.";
            if (string.IsNullOrWhiteSpace(reason)) return "Enter a reason for the adjustment.";
            var index = _data.Parts.FindIndex(x => x.Id == id);
            if (index < 0) return "Part was not found.";
            var part = _data.Parts[index];
            if (newQuantity == part.QuantityOnHand) return "That's already the quantity on hand.";
            var history = part.History.ToList();
            history.Add(new PartActivity("Stock adjusted", $"{part.QuantityOnHand} -> {newQuantity} ({reason.Trim()})", DateTime.UtcNow) { By = CurrentActor() });
            _data.Parts[index] = part with { QuantityOnHand = newQuantity, History = history };
            Save();
            return null;
        }
    }
    // ---- Loan kits ---------------------------------------------------------
    // Kit contents are held for reference (so you can see which laptop is in a kit). Issuing a kit deliberately does
    // not touch the assets' own AssignedUserId/LoanDueDate, so kit loans and per-asset loans can't fight each other.
    public (bool Ok, string Message) AddLoanKit(string name, string? notes, IEnumerable<Guid>? assetIds)
    {
        lock (_sync)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0) return (false, "Kit name is required.");
            if (_data.LoanKits.Any(x => string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase))) return (false, "A kit with that name already exists.");
            _data.LoanKits.Add(new LoanKit(Guid.NewGuid(), value, (notes ?? string.Empty).Trim(), DateTime.UtcNow)
            {
                AssetIds = ValidAssetIds(assetIds)
            });
            Save();
            return (true, $"{value} added.");
        }
    }

    public (bool Ok, string Message) UpdateLoanKit(Guid id, string name, string? notes, IEnumerable<Guid>? assetIds, bool retired)
    {
        lock (_sync)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0) return (false, "Kit name is required.");
            var index = _data.LoanKits.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "Loan kit was not found.");
            if (_data.LoanKits.Any(x => x.Id != id && string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase))) return (false, "A kit with that name already exists.");
            if (retired && _data.KitLoans.Any(x => x.KitId == id && x.ReturnedAt is null)) return (false, "That kit is out on loan. Book it back in before retiring it.");
            _data.LoanKits[index] = _data.LoanKits[index] with
            {
                Name = value,
                Notes = (notes ?? string.Empty).Trim(),
                AssetIds = ValidAssetIds(assetIds),
                IsRetired = retired
            };
            Save();
            return (true, $"{value} updated.");
        }
    }

    // Kits with loan history are kept, so the report doesn't lose the past. Retire them instead.
    public (bool Ok, string Message) DeleteLoanKit(Guid id)
    {
        lock (_sync)
        {
            var kit = _data.LoanKits.FirstOrDefault(x => x.Id == id);
            if (kit is null) return (false, "Loan kit was not found.");
            if (_data.KitLoans.Any(x => x.KitId == id)) return (false, "That kit has loan history and cannot be deleted. Retire it instead, and it will stay out of the issue list.");
            _data.LoanKits.Remove(kit);
            Save();
            return (true, $"{kit.Name} deleted.");
        }
    }

    private List<Guid> ValidAssetIds(IEnumerable<Guid>? assetIds) =>
        (assetIds ?? []).Where(id => _data.Assets.Any(a => a.Id == id)).Distinct().ToList();

    // Every loan of either kind, for the Loans page and the loan report.
    // Asset assignments only qualify as loans when they have a due-back date: an assignment without one is a permanent
    // allocation (a teacher's own laptop) and has no business in a loan report. Assignments carrying a KitLoanId are
    // left out because the kit loan that created them is already in the list on its own.
    public IReadOnlyList<LoanInsights.LoanEntry> AllLoans()
    {
        lock (_sync)
        {
            var entries = _data.KitLoans
                .Where(x => _data.LoanKits.Any(k => k.Id == x.KitId))
                .Select(x => LoanInsights.From(x, _data.LoanKits.First(k => k.Id == x.KitId).Name))
                .ToList();
            foreach (var asset in _data.Assets)
                entries.AddRange(asset.Assignments
                    .Where(x => x.DueBack is not null && x.KitLoanId is null)
                    .Select(x => LoanInsights.From(x, asset.AssetTag)));
            return entries;
        }
    }

    public (bool Ok, string Message) IssueKit(Guid kitId, Guid? borrowerUserId, string? borrowerName, string? reason, DateOnly dueBack, string? issuedBy, string? notes)
    {
        lock (_sync)
        {
            var kit = _data.LoanKits.FirstOrDefault(x => x.Id == kitId);
            if (kit is null) return (false, "Loan kit was not found.");
            if (kit.IsRetired) return (false, "That kit is retired and cannot be issued.");
            if (_data.KitLoans.Any(x => x.KitId == kitId && x.ReturnedAt is null)) return (false, "That kit is already out on loan.");

            var name = (borrowerName ?? string.Empty).Trim();
            if (borrowerUserId is { } userId)
            {
                var user = _data.Users.FirstOrDefault(x => x.Id == userId);
                if (user is null) return (false, "Select a valid person.");
                name = user.Name;
            }
            else if (name.Length == 0) return (false, "Choose who is borrowing it, or type a name.");

            var chosenReason = _data.LoanReasons.FirstOrDefault(x => string.Equals(x, (reason ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosenReason is null) return (false, "Choose a reason for the loan.");

            // Issuing force-sets every member's status to "On loan", so a disposed asset left in a kit would be quietly
            // brought back from the dead. Refuse, and let someone take it out of the kit first.
            if (kit.AssetIds.Select(id => _data.Assets.FirstOrDefault(x => x.Id == id)).OfType<AssetRecord>().FirstOrDefault(IsDisposed) is { } disposed)
                return (false, $"{kit.Name} contains {disposed.AssetTag}, which has been disposed of. Take it out of the kit before issuing.");

            var loan = new KitLoan(Guid.NewGuid(), kitId, borrowerUserId, name, chosenReason,
                DateTime.UtcNow, dueBack, null, (issuedBy ?? string.Empty).Trim(), (notes ?? string.Empty).Trim());
            _data.KitLoans.Add(loan);

            // The equipment goes out with the kit, so each asset is marked out too - otherwise the asset list still
            // shows a laptop sitting in stock that is actually in somebody's bag.
            var onLoan = ResolveAssetStatus(OnLoanStatus);
            var takenOver = new List<string>();
            foreach (var assetId in kit.AssetIds)
            {
                var index = _data.Assets.FindIndex(x => x.Id == assetId);
                if (index < 0) continue;
                var asset = _data.Assets[index];
                // Worth saying out loud rather than silently moving someone's device. The change itself is recorded in
                // the asset's own history either way.
                if (asset.AssignedUserId is { } current && current != borrowerUserId) takenOver.Add($"{asset.AssetTag} (was with {UserName(current)})");
                // A borrower who isn't in the directory can't be recorded as the holder, so for them the status is the
                // only marker - which is exactly why the status is set for every loan and not just named ones.
                // Stamped with the kit loan's id so the unified loan list counts the kit loan once, not once per asset.
                ApplyAssetUpdate(index, asset with
                {
                    AssignedUserId = borrowerUserId,
                    LoanDueDate = borrowerUserId.HasValue ? dueBack : null,
                    Status = onLoan ?? asset.Status
                }, chosenReason, loan.Id);
            }

            Save();
            var message = $"{kit.Name} issued to {name}, due back {AssetInsights.Format(dueBack)}.";
            if (takenOver.Count > 0) message += $" Note: {string.Join(", ", takenOver)} {(takenOver.Count == 1 ? "was" : "were")} assigned to someone else and {(takenOver.Count == 1 ? "has" : "have")} been moved onto this loan.";
            return (true, message);
        }
    }

    public (bool Ok, string Message) ReturnKit(Guid kitId, string? notes)
    {
        lock (_sync)
        {
            var index = _data.KitLoans.FindIndex(x => x.KitId == kitId && x.ReturnedAt is null);
            if (index < 0) return (false, "That kit is not currently out on loan.");
            var loan = _data.KitLoans[index];
            var extra = (notes ?? string.Empty).Trim();
            _data.KitLoans[index] = loan with
            {
                ReturnedAt = DateTime.UtcNow,
                Notes = extra.Length == 0 ? loan.Notes : (loan.Notes.Length == 0 ? extra : $"{loan.Notes} | Returned: {extra}")
            };

            var kit = _data.LoanKits.FirstOrDefault(x => x.Id == kitId);
            var inStock = ResolveAssetStatus(InStockStatus);
            foreach (var assetId in kit?.AssetIds ?? [])
            {
                var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
                if (assetIndex < 0) continue;
                var asset = _data.Assets[assetIndex];
                // Only the status this feature set is reversed. If someone has since marked the laptop as in repair or
                // lost, that is a deliberate decision and booking the kit in should not quietly undo it.
                var status = inStock is not null && string.Equals(asset.Status, OnLoanStatus, StringComparison.OrdinalIgnoreCase) ? inStock : asset.Status;
                ApplyAssetUpdate(assetIndex, asset with { AssignedUserId = null, LoanDueDate = null, Status = status });
            }

            Save();
            return (true, $"{kit?.Name ?? "Kit"} booked back in from {loan.BorrowerName}.");
        }
    }

    public string SetLoanRepeatThreshold(int count, int days)
    {
        lock (_sync)
        {
            if (count is < 1 or > 100) return "Enter a number of loans between 1 and 100.";
            if (days is < 1 or > 3650) return "Enter a number of days between 1 and 3650.";
            _data.LoanRepeatCount = count;
            _data.LoanRepeatDays = days;
            Save();
            return "Repeat borrower threshold saved.";
        }
    }

    public IReadOnlyList<(PartRecord Part, int Quantity)> GetTicketParts(int number)
    {
        lock (_sync)
        {
            return _data.TicketParts
                .Where(x => x.TicketNumber == number)
                .Join(_data.Parts, x => x.PartId, p => p.Id, (x, p) => (p, x.Quantity))
                .OrderBy(x => x.p.Name)
                .ToList();
        }
    }
    public string? SetTicketPartQuantity(int number, Guid partId, int quantity)
    {
        lock (_sync)
        {
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
            if (ticketIndex < 0) return "Ticket was not found.";
            var partIndex = _data.Parts.FindIndex(x => x.Id == partId);
            if (partIndex < 0) return "Part was not found.";

            var part = _data.Parts[partIndex];
            var assignmentIndex = _data.TicketParts.FindIndex(x => x.TicketNumber == number && x.PartId == partId);
            var previousQuantity = assignmentIndex >= 0 ? _data.TicketParts[assignmentIndex].Quantity : 0;
            var delta = quantity - previousQuantity;

            if (delta > 0 && part.QuantityOnHand < delta) return $"Only {part.QuantityOnHand} {part.Name} left in stock.";

            _data.Parts[partIndex] = part with { QuantityOnHand = part.QuantityOnHand - delta };

            var history = _data.Tickets[ticketIndex].History.ToList();
            var from = history.Count;
            if (quantity <= 0)
            {
                if (assignmentIndex >= 0)
                {
                    _data.TicketParts.RemoveAt(assignmentIndex);
                    history.Add(new("Part removed", $"Removed {previousQuantity}x {part.Name} - returned to stock.", DateTime.UtcNow));
                }
            }
            else if (assignmentIndex >= 0)
            {
                _data.TicketParts[assignmentIndex] = _data.TicketParts[assignmentIndex] with { Quantity = quantity };
                history.Add(new("Part quantity changed", $"{part.Name}: {previousQuantity} -> {quantity}", DateTime.UtcNow));
            }
            else
            {
                _data.TicketParts.Add(new(number, partId, quantity));
                history.Add(new("Part assigned", $"Assigned {quantity}x {part.Name}.", DateTime.UtcNow));
            }
            StampActor(history, from);
            _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };

            Save();
            return null;
        }
    }
    public int AddTicket(TicketRecord item)
    {
        lock (_sync)
        {
            var number = ++_data.LastTicketNumber;
            var history = new List<TicketActivity>
            {
                new("Ticket created", "The ticket was created.", DateTime.UtcNow) { By = CurrentActor() }
            };
            _data.Tickets.Add(item with { Number = number, History = history });
            Save();
            return number;
        }
    }
    public string? DeleteTicket(int number)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return "Ticket was not found.";

            foreach (var assignment in _data.TicketParts.Where(x => x.TicketNumber == number).ToList())
            {
                var partIndex = _data.Parts.FindIndex(x => x.Id == assignment.PartId);
                if (partIndex >= 0) _data.Parts[partIndex] = _data.Parts[partIndex] with { QuantityOnHand = _data.Parts[partIndex].QuantityOnHand + assignment.Quantity };
            }
            _data.TicketParts.RemoveAll(x => x.TicketNumber == number);
            _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == number);
            RemoveTicketExtras(number);
            _data.Tickets.RemoveAt(index);
            Save();
            return null;
        }
    }
    public string? MergeTicket(int sourceNumber, int targetNumber)
    {
        lock (_sync)
        {
            var error = MergeTicketCore(sourceNumber, targetNumber);
            if (error is null) Save();
            return error;
        }
    }
    // Merges without saving, so a batch of merges can be saved once.
    private string? MergeTicketCore(int sourceNumber, int targetNumber)
    {
        if (sourceNumber == targetNumber) return "Select two different tickets to merge.";
        var sourceIndex = _data.Tickets.FindIndex(x => x.Number == sourceNumber);
        var targetIndex = _data.Tickets.FindIndex(x => x.Number == targetNumber);
        if (sourceIndex < 0 || targetIndex < 0) return "Ticket was not found.";

        var source = _data.Tickets[sourceIndex];
        var target = _data.Tickets[targetIndex];
        var now = DateTime.UtcNow;

        var mergedComments = target.Comments.ToList();
        // Carried comments keep their original author, not whoever performed the merge.
        mergedComments.AddRange(source.Comments.Select(c => new TicketComment($"(Merged from #{source.Number}) {c.Text}", c.CreatedAt, c.IsInternal) { By = c.By }));

        var mergedAssetIds = target.AssetIds.Concat(source.AssetIds).Distinct().ToList();

        var targetHistory = target.History.ToList();
        targetHistory.Add(new("Ticket merged", $"Merged ticket #{source.Number} - {source.Title} into this ticket.", now) { By = CurrentActor() });

        _data.Tickets[targetIndex] = target with
        {
            AssetIds = mergedAssetIds,
            Comments = mergedComments,
            History = targetHistory
        };

        foreach (var assignment in _data.TicketParts.Where(x => x.TicketNumber == sourceNumber).ToList())
        {
            var existingIndex = _data.TicketParts.FindIndex(x => x.TicketNumber == targetNumber && x.PartId == assignment.PartId);
            if (existingIndex >= 0)
                _data.TicketParts[existingIndex] = _data.TicketParts[existingIndex] with { Quantity = _data.TicketParts[existingIndex].Quantity + assignment.Quantity };
            else
                _data.TicketParts.Add(assignment with { TicketNumber = targetNumber });
        }
        _data.TicketParts.RemoveAll(x => x.TicketNumber == sourceNumber);

        foreach (var value in _data.TicketAttributeValues.Where(x => x.TicketNumber == sourceNumber).ToList())
        {
            if (!_data.TicketAttributeValues.Any(x => x.TicketNumber == targetNumber && x.AttributeDefinitionId == value.AttributeDefinitionId))
                _data.TicketAttributeValues.Add(value with { TicketNumber = targetNumber });
        }
        _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == sourceNumber);
        MoveTicketExtras(sourceNumber, targetNumber);

        var sourceHistory = source.History.ToList();
        sourceHistory.Add(new("Ticket merged", $"Merged into ticket #{target.Number} - {target.Title}.", now) { By = CurrentActor() });
        _data.Tickets[sourceIndex] = source with
        {
            Status = "Closed",
            ClosedAt = source.ClosedAt ?? now,
            History = sourceHistory
        };

        return null;
    }

    // A ticket with its priority or category changed. Unless the SLA or due date was set by hand, they follow the change.
    public TicketRecord WithPriority(TicketRecord ticket, string priority)
    {
        lock (_sync)
        {
            if (ticket.SlaOverridden) return ticket with { Priority = priority };
            var sla = SlaFor(priority, ticket.Category);
            return ticket with { Priority = priority, SlaId = sla, DueDate = ticket.DueDateOverridden ? ticket.DueDate : CalculateDueDate(sla, ticket.CreatedAt) };
        }
    }
    public TicketRecord WithCategory(TicketRecord ticket, string category)
    {
        lock (_sync)
        {
            if (ticket.SlaOverridden) return ticket with { Category = category };
            var sla = SlaFor(ticket.Priority, category);
            return ticket with { Category = category, SlaId = sla, DueDate = ticket.DueDateOverridden ? ticket.DueDate : CalculateDueDate(sla, ticket.CreatedAt) };
        }
    }

    // One change applied to many tickets. Operation is status, technician, team, priority, category, type, comment, close or merge.
    // Value is the new status, team, priority or category (TicketListQuery.None clears the team); Text is the comment or closing message;
    // TargetNumber is the ticket to merge into.
    public sealed record TicketBulkChange(string Operation, string? Value = null, Guid? TechnicianId = null, string? Text = null, int? TargetNumber = null, bool Internal = false);
    // Skipped tickets could not be changed for a reason worth telling the user; unchanged ones already had the value.
    public sealed record TicketBulkResult(int Updated, int Unchanged, int Skipped, string? SkippedReason, string? Error);

    // Saves once for the whole batch, however many tickets change.
    public TicketBulkResult BulkUpdateTickets(IReadOnlyCollection<int> numbers, TicketBulkChange change)
    {
        lock (_sync)
        {
            static TicketBulkResult Fail(string message) => new(0, 0, 0, null, message);
            var chosen = numbers.ToHashSet();
            string? canonical = null;
            string? text = string.IsNullOrWhiteSpace(change.Text) ? null : change.Text.Trim();
            switch (change.Operation)
            {
                case "status":
                    canonical = _data.Statuses.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid status.");
                    break;
                case "priority":
                    canonical = _data.Priorities.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid priority.");
                    break;
                case "category":
                    canonical = _data.Categories.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid category.");
                    break;
                case "type":
                    canonical = TicketTypes.All.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid type.");
                    break;
                case "team":
                    if (change.Value == TicketListQuery.None) break;
                    canonical = _data.TechnicianTeams.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid team.");
                    break;
                case "technician":
                    if (change.TechnicianId is { } id && !_data.Technicians.Any(x => x.Id == id)) return Fail("Select a valid technician.");
                    break;
                case "comment":
                    if (text is null) return Fail("Enter a comment to add.");
                    break;
                case "close":
                    break;
                case "merge":
                    if (change.TargetNumber is not { } target || !_data.Tickets.Any(x => x.Number == target)) return Fail("Enter the number of an existing ticket to merge into.");
                    break;
                default:
                    return Fail("Choose what to change.");
            }

            int updated = 0, unchanged = 0, skipped = 0;
            string? skippedReason = null;
            void Skip(string reason) { skipped++; skippedReason ??= reason; }

            foreach (var number in _data.Tickets.Where(x => chosen.Contains(x.Number)).Select(x => x.Number).ToList())
            {
                var index = _data.Tickets.FindIndex(x => x.Number == number);
                var ticket = _data.Tickets[index];
                TicketRecord changed;
                switch (change.Operation)
                {
                    case "status":
                        if (string.Equals(ticket.Status, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = ticket with { Status = canonical!, ClosedAt = string.Equals(canonical, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase) ? ticket.ClosedAt ?? DateTime.UtcNow : null };
                        break;
                    case "priority":
                        if (string.Equals(ticket.Priority, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = WithPriority(ticket, canonical!);
                        break;
                    case "category":
                        if (string.Equals(ticket.Category, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = WithCategory(ticket, canonical!);
                        break;
                    case "type":
                        if (string.Equals(ticket.Type, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = ticket with { Type = canonical! };
                        break;
                    case "team":
                    {
                        var team = change.Value == TicketListQuery.None ? null : canonical;
                        if (string.Equals(ticket.TeamName, team, StringComparison.Ordinal)) { unchanged++; continue; }
                        var keep = ticket.TechnicianId is not { } current || _data.Technicians.FirstOrDefault(x => x.Id == current) is not { } holder || TechnicianInTeam(holder, team);
                        changed = ticket with { TeamName = team, TechnicianId = keep ? ticket.TechnicianId : null };
                        break;
                    }
                    case "technician":
                    {
                        if (ticket.TechnicianId == change.TechnicianId) { unchanged++; continue; }
                        if (change.TechnicianId is { } technicianId && _data.Technicians.First(x => x.Id == technicianId) is { } technician && !TechnicianInTeam(technician, ticket.TeamName))
                        { Skip($"{technician.Name} is not in the team a ticket is assigned to."); continue; }
                        changed = ticket with { TechnicianId = change.TechnicianId };
                        break;
                    }
                    case "comment":
                    {
                        var comments = ticket.Comments.ToList();
                        comments.Add(new TicketComment(text!, DateTime.UtcNow, change.Internal) { By = CurrentActor() });
                        _data.Tickets[index] = ticket with { Comments = comments };
                        updated++;
                        continue;
                    }
                    case "close":
                    {
                        if (TicketInsights.IsClosed(ticket)) { unchanged++; continue; }
                        if (text is null && RequiresCloseMessage(ticket)) { Skip("A closing message is required for some of these tickets."); continue; }
                        var comments = ticket.Comments.ToList();
                        if (text is not null) comments.Add(new TicketComment(text, DateTime.UtcNow) { By = CurrentActor() });
                        changed = ticket with { Status = TicketInsights.ClosedStatus, ClosedAt = ticket.ClosedAt ?? DateTime.UtcNow, Comments = comments };
                        break;
                    }
                    default: // merge
                    {
                        if (number == change.TargetNumber) { unchanged++; continue; }
                        if (MergeTicketCore(number, change.TargetNumber!.Value) is { } mergeError) { Skip(mergeError); continue; }
                        updated++;
                        continue;
                    }
                }
                var history = ticket.History.ToList();
                var from = history.Count;
                AddTicketActivities(history, ticket, changed);
                StampActor(history, from);
                _data.Tickets[index] = changed with { History = history };
                updated++;
            }

            if (updated > 0) Save();
            return new TicketBulkResult(updated, unchanged, skipped, skippedReason, null);
        }
    }
    // A reply from the person who raised the ticket. If it had been closed it is reopened, because otherwise "it is
    // still not working" lands on a closed ticket that nobody is looking at.
    // Deliberately separate from AddTicketComment: a technician adding a note to a ticket they have just closed should
    // not bounce it straight back open, so only this path reopens.
    public (bool Ok, bool Reopened) AddRequesterComment(int number, string text)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return (false, false);

            var actor = CurrentActor();
            var ticket = _data.Tickets[index];
            var comments = ticket.Comments.ToList();
            comments.Add(new TicketComment(text.Trim(), DateTime.UtcNow) { By = actor });
            ticket = ticket with { Comments = comments };

            var reopened = false;
            // Nothing to reopen to if every status has been renamed away from "Closed"; the comment is still kept.
            if (TicketInsights.IsClosed(ticket) && _data.Statuses.FirstOrDefault(x => !string.Equals(x, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase)) is { } openStatus)
            {
                var history = ticket.History.ToList();
                history.Add(new TicketActivity("Ticket reopened", $"{actor.Name} replied after the ticket was closed, so it was reopened.", DateTime.UtcNow) { By = actor });
                ticket = ticket with { Status = openStatus, ClosedAt = null, History = history };
                // The old due date belongs to the first time round - left alone it would show as overdue the moment the
                // ticket reopens, which is both wrong and noisy. A manually typed due date is the user's, so it stays.
                if (!ticket.DueDateOverridden && ticket.SlaId is not null)
                    ticket = ticket with { DueDate = CalculateDueDate(ticket.SlaId, DateTime.UtcNow) };
                reopened = true;
            }

            _data.Tickets[index] = ticket;
            Save();
            return (true, reopened);
        }
    }

    public bool AddTicketComment(int number, string text, bool isInternal = false)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return false;
            var comments = _data.Tickets[index].Comments.ToList();
            comments.Add(new TicketComment(text.Trim(), DateTime.UtcNow, isInternal) { By = CurrentActor() });
            _data.Tickets[index] = _data.Tickets[index] with { Comments = comments };
            Save();
            return true;
        }
    }
        public string AddSla(string name, int duration, string durationUnit, string? description, IEnumerable<string>? priorities, IEnumerable<string>? categories)
        {
            lock (_sync)
            {
                name = name.Trim();
                durationUnit = NormalizeDurationUnit(durationUnit);
                if (string.IsNullOrWhiteSpace(name)) return "SLA name is required.";
                if (duration < 1) return "SLA duration must be at least 1.";
                if (_data.Slas.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return "That SLA already exists.";
                var validPriorities = (priorities ?? []).Where(x => _data.Priorities.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var validCategories = (categories ?? []).Where(x => _data.Categories.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _data.Slas.Add(new(Guid.NewGuid(), name, duration, durationUnit, string.IsNullOrWhiteSpace(description) ? null : description.Trim()) { Priorities = validPriorities, Categories = validCategories });
                Save();
                return "SLA added.";
            }
        }
        public string UpdateSla(Guid id, string name, int duration, string durationUnit, string? description, IEnumerable<string>? priorities, IEnumerable<string>? categories)
        {
            lock (_sync)
            {
                var index = _data.Slas.FindIndex(x => x.Id == id);
                if (index < 0) return "SLA was not found.";
                name = name.Trim();
                durationUnit = NormalizeDurationUnit(durationUnit);
                if (string.IsNullOrWhiteSpace(name)) return "SLA name is required.";
                if (duration < 1) return "SLA duration must be at least 1.";
                if (_data.Slas.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return "That SLA already exists.";
                var validPriorities = (priorities ?? []).Where(x => _data.Priorities.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var validCategories = (categories ?? []).Where(x => _data.Categories.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _data.Slas[index] = new(id, name, duration, durationUnit, string.IsNullOrWhiteSpace(description) ? null : description.Trim()) { Priorities = validPriorities, Categories = validCategories };
                Save();
                return "SLA updated.";
            }
        }
        public string DeleteSla(Guid id)
        {
            lock (_sync)
            {
                if (_data.Tickets.Any(x => x.SlaId == id)) return "This SLA is used by tickets and cannot be deleted.";
                var index = _data.Slas.FindIndex(x => x.Id == id);
                if (index < 0) return "SLA was not found.";
                _data.Slas.RemoveAt(index);
                // A template that named this SLA goes back to working the SLA out from the priority and category.
                for (var i = 0; i < _data.TicketTemplates.Count; i++)
                    if (_data.TicketTemplates[i].SlaId == id) _data.TicketTemplates[i] = _data.TicketTemplates[i] with { SlaId = null };
                Save();
                return "SLA deleted.";
            }
        }
        public DateTime? CalculateDueDate(Guid? slaId, DateTime createdAt) =>
            slaId is Guid id && _data.Slas.FirstOrDefault(x => x.Id == id) is { } sla
                ? createdAt.Add(sla.DurationUnit switch { "days" => TimeSpan.FromDays(sla.Duration), "minutes" => TimeSpan.FromMinutes(sla.Duration), _ => TimeSpan.FromHours(sla.Duration) })
                : null;
    public Guid? SlaFor(string priority, string category) =>
        _data.Slas.FirstOrDefault(x => x.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))?.Id
        ?? _data.Slas.FirstOrDefault(x => x.Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase))?.Id;
    public bool UpdateUser(UserRecord item) => Update(item, _data.Users, x => x.Id == item.Id);
    public bool UpdateUserAndTickets(UserRecord user, IEnumerable<int> selectedTicketNumbers)
    {
        lock (_sync)
        {
            var userIndex = _data.Users.FindIndex(x => x.Id == user.Id);
            if (userIndex < 0) return false;
            _data.Users[userIndex] = user;
            var selected = selectedTicketNumbers.ToHashSet();
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                if (selected.Contains(ticket.Number))
                    _data.Tickets[i] = ticket with { RequesterId = user.Id };
                else if (ticket.RequesterId == user.Id)
                    continue;
            }
            Save();
            return true;
        }
    }
    public bool UpdateTechnician(TechnicianRecord item) => Update(item, _data.Technicians, x => x.Id == item.Id);
    public bool UpdateAsset(AssetRecord item)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == item.Id);
            if (index < 0) return false;
            ApplyAssetUpdate(index, item);
            Save();
            return true;
        }
    }
    // Replaces the asset at index, recording history and ownership changes. The caller holds the lock and saves.
    // loanReason and kitLoanId stamp the assignment period this creates, so the unified loan list can tell why the
    // asset went out and whether a kit loan already accounts for it. Both are null for an ordinary asset edit.
    private void ApplyAssetUpdate(int index, AssetRecord item, string? loanReason = null, Guid? kitLoanId = null)
    {
        var previous = _data.Assets[index];
        if (string.IsNullOrWhiteSpace(item.Status)) item = item with { Status = previous.Status };
        if (!item.AssignedUserId.HasValue) item = item with { LoanDueDate = null };
        var history = previous.History.ToList();
        var from = history.Count;
        AddAssetActivities(history, previous, item, _data.Users);
        StampActor(history, from);
        var assignments = previous.Assignments.ToList();
        var now = DateTime.UtcNow;
        if (previous.AssignedUserId != item.AssignedUserId)
        {
            for (var i = 0; i < assignments.Count; i++)
                if (assignments[i].EndedAt is null) assignments[i] = assignments[i] with { EndedAt = now };
            if (item.AssignedUserId is { } holder)
                assignments.Add(new AssetAssignment(holder, UserName(holder), now, null, item.LoanDueDate) { Reason = loanReason, KitLoanId = kitLoanId });
        }
        else if (previous.LoanDueDate != item.LoanDueDate)
        {
            var open = assignments.FindLastIndex(x => x.EndedAt is null);
            if (open >= 0) assignments[open] = assignments[open] with { DueBack = item.LoanDueDate };
        }
        _data.Assets[index] = item with { History = history, Comments = previous.Comments, Assignments = assignments };
    }

    // One or more of these can be set: the status, the owner (OwnerId null clears it) and the location (blank clears it).
    public sealed record AssetBulkChange(string? Status, bool ChangeOwner, Guid? OwnerId, bool ChangeLocation, string? Location);

    // Applies the same change to many assets and saves once, however many there are. Assets the change would not alter are counted, not touched.
    public (int Updated, int Unchanged, string? Error) BulkUpdateAssets(IEnumerable<Guid> assetIds, AssetBulkChange change)
    {
        lock (_sync)
        {
            if (change.Status is null && !change.ChangeOwner && !change.ChangeLocation) return (0, 0, "Choose what to change.");
            string? status = null;
            if (change.Status is not null)
            {
                status = _data.AssetStatuses.FirstOrDefault(x => string.Equals(x, change.Status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (status is null) return (0, 0, "Select a valid status.");
            }
            if (change.ChangeOwner && change.OwnerId.HasValue && !_data.Users.Any(x => x.Id == change.OwnerId.Value)) return (0, 0, "Select a valid user.");
            var location = string.Empty;
            if (change.ChangeLocation && !string.IsNullOrWhiteSpace(change.Location))
            {
                var match = _data.Locations.FirstOrDefault(x => string.Equals(x, change.Location.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return (0, 0, "Select a valid location.");
                location = match;
            }

            var updated = 0;
            var unchanged = 0;
            foreach (var id in assetIds.Distinct())
            {
                var index = _data.Assets.FindIndex(x => x.Id == id);
                if (index < 0) continue;
                var asset = _data.Assets[index];
                var next = asset;
                if (status is not null) next = next with { Status = status };
                if (change.ChangeOwner)
                {
                    // Handing a device to someone else is a normal assignment, not a continuation of the old loan.
                    next = next with { AssignedUserId = change.OwnerId, LoanDueDate = asset.AssignedUserId == change.OwnerId ? asset.LoanDueDate : null };
                }
                if (change.ChangeLocation) next = next with { Location = location };
                if (next == asset) { unchanged++; continue; }
                ApplyAssetUpdate(index, next);
                updated++;
            }
            if (updated > 0) Save();
            return (updated, unchanged, null);
        }
    }
    public IReadOnlyList<AssetAttributeValue> AssetAttributeValues { get { lock (_sync) return _data.AssetAttributeValues.ToList(); } }
    public bool AddAssetComment(Guid assetId, string text)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return false;
            var comments = _data.Assets[index].Comments.ToList();
            comments.Add(new AssetComment(text.Trim(), DateTime.UtcNow) { By = CurrentActor() });
            _data.Assets[index] = _data.Assets[index] with { Comments = comments };
            Save();
            return true;
        }
    }
    public bool LinkAssetToTicket(Guid assetId, int ticketNumber)
    {
        lock (_sync)
        {
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null) return false;
            // Nothing new gets linked to kit that has left the estate. Tickets already linked keep their link, so the
            // repair history of a disposed asset stays readable.
            if (IsDisposed(asset)) return false;
            var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (index < 0) return false;
            var ticket = _data.Tickets[index];
            if (ticket.AssetIds.Contains(assetId)) return true;
            var history = ticket.History.ToList();
            history.Add(new("Asset changed", $"Asset {asset.AssetTag} was linked.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { AssetIds = ticket.AssetIds.Append(assetId).ToList(), History = history };
            var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
            if (assetIndex >= 0)
            {
                var assetHistory = _data.Assets[assetIndex].History.ToList();
                assetHistory.Add(new("Ticket linked", $"Linked to ticket #{ticket.Number} - {ticket.Title}", DateTime.UtcNow) { By = CurrentActor() });
                _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
            }
            Save();
            return true;
        }
    }
    public bool UnlinkAssetFromTicket(Guid assetId, int ticketNumber)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (index < 0) return false;
            var ticket = _data.Tickets[index];
            if (!ticket.AssetIds.Contains(assetId)) return true;
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            var history = ticket.History.ToList();
            history.Add(new("Asset changed", $"Asset {asset?.AssetTag ?? "Unknown"} was unlinked.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { AssetIds = ticket.AssetIds.Where(x => x != assetId).ToList(), History = history };
            var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
            if (assetIndex >= 0)
            {
                var assetHistory = _data.Assets[assetIndex].History.ToList();
                assetHistory.Add(new("Ticket unlinked", $"Unlinked from ticket #{ticket.Number} - {ticket.Title}", DateTime.UtcNow) { By = CurrentActor() });
                _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
            }
            Save();
            return true;
        }
    }
    public IReadOnlyList<AssetAttributeValue> GetAssetAttributeValues(Guid assetId)
    {
        lock (_sync) return _data.AssetAttributeValues.Where(x => x.AssetId == assetId).ToList();
    }
    public string AddAssetAttributeDefinition(string name, IEnumerable<string>? assetTypes, string fieldType, string? choices)
    {
        lock (_sync)
        {
            name = (name ?? string.Empty).Trim();
            fieldType = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
            choices = NormalizeChoices(choices);
            if (string.IsNullOrWhiteSpace(name)) return "Attribute name is required.";
            var types = ResolveScope(assetTypes, _data.AssetTypes);
            if (types is null) return "Select valid asset types.";
            if (!IsAttributeType(fieldType)) return "Select a valid field type.";
            if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
            if (_data.AssetAttributeDefinitions.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.AssetTypes, types))) return DuplicateAttributeMessage(types, "asset types");
            _data.AssetAttributeDefinitions.Add(new(Guid.NewGuid(), name, fieldType, choices) { AssetTypes = types });
            Save();
            return "Custom attribute added.";
        }
    }
    public string AddAssetAttributeDefinition(string name, IEnumerable<string>? assetTypes) =>
        AddAssetAttributeDefinition(name, assetTypes, "single-line", null);
    public string UpdateAssetAttributeDefinition(Guid id, string name, IEnumerable<string>? assetTypes, string fieldType, string? choices)
    {
        lock (_sync)
        {
            name = (name ?? string.Empty).Trim();
            fieldType = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
            choices = NormalizeChoices(choices);
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            if (string.IsNullOrWhiteSpace(name)) return "Attribute name is required.";
            var types = ResolveScope(assetTypes, _data.AssetTypes);
            if (types is null) return "Select valid asset types.";
            if (!IsAttributeType(fieldType)) return "Select a valid field type.";
            if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
            if (_data.AssetAttributeDefinitions.Any(x => x.Id != id && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.AssetTypes, types))) return DuplicateAttributeMessage(types, "asset types");
            _data.AssetAttributeDefinitions[index] = new(id, name, fieldType, choices) { AssetTypes = types };
            Save();
            return "Custom attribute updated.";
        }
    }
    public string UpdateAssetAttributeDefinition(Guid id, string name, IEnumerable<string>? assetTypes) =>
        UpdateAssetAttributeDefinition(id, name, assetTypes, "single-line", null);
    public string SetAssetAttributeAssetTypes(Guid id, IEnumerable<string>? assetTypes)
    {
        lock (_sync)
        {
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            var types = ResolveScope(assetTypes, _data.AssetTypes);
            if (types is null) return "Select valid asset types.";
            var definition = _data.AssetAttributeDefinitions[index];
            if (_data.AssetAttributeDefinitions.Any(x => x.Id != id && x.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.AssetTypes, types))) return DuplicateAttributeMessage(types, "asset types");
            _data.AssetAttributeDefinitions[index] = definition with { AssetTypes = types };
            Save();
            return "Custom attribute updated.";
        }
    }
    public string DeleteAssetAttributeDefinition(Guid id)
    {
        lock (_sync)
        {
            if (id == Guid.Empty) return "A valid custom attribute is required.";
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            _data.AssetAttributeDefinitions.RemoveAt(index);
            _data.AssetAttributeValues.RemoveAll(x => x.AttributeDefinitionId == id);
            Save();
            return "Custom attribute deleted.";
        }
    }
    public bool UpdateAssetAttributeValues(Guid assetId, string assetType, IDictionary<Guid, string>? values)
    {
        lock (_sync)
        {
            if (!_data.Assets.Any(x => x.Id == assetId)) return false;
            _data.AssetAttributeValues.RemoveAll(x => x.AssetId == assetId);
            foreach (var definition in _data.AssetAttributeDefinitions.Where(x => x.AppliesTo(assetType)))
            {
                string? raw = null;
                values?.TryGetValue(definition.Id, out raw);
                var value = raw?.Trim() ?? string.Empty;
                if (definition.FieldType == "checkbox")
                    value = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                else if (definition.FieldType == "dropdown" && !string.IsNullOrEmpty(value) && !GetChoices(definition).Contains(value, StringComparer.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(value))
                    _data.AssetAttributeValues.Add(new(assetId, definition.Id, value));
            }
            Save();
            return true;
        }
    }

            public string AddTicketAttributeDefinition(string name, IEnumerable<string>? categories, string fieldType, string? choices)
            {
                lock (_sync)
                {
                    name = (name ?? "").Trim();
                    fieldType = NormalizeAttributeType(fieldType); choices = NormalizeChoices(choices);
                    if (string.IsNullOrWhiteSpace(name) || !IsAttributeType(fieldType)) return "Enter a name and valid field type.";
                    var scope = ResolveScope(categories, _data.Categories);
                    if (scope is null) return "Select valid categories.";
                    if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
                    if (_data.TicketAttributeDefinitions.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.Categories, scope))) return DuplicateAttributeMessage(scope, "categories");
                    _data.TicketAttributeDefinitions.Add(new(Guid.NewGuid(), name, fieldType, choices) { Categories = scope }); Save(); return "Ticket attribute added.";
                }
            }
            public string UpdateTicketAttributeDefinition(Guid id, string name, IEnumerable<string>? categories, string fieldType, string? choices)
            {
                lock (_sync)
                {
                    var index = _data.TicketAttributeDefinitions.FindIndex(x => x.Id == id);
                    if (index < 0) return "Ticket attribute was not found.";
                    name = (name ?? "").Trim(); fieldType = NormalizeAttributeType(fieldType); choices = NormalizeChoices(choices);
                    if (string.IsNullOrWhiteSpace(name) || !IsAttributeType(fieldType)) return "Enter a name and valid field type.";
                    var scope = ResolveScope(categories, _data.Categories);
                    if (scope is null) return "Select valid categories.";
                    if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
                    if (_data.TicketAttributeDefinitions.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.Categories, scope))) return DuplicateAttributeMessage(scope, "categories");
                    _data.TicketAttributeDefinitions[index] = new(id, name, fieldType, choices) { Categories = scope }; Save(); return "Ticket attribute updated.";
                }
            }
            public string SetTicketAttributeCategories(Guid id, IEnumerable<string>? categories)
            {
                lock (_sync)
                {
                    var index = _data.TicketAttributeDefinitions.FindIndex(x => x.Id == id);
                    if (index < 0) return "Ticket attribute was not found.";
                    var scope = ResolveScope(categories, _data.Categories);
                    if (scope is null) return "Select valid categories.";
                    var definition = _data.TicketAttributeDefinitions[index];
                    if (_data.TicketAttributeDefinitions.Any(x => x.Id != id && string.Equals(x.Name, definition.Name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.Categories, scope))) return DuplicateAttributeMessage(scope, "categories");
                    _data.TicketAttributeDefinitions[index] = definition with { Categories = scope }; Save(); return "Ticket attribute updated.";
                }
            }
            public string DeleteTicketAttributeDefinition(Guid id)
            {
                lock (_sync) { if (!_data.TicketAttributeDefinitions.RemoveAll(x => x.Id == id).Equals(1)) return "Ticket attribute was not found."; _data.TicketAttributeValues.RemoveAll(x => x.AttributeDefinitionId == id); Save(); return "Ticket attribute deleted."; }
            }
            public bool UpdateTicketAttributeValues(int number, string category, IDictionary<Guid, string>? values)
            {
                lock (_sync)
                {
                    var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
                    if (ticketIndex < 0) return false;
                    var previousValues = _data.TicketAttributeValues.Where(x => x.TicketNumber == number).ToDictionary(x => x.AttributeDefinitionId, x => x.Value);
                    _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == number);
                    var changes = new List<string>();
                    foreach (var definition in GetTicketAttributes(category))
                    {
                        string? raw = null; values?.TryGetValue(definition.Id, out raw); var value = raw?.Trim() ?? "";
                        if (definition.FieldType == "checkbox") value = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                        if (definition.FieldType == "dropdown" && value.Length > 0 && !GetChoices(definition).Contains(value, StringComparer.Ordinal)) continue;
                        if (value.Length > 0) _data.TicketAttributeValues.Add(new(number, definition.Id, value));

                        var previousValue = previousValues.TryGetValue(definition.Id, out var existing) ? existing : "";
                        if (!string.Equals(previousValue, value, StringComparison.Ordinal))
                            changes.Add($"{definition.Name}: {(previousValue.Length > 0 ? previousValue : "(empty)")} -> {(value.Length > 0 ? value : "(empty)")}");
                    }
                    if (changes.Count > 0)
                    {
                        var history = _data.Tickets[ticketIndex].History.ToList();
                        history.Add(new TicketActivity("Custom attributes changed", string.Join("; ", changes), DateTime.UtcNow) { By = CurrentActor() });
                        _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };
                    }
                    Save(); return true;
                }
            }
    public bool UpdateTicket(TicketRecord item)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == item.Number);
            if (index < 0) return false;

            var previous = _data.Tickets[index];
            var history = previous.History.ToList();
            var from = history.Count;
            AddTicketActivities(history, previous, item);
            StampActor(history, from);
            _data.Tickets[index] = item with { History = history };

            var previousAssetIds = previous.AssetIds.ToHashSet();
            var updatedAssetIds = item.AssetIds.ToHashSet();
            if (!previousAssetIds.SetEquals(updatedAssetIds))
            {
                var now = DateTime.UtcNow;
                foreach (var addedAssetId in updatedAssetIds.Except(previousAssetIds))
                {
                    var assetIndex = _data.Assets.FindIndex(x => x.Id == addedAssetId);
                    if (assetIndex < 0) continue;
                    var assetHistory = _data.Assets[assetIndex].History.ToList();
                    assetHistory.Add(new("Ticket linked", $"Linked to ticket #{item.Number} - {item.Title}", now) { By = CurrentActor() });
                    _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
                }
                foreach (var removedAssetId in previousAssetIds.Except(updatedAssetIds))
                {
                    var assetIndex = _data.Assets.FindIndex(x => x.Id == removedAssetId);
                    if (assetIndex < 0) continue;
                    var assetHistory = _data.Assets[assetIndex].History.ToList();
                    assetHistory.Add(new("Ticket unlinked", $"Unlinked from ticket #{item.Number} - {item.Title}", now) { By = CurrentActor() });
                    _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
                }
            }

            Save();
            return true;
        }
    }
    public void UpdateBranding(BrandingSettings item) { lock (_sync) { _data.Branding = item; Save(); } }
    // The lists a brand new install starts with. Anything already in use is added on top of these when data is loaded.
    private void EnsureFactoryOptions()
    {
        // "On loan" and "Disposed" are set by the system itself (see IssueKit and DisposeAsset), so unlike the others
        // they have to exist in every database rather than only in newly seeded ones.
        // "Disposed" goes LAST on purpose: the add-asset form and the CSV importer both fall back to
        // AssetStatuses.FirstOrDefault() for a default, and defaulting new kit to disposed would be absurd.
        EnsureOptions(_data.AssetStatuses, ["In use", "On loan", "In stock or spare", "In repair", "Lost or stolen", "Disposed"]);
        EnsureOptions(_data.Categories, ["Hardware", "Software", "Account", "Network", "Classroom AV", "Other"]);
        EnsureOptions(_data.Statuses, ["Open", "In Progress", "On Hold", "Closed"]);
        EnsureOptions(_data.Priorities, ["Normal", "Low", "High", "Urgent"]);
        EnsureOptions(_data.LoanReasons, ["Forgot own device", "Supply or visitor", "Own device in repair", "Other"]);
    }

    // The demo records that are still there, as (kind, name) for the "Go live" panel to list. Ids that no longer
    // resolve are skipped: the admin deleted that one by hand, which is fine.
    public IReadOnlyList<(string Kind, string Name)> DemoDataSummary()
    {
        lock (_sync) return _data.DemoRecords.Select(DescribeDemoRecord).OfType<(string, string)>().ToList();
    }
    public bool HasDemoData { get { lock (_sync) return _data.DemoRecords.Any(x => DescribeDemoRecord(x) is not null); } }

    private (string Kind, string Name)? DescribeDemoRecord(DemoRecord record)
    {
        switch (record.EntityType)
        {
            case "Ticket" when int.TryParse(record.EntityKey, out var number):
                return _data.Tickets.FirstOrDefault(x => x.Number == number) is { } ticket ? ("Ticket", $"#{ticket.Number} {ticket.Title}") : null;
            case "Asset" when Guid.TryParse(record.EntityKey, out var assetId):
                return _data.Assets.FirstOrDefault(x => x.Id == assetId) is { } asset ? ("Asset", asset.AssetTag) : null;
            case "User" when Guid.TryParse(record.EntityKey, out var userId):
                return _data.Users.FirstOrDefault(x => x.Id == userId) is { } user ? ("Requester", user.Name) : null;
            case "Technician" when Guid.TryParse(record.EntityKey, out var technicianId):
                return _data.Technicians.FirstOrDefault(x => x.Id == technicianId) is { } tech ? ("Technician", tech.Name) : null;
            case "Supplier" when Guid.TryParse(record.EntityKey, out var supplierId):
                return _data.Suppliers.FirstOrDefault(x => x.Id == supplierId) is { } supplier ? ("Supplier", supplier.Name) : null;
            case "Part" when Guid.TryParse(record.EntityKey, out var partId):
                return _data.Parts.FirstOrDefault(x => x.Id == partId) is { } part ? ("Part", part.Name) : null;
            case "Sla" when Guid.TryParse(record.EntityKey, out var slaId):
                return _data.Slas.FirstOrDefault(x => x.Id == slaId) is { } sla ? ("SLA", sla.Name) : null;
            case "TicketTemplate" when Guid.TryParse(record.EntityKey, out var templateId):
                return _data.TicketTemplates.FirstOrDefault(x => x.Id == templateId) is { } template ? ("Ticket template", template.Name) : null;
            case "LoanKit" when Guid.TryParse(record.EntityKey, out var kitId):
                return _data.LoanKits.FirstOrDefault(x => x.Id == kitId) is { } kit ? ("Loan kit", kit.Name) : null;
            case "AssetAttribute" when Guid.TryParse(record.EntityKey, out var assetAttributeId):
                return _data.AssetAttributeDefinitions.FirstOrDefault(x => x.Id == assetAttributeId) is { } definition ? ("Asset attribute", definition.Name) : null;
            case "TicketAttribute" when Guid.TryParse(record.EntityKey, out var ticketAttributeId):
                return _data.TicketAttributeDefinitions.FirstOrDefault(x => x.Id == ticketAttributeId) is { } ticketDefinition ? ("Ticket attribute", ticketDefinition.Name) : null;
            default:
                return null;
        }
    }

    // "Go live": removes the records SeedDemoData created and nothing else. Option-list values it added (Teaching,
    // Dell, Consumables and so on) are deliberately kept - they are ordinary values a school may already be using,
    // and each can be deleted from its own Settings page if it isn't wanted.
    // Real records that point at demo ones are unpicked rather than deleted: a ticket loses its demo technician, SLA
    // and asset links instead of disappearing. The one thing that cannot be unpicked is a ticket's requester, so that
    // case refuses the whole operation rather than deleting someone's ticket or leaving a row that breaks the save.
    public (bool Ok, string Message) RemoveDemoData()
    {
        lock (_sync)
        {
            if (!_data.DemoRecords.Any(x => DescribeDemoRecord(x) is not null))
            {
                if (_data.DemoRecords.Count > 0) { _data.DemoRecords.Clear(); Save(); }
                return (false, "There is no demo data left to remove.");
            }

            HashSet<Guid> Ids(string type) => _data.DemoRecords.Where(x => x.EntityType == type)
                .Select(x => Guid.TryParse(x.EntityKey, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToHashSet();
            var ticketNumbers = _data.DemoRecords.Where(x => x.EntityType == "Ticket")
                .Select(x => int.TryParse(x.EntityKey, out var n) ? n : 0).Where(x => x != 0).ToHashSet();
            var assetIds = Ids("Asset");
            var userIds = Ids("User");
            var technicianIds = Ids("Technician");
            var supplierIds = Ids("Supplier");
            var partIds = Ids("Part");
            var slaIds = Ids("Sla");
            var templateIds = Ids("TicketTemplate");
            var kitIds = Ids("LoanKit");
            var assetAttributeIds = Ids("AssetAttribute");
            var ticketAttributeIds = Ids("TicketAttribute");

            var blocking = _data.Tickets.Where(x => userIds.Contains(x.RequesterId) && !ticketNumbers.Contains(x.Number)).ToList();
            if (blocking.Count > 0)
            {
                var names = string.Join(", ", blocking.Take(5).Select(x => $"#{x.Number}"));
                var one = blocking.Count == 1;
                return (false, $"Nothing was changed. {(one ? "Ticket" : "Tickets")} {names}{(blocking.Count > 5 ? " and others" : "")} "
                    + $"still {(one ? "lists" : "list")} a demo requester. Change the requester on {(one ? "that ticket" : "those tickets")}, or delete "
                    + $"{(one ? "it" : "them")}, and try again.");
            }

            var removed = _data.DemoRecords.Select(DescribeDemoRecord).OfType<(string Kind, string Name)>().Count();

            // Demo tickets and everything hanging off them.
            foreach (var number in ticketNumbers) RemoveTicketExtras(number);
            _data.TicketParts.RemoveAll(x => ticketNumbers.Contains(x.TicketNumber) || partIds.Contains(x.PartId));
            _data.TicketAttributeValues.RemoveAll(x => ticketNumbers.Contains(x.TicketNumber) || ticketAttributeIds.Contains(x.AttributeDefinitionId));
            _data.Tickets.RemoveAll(x => ticketNumbers.Contains(x.Number));
            // Real tickets keep everything they can: only the pointers at demo records go.
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                if (ticket.TechnicianId is { } assigned && technicianIds.Contains(assigned)) ticket = ticket with { TechnicianId = null };
                if (ticket.SlaId is { } sla && slaIds.Contains(sla)) ticket = ticket with { SlaId = null };
                if (ticket.AssetIds.Any(assetIds.Contains)) ticket = ticket with { AssetIds = ticket.AssetIds.Where(x => !assetIds.Contains(x)).ToList() };
                _data.Tickets[i] = ticket;
            }

            _data.AssetAttributeValues.RemoveAll(x => assetIds.Contains(x.AssetId) || assetAttributeIds.Contains(x.AttributeDefinitionId));
            _data.Assets.RemoveAll(x => assetIds.Contains(x.Id));
            for (var i = 0; i < _data.Assets.Count; i++)
            {
                var asset = _data.Assets[i];
                if (asset.AssignedUserId is { } holder && userIds.Contains(holder)) asset = asset with { AssignedUserId = null, LoanDueDate = null };
                if (asset.SupplierId is { } supplier && supplierIds.Contains(supplier)) asset = asset with { SupplierId = null };
                _data.Assets[i] = asset;
            }

            _data.KitLoans.RemoveAll(x => kitIds.Contains(x.KitId));
            _data.LoanKits.RemoveAll(x => kitIds.Contains(x.Id));
            for (var i = 0; i < _data.LoanKits.Count; i++)
                _data.LoanKits[i] = _data.LoanKits[i] with { AssetIds = _data.LoanKits[i].AssetIds.Where(x => !assetIds.Contains(x)).ToList() };
            // The borrower's name stays on the loan - that is exactly why it is stored alongside the id.
            for (var i = 0; i < _data.KitLoans.Count; i++)
                if (_data.KitLoans[i].BorrowerUserId is { } borrower && userIds.Contains(borrower))
                    _data.KitLoans[i] = _data.KitLoans[i] with { BorrowerUserId = null };

            _data.Parts.RemoveAll(x => partIds.Contains(x.Id));
            for (var i = 0; i < _data.Parts.Count; i++)
                _data.Parts[i] = _data.Parts[i] with { SupplierIds = _data.Parts[i].SupplierIds.Where(x => !supplierIds.Contains(x)).ToList() };

            _data.AssetAttributeDefinitions.RemoveAll(x => assetAttributeIds.Contains(x.Id));
            _data.TicketAttributeDefinitions.RemoveAll(x => ticketAttributeIds.Contains(x.Id));

            _data.TicketTemplates.RemoveAll(x => templateIds.Contains(x.Id));
            for (var i = 0; i < _data.TicketTemplates.Count; i++)
            {
                var template = _data.TicketTemplates[i];
                if (template.SlaId is { } sla && slaIds.Contains(sla)) template = template with { SlaId = null };
                foreach (var key in template.AttributeValues.Keys.Where(ticketAttributeIds.Contains).ToList()) template.AttributeValues.Remove(key);
                _data.TicketTemplates[i] = template;
            }
            _data.Slas.RemoveAll(x => slaIds.Contains(x.Id));

            _data.Suppliers.RemoveAll(x => supplierIds.Contains(x.Id));
            _data.Users.RemoveAll(x => userIds.Contains(x.Id));
            _data.Technicians.RemoveAll(x => technicianIds.Contains(x.Id));

            _data.DemoRecords.Clear();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "System", null, null, "Helpdesk", "Demo data removed",
                $"The system went live: {removed} demo {(removed == 1 ? "record was" : "records were")} removed. Option list values were kept."));
            Save();
            return (true, $"Demo data removed - {removed} {(removed == 1 ? "record is" : "records are")} gone and the system is live. "
                + "The option lists it added (departments, makes, part categories) were kept; delete any you don't want from their own settings pages.");
        }
    }

    public string BackupFolder => Path.Combine(Path.GetDirectoryName(_path)!, "backups");

    // Removes everything and puts the system back to how a new install starts (the same demo records Seed creates).
    // The audit log survives unless eraseAudit is set; either way the reset itself is recorded as the first entry.
    public (bool Ok, string Message) ResetFactory(string? confirmation, bool keepBackup = true, bool eraseAudit = false)
    {
        lock (_sync)
        {
            if (!string.Equals(confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
                return (false, "Nothing was changed. Type DELETE in capitals to reset the system.");

            string? backupName = null;
            if (keepBackup)
            {
                try { backupName = BackUpBeforeReset(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
                {
                    return (false, $"Nothing was changed. The backup could not be saved ({ex.Message}). Fix that, or untick the backup option to reset without one.");
                }
            }

            if (eraseAudit)
            {
                using var connection = new SqliteConnection($"Data Source={_path}");
                connection.Open();
                using var transaction = connection.BeginTransaction();
                Execute(connection, transaction, "DELETE FROM AuditLog;");
                transaction.Commit();
                _audit.Clear();
            }
            // The wipe is not audited record by record.
            _pendingAudit.Clear();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "System", null, null, "Helpdesk", "Factory reset",
                "All data was reset to factory settings." + (eraseAudit ? " The previous audit log was erased." : "") + (backupName is null ? " No backup was kept." : $" A backup was saved as {backupName}.")));
            _data = new StoreData();
            SeedStarterData();
            // Without these two the reset leaves no Administrator role and no account that can sign in, which locked
            // the system until the app was restarted. They only ran at startup before.
            EnsureSeedRoles();
            EnsureBootstrapAdministrator();
            // Deleted rows otherwise linger in the file's free pages, which defeats the point of a purge.
            using (var connection = new SqliteConnection($"Data Source={_path}"))
            {
                connection.Open();
                using var vacuum = connection.CreateCommand();
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
            if (File.Exists(_templatePath))
                File.Delete(_templatePath);
            if (File.Exists(_legacyPath))
                File.Delete(_legacyPath);
            DeleteAllAttachmentFiles();
            return (true, $"System reset to factory settings. Sign in again as {BootstrapAdminEmail} with the password {BootstrapAdminPassword}, and change it straight away."
                + (backupName is null ? "" : $" A backup of the old data was saved as {backupName} in App_Data\\backups."));
        }
    }

    // A consistent copy of the database (and the print template, if there is one) before it is wiped.
    private string BackUpBeforeReset()
    {
        Directory.CreateDirectory(BackupFolder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var name = $"helpdesk-before-reset-{stamp}.db";
        using (var source = new SqliteConnection($"Data Source={_path}"))
        using (var target = new SqliteConnection($"Data Source={Path.Combine(BackupFolder, name)}"))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }
        SqliteConnection.ClearAllPools();
        if (File.Exists(_templatePath))
            File.Copy(_templatePath, Path.Combine(BackupFolder, $"print-template-before-reset-{stamp}.docx"));
        // Restoring: put this folder back as App_Data\attachments next to the restored database.
        CopyAttachmentsTo(Path.Combine(BackupFolder, $"attachments-before-reset-{stamp}"));
        return name;
    }
    public void SavePrintTemplate(Stream source)
    {
        lock (_sync)
        {
            using (var destination = File.Create(_templatePath))
                source.CopyTo(destination);
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "Ticket print template", "Uploaded", "The ticket print template was replaced."));
            Save();
        }
    }

    public string RenderPrintTemplate(TicketRecord ticket, UserRecord? requester, TechnicianRecord? technician, IReadOnlyList<AssetRecord> assets)
    {
        if (!File.Exists(_templatePath)) return string.Empty;
        using var document = WordprocessingDocument.Open(_templatePath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        var values = new Dictionary<string, string?>
        {
            ["{{Job.Number}}"] = ticket.Number.ToString(),
            ["{{Job.Title}}"] = ticket.Title,
            ["{{Job.Description}}"] = ticket.Description,
            ["{{Job.Status}}"] = ticket.Status,
            ["{{Job.Priority}}"] = ticket.Priority,
            ["{{Job.Category}}"] = ticket.Category,
            ["{{Job.Created}}"] = ticket.CreatedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm"),
            ["{{Job.Closed}}"] = ticket.ClosedAt?.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? "Not closed",
            // Internal notes never go on a printout.
            ["{{Job.Comments}}"] = ticket.Comments.Count(x => !x.IsInternal) == 0 ? "No comments" : string.Join("\n", ticket.Comments.Where(x => !x.IsInternal).OrderBy(x => x.CreatedAt).Select(x => $"{x.CreatedAt.ToLocalTime():dd MMM yyyy, HH:mm}: {x.Text}")),
            ["{{Requester.Name}}"] = requester?.Name ?? "Unknown",
            ["{{Requester.Email}}"] = requester?.Email ?? "",
            ["{{Requester.Department}}"] = requester?.Department ?? "",
            ["{{Requester.Location}}"] = requester?.Location ?? "",
            ["{{Technician.Name}}"] = technician?.Name ?? "Unassigned",
            ["{{Technician.Email}}"] = technician?.Email ?? "",
            ["{{Technician.Team}}"] = technician?.Team ?? "",
            ["{{Asset.Tag}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.AssetTag)) : "No asset linked",
            ["{{Asset.Type}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Type)) : "",
            ["{{Asset.Model}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Model)) : "",
            ["{{Asset.Serial}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.SerialNumber)) : "",
            ["{{Asset.Location}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Location)) : ""
        };
        var html = new StringBuilder();
        foreach (var element in body.Elements())
        {
            var content = element.InnerText;
            foreach (var value in values) content = content.Replace(value.Key, value.Value ?? "", StringComparison.OrdinalIgnoreCase);
            var encoded = WebUtility.HtmlEncode(content).Replace("\n", "<br />");
            if (element is Table) html.Append($"<div class=\"template-table\">{encoded}</div>");
            else if (!string.IsNullOrWhiteSpace(encoded)) html.Append($"<p>{encoded}</p>");
        }
        return html.ToString();
    }

    public string? DeleteAsset(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.AssetIds.Contains(id)))
                return "This asset is linked to a ticket and cannot be deleted.";
            var item = _data.Assets.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Asset was not found.";
            _data.Assets.Remove(item);
            Save();
            return null;
        }
    }

    public string? DeleteUser(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.RequesterId == id) || _data.Assets.Any(x => x.AssignedUserId == id))
                return "This user is linked to a ticket or asset and cannot be deleted.";
            var item = _data.Users.FirstOrDefault(x => x.Id == id);
            if (item is null) return "User was not found.";
            _data.Users.Remove(item);
            Save();
            return null;
        }
    }

    public string? DeleteTechnician(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.TechnicianId == id))
                return "This technician is assigned to a ticket and cannot be deleted.";
            var item = _data.Technicians.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Technician was not found.";
            if (item.Role == StaffRoles.Administrator && !_data.Technicians.Any(x => x.Id != id && x.Role == StaffRoles.Administrator && x.IsActive))
                return "At least one active Administrator must remain.";
            _data.Technicians.Remove(item);
            Save();
            return null;
        }
    }

    // Kinds use the same names as the Settings option pages: AssetTypes, AssetMakes, AssetModels and Categories.
    public (int Imported, int Skipped) ImportOptions(string kind, IEnumerable<string> values)
    {
        lock (_sync)
        {
            var options = kind switch
            {
                "AssetTypes" => _data.AssetTypes,
                "AssetMakes" => _data.AssetMakes,
                "AssetModels" => _data.AssetModels,
                "Categories" => _data.Categories,
                _ => throw new ArgumentException("Unknown option list.", nameof(kind))
            };
            var imported = 0;
            var skipped = 0;
            foreach (var value in values)
            {
                var item = (value ?? string.Empty).Trim();
                if (item.Length == 0 || options.Contains(item, StringComparer.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
                options.Add(item);
                imported++;
            }
            if (imported > 0) Save();
            return (imported, skipped);
        }
    }
    public (int Imported, int Skipped) ImportUsers(IEnumerable<UserRecord> users)
    {
        lock (_sync)
        {
            var imported = 0;
            var skipped = 0;
            var existingEmails = _data.Users.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users)
            {
                var name = (user.Name ?? string.Empty).Trim();
                var email = (user.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || !existingEmails.Add(email))
                {
                    skipped++;
                    continue;
                }
                var department = (user.Department ?? string.Empty).Trim();
                var location = (user.Location ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(department) && !_data.Departments.Contains(department, StringComparer.OrdinalIgnoreCase))
                    _data.Departments.Add(department);
                if (!string.IsNullOrWhiteSpace(location) && !_data.Locations.Contains(location, StringComparer.OrdinalIgnoreCase))
                    _data.Locations.Add(location);
                _data.Users.Add(user with { Name = name, Email = email, Department = department, Location = location });
                imported++;
            }
            Save();
            return (imported, skipped);
        }
    }
    public (int Imported, int Skipped) ImportTechnicians(IEnumerable<TechnicianRecord> technicians)
    {
        lock (_sync)
        {
            var imported = 0;
            var skipped = 0;
            var existingEmails = _data.Technicians.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var technician in technicians)
            {
                var name = (technician.Name ?? string.Empty).Trim();
                var email = (technician.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || !existingEmails.Add(email))
                {
                    skipped++;
                    continue;
                }
                var team = (technician.Team ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(team) && !_data.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase))
                    _data.TechnicianTeams.Add(team);
                _data.Technicians.Add(technician with { Name = name, Email = email, Team = team });
                imported++;
            }
            Save();
            return (imported, skipped);
        }
    }

    private bool Update<T>(T item, List<T> items, Func<T, bool> match)
    {
        lock (_sync)
        {
            var index = items.FindIndex(x => match(x));
            if (index < 0) return false;
            items[index] = item;
            Save();
            return true;
        }
    }

    private static void AddTicketActivities(List<TicketActivity> history, TicketRecord previous, TicketRecord updated)
    {
        var now = DateTime.UtcNow;
        if (previous.Title != updated.Title) history.Add(new("Title changed", $"{previous.Title} -> {updated.Title}", now));
        if (previous.Description != updated.Description) history.Add(new("Description changed", "The ticket description was updated.", now));
        if (previous.Status != updated.Status) history.Add(new("Status changed", $"{previous.Status} -> {updated.Status}", now));
        if (previous.Priority != updated.Priority) history.Add(new("Priority changed", $"{previous.Priority} -> {updated.Priority}", now));
        if (previous.Category != updated.Category) history.Add(new("Category changed", $"{previous.Category} -> {updated.Category}", now));
        if (previous.Type != updated.Type) history.Add(new("Type changed", $"{previous.Type} -> {updated.Type}", now));
        if (previous.RequesterId != updated.RequesterId) history.Add(new("Requester changed", "The ticket requester was updated.", now));
        if (previous.TechnicianId != updated.TechnicianId) history.Add(new("Technician changed", updated.TechnicianId.HasValue ? "A technician was assigned." : "The technician assignment was removed.", now));
        if (previous.TeamName != updated.TeamName) history.Add(new("Team changed", updated.TeamName is null ? "The team assignment was removed." : $"Assigned to team {updated.TeamName}.", now));
        if (!previous.AssetIds.ToHashSet().SetEquals(updated.AssetIds)) history.Add(new("Assets changed", updated.AssetIds.Count > 0 ? $"Linked assets updated ({updated.AssetIds.Count} linked)." : "All linked assets were removed.", now));
        if (previous.ClosedAt != updated.ClosedAt && previous.Status == updated.Status) history.Add(new("Closure changed", updated.ClosedAt.HasValue ? "The ticket was closed." : "The ticket was reopened.", now));
        if (previous.SlaId != updated.SlaId) history.Add(new("SLA changed", updated.SlaId.HasValue ? "An SLA was assigned." : "The SLA was removed.", now));
        if (previous.DueDate != updated.DueDate || previous.DueDateOverridden != updated.DueDateOverridden) history.Add(new("Due date changed", updated.DueDate.HasValue ? (updated.DueDateOverridden ? "The due date was manually overridden." : "The due date was recalculated from the SLA.") : "The due date was removed.", now));
    }

    private static void AddAssetActivities(List<AssetActivity> history, AssetRecord previous, AssetRecord updated, IReadOnlyList<UserRecord> users)
    {
        var now = DateTime.UtcNow;
        string Person(Guid? id) => users.FirstOrDefault(x => x.Id == id)?.Name ?? "an unknown user";
        static string Date(DateOnly? value) => value.HasValue ? AssetInsights.Format(value.Value) : "(none)";
        static string Money(decimal? value) => value.HasValue ? value.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "(none)";
        static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        if (!string.Equals(previous.Status, updated.Status, StringComparison.Ordinal)) history.Add(new("Status changed", $"{previous.Status} -> {updated.Status}", now));
        if (previous.PurchaseDate != updated.PurchaseDate) history.Add(new("Purchase date changed", $"{Date(previous.PurchaseDate)} -> {Date(updated.PurchaseDate)}", now));
        if (previous.PurchasePrice != updated.PurchasePrice) history.Add(new("Purchase price changed", $"{Money(previous.PurchasePrice)} -> {Money(updated.PurchasePrice)}", now));
        if (!string.Equals(previous.PurchaseOrder, updated.PurchaseOrder, StringComparison.Ordinal)) history.Add(new("Purchase order changed", $"{Text(previous.PurchaseOrder)} -> {Text(updated.PurchaseOrder)}", now));
        if (previous.WarrantyEnd != updated.WarrantyEnd) history.Add(new("Warranty end changed", $"{Date(previous.WarrantyEnd)} -> {Date(updated.WarrantyEnd)}", now));
        if (previous.ReplacementDate != updated.ReplacementDate) history.Add(new("Replacement date changed", $"{Date(previous.ReplacementDate)} -> {Date(updated.ReplacementDate)}", now));
        if (previous.LoanDueDate != updated.LoanDueDate) history.Add(new("Loan due date changed", updated.LoanDueDate.HasValue ? $"Due back {Date(updated.LoanDueDate)}." : "The loan due date was cleared.", now));
        if (previous.AssetTag != updated.AssetTag) history.Add(new("Asset tag changed", $"{previous.AssetTag} -> {updated.AssetTag}", now));
        if (previous.Make != updated.Make) history.Add(new("Make changed", $"{previous.Make} -> {updated.Make}", now));
        if (previous.Model != updated.Model) history.Add(new("Model changed", $"{previous.Model} -> {updated.Model}", now));
        if (previous.Type != updated.Type) history.Add(new("Type changed", $"{previous.Type} -> {updated.Type}", now));
        if (previous.SerialNumber != updated.SerialNumber) history.Add(new("Serial number changed", $"{previous.SerialNumber} -> {updated.SerialNumber}", now));
        if (previous.Location != updated.Location) history.Add(new("Location changed", string.IsNullOrWhiteSpace(updated.Location) ? "The location was removed." : $"Moved to {updated.Location}.", now));
        if (previous.AssignedUserId != updated.AssignedUserId) history.Add(new("Assigned user changed", updated.AssignedUserId.HasValue ? $"Assigned to {Person(updated.AssignedUserId)}." : $"No longer assigned to {Person(previous.AssignedUserId)}.", now));
        if (previous.SupplierId != updated.SupplierId) history.Add(new("Supplier changed", updated.SupplierId.HasValue ? "A supplier was linked." : "The supplier was removed.", now));
    }

    private StoreData Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        EnsureSchema(connection);

        var version = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SchemaVersion';");
        if (version is null)
        {
            StoreData? migrated = null;
            var payload = TableExists(connection, "Store") ? ExecuteScalar(connection, "SELECT Payload FROM Store WHERE Id = 1;") : null;
            if (payload is string json && !string.IsNullOrWhiteSpace(json))
            {
                try { migrated = JsonSerializer.Deserialize<StoreData>(json); } catch (JsonException) { }
            }
            if (migrated is null && File.Exists(_legacyPath))
            {
                try { migrated = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(_legacyPath)); } catch (JsonException) { }
            }
            if (migrated is not null)
            {
                foreach (var team in migrated.Technicians.Select(x => x.Team).Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (!migrated.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase)) migrated.TechnicianTeams.Add(team);
                foreach (var department in migrated.Users.Select(x => x.Department).Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (!migrated.Departments.Contains(department, StringComparer.OrdinalIgnoreCase)) migrated.Departments.Add(department);
                using var transaction = connection.BeginTransaction();
                WriteData(connection, transaction, migrated);
                SetMetadata(connection, transaction, "SchemaVersion", "8");
                DropLegacyStore(connection, transaction);
                transaction.Commit();
                return migrated;
            }
            using var marker = connection.CreateCommand();
            marker.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES ('SchemaVersion', '5');";
            marker.ExecuteNonQuery();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS Store;";
            drop.ExecuteNonQuery();
        }
        else if (TableExists(connection, "Store"))
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS Store;";
            drop.ExecuteNonQuery();
        }
        return ReadData(connection);
    }

    private void Save() => Persist(auditChanges: true);

    // Saves without auditing the differences: used at startup and after seeding, when the change is not a user action.
    private void SaveBaseline() => Persist(auditChanges: false);

    private void Persist(bool auditChanges)
    {
        var current = AuditTracker.Take(_data);
        var entries = new List<AuditEntry>(_pendingAudit);
        if (auditChanges && _snapshot is not null) entries.AddRange(AuditTracker.Diff(_snapshot, current, DateTime.UtcNow));
        // Every audit entry funnels through here, whether it came from diffing or was queued by a mutator, so this is
        // the one place attribution has to happen. It applies to baseline saves too: a factory reset does not diff, but
        // it is still very much something a person did. An entry that already named its actor keeps it.
        var actor = CurrentActor();
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].By is null) entries[i] = entries[i] with { By = actor };

        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        WriteData(connection, transaction, _data);
        SetMetadata(connection, transaction, "SchemaVersion", "5");
        foreach (var entry in entries)
            Execute(connection, transaction, "INSERT INTO AuditLog (At, Area, EntityType, EntityKey, Entity, Action, Details, Actor, ActorId) VALUES ($at,$area,$type,$key,$entity,$action,$details,$actor,$actorid);",
                ("$at", Iso(entry.At)), ("$area", entry.Area), ("$type", entry.EntityType), ("$key", entry.EntityKey), ("$entity", entry.Entity), ("$action", entry.Action), ("$details", entry.Details),
                ("$actor", entry.By?.Name), ("$actorid", entry.By?.Id?.ToString()));
        transaction.Commit();

        _audit.AddRange(entries);
        _pendingAudit.Clear();
        _snapshot = current;
    }

    private List<AuditEntry> LoadAudit()
    {
        var entries = new List<AuditEntry>();
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT At, Area, EntityType, EntityKey, Entity, Action, Details, Actor, ActorId FROM AuditLog ORDER BY Id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            entries.Add(new AuditEntry(Date(reader, 0), reader.GetString(1), NullableString(reader, 2), NullableString(reader, 3), reader.GetString(4), reader.GetString(5), reader.GetString(6))
            {
                By = ReadActor(reader, 7, 8)
            });
        return entries;
    }

    // Everything that has happened, newest first: the audit log plus the history and comments already kept on tickets and assets.
    public IReadOnlyList<AuditEntry> GetAuditEntries()
    {
        lock (_sync)
        {
            var entries = new List<AuditEntry>(_audit);
            foreach (var ticket in _data.Tickets)
            {
                var label = $"#{ticket.Number} {ticket.Title}";
                var key = ticket.Number.ToString();
                entries.AddRange(ticket.History.Select(x => new AuditEntry(x.CreatedAt, "Tickets", "Ticket", key, label, x.Action, x.Details) { By = x.By }));
                entries.AddRange(ticket.Comments.Where(x => !x.Text.StartsWith("(Merged from #", StringComparison.Ordinal))
                    .Select(x => new AuditEntry(x.CreatedAt, "Tickets", "Ticket", key, label, x.IsInternal ? "Internal note added" : "Comment added", x.Text) { By = x.By }));
            }
            foreach (var asset in _data.Assets)
            {
                var key = asset.Id.ToString();
                entries.AddRange(asset.History.Select(x => new AuditEntry(x.CreatedAt, "Assets", "Asset", key, asset.AssetTag, x.Action, x.Details) { By = x.By }));
                entries.AddRange(asset.Comments.Select(x => new AuditEntry(x.CreatedAt, "Assets", "Asset", key, asset.AssetTag, "Comment added", x.Text) { By = x.By }));
            }
            foreach (var part in _data.Parts)
            {
                var key = part.Id.ToString();
                entries.AddRange(part.History.Select(x => new AuditEntry(x.CreatedAt, "Parts", "Part", key, part.Name, x.Action, x.Details) { By = x.By }));
            }
            return entries.OrderByDescending(x => x.At).ToList();
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT NULL);
            CREATE TABLE IF NOT EXISTS Users (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Email TEXT NOT NULL, Department TEXT NULL, Location TEXT NULL);
            CREATE TABLE IF NOT EXISTS TechnicianTeams (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Technicians (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Email TEXT NOT NULL, Team TEXT NULL,
                FOREIGN KEY (Team) REFERENCES TechnicianTeams(Name) ON UPDATE CASCADE);
            CREATE TABLE IF NOT EXISTS Departments (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Locations (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetTypes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetMakes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetModels (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetModelMakes (Model TEXT PRIMARY KEY, Make TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Categories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Statuses (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS StatusDescriptions (Status TEXT PRIMARY KEY, Description TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS RequireCloseMessagePriorities (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS RequireCloseMessageCategories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Priorities (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Slas (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, Duration INTEGER NOT NULL, DurationUnit TEXT NOT NULL, Priority TEXT NULL, Description TEXT NULL);
            CREATE TABLE IF NOT EXISTS SlaPriorities (SlaId TEXT NOT NULL, Priority TEXT NOT NULL, PRIMARY KEY (SlaId, Priority), FOREIGN KEY (SlaId) REFERENCES Slas(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS SlaCategories (SlaId TEXT NOT NULL, Category TEXT NOT NULL, PRIMARY KEY (SlaId, Category), FOREIGN KEY (SlaId) REFERENCES Slas(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS Suppliers (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, ContactName TEXT NOT NULL DEFAULT '', Email TEXT NOT NULL DEFAULT '', Phone TEXT NOT NULL DEFAULT '', AddressLine1 TEXT NOT NULL DEFAULT '', AddressLine2 TEXT NOT NULL DEFAULT '', City TEXT NOT NULL DEFAULT '', StateRegion TEXT NOT NULL DEFAULT '', PostalCode TEXT NOT NULL DEFAULT '', Country TEXT NOT NULL DEFAULT '', Website TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Parts (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Sku TEXT NOT NULL DEFAULT '', Category TEXT NOT NULL DEFAULT '', QuantityOnHand INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TicketParts (TicketNumber INTEGER NOT NULL, PartId TEXT NOT NULL, Quantity INTEGER NOT NULL,
                PRIMARY KEY (TicketNumber, PartId),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS Assets (Id TEXT PRIMARY KEY, AssetTag TEXT NOT NULL, Make TEXT NOT NULL DEFAULT '', Type TEXT NOT NULL, Model TEXT NOT NULL,
                SerialNumber TEXT NOT NULL, Location TEXT NOT NULL, AssignedUserId TEXT NULL, SupplierId TEXT NULL,
                FOREIGN KEY (AssignedUserId) REFERENCES Users(Id) ON DELETE SET NULL);
            CREATE TABLE IF NOT EXISTS Tickets (Number INTEGER PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NOT NULL,
                RequesterId TEXT NOT NULL, AssetId TEXT NULL, TechnicianId TEXT NULL, Priority TEXT NOT NULL, Status TEXT NOT NULL,
                Category TEXT NOT NULL, CreatedAt TEXT NOT NULL, ClosedAt TEXT NULL, SlaId TEXT NULL, DueDate TEXT NULL, DueDateOverridden INTEGER NOT NULL DEFAULT 0, SlaOverridden INTEGER NOT NULL DEFAULT 0, TeamName TEXT NULL,
                FOREIGN KEY (RequesterId) REFERENCES Users(Id), FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE SET NULL,
                FOREIGN KEY (TechnicianId) REFERENCES Technicians(Id) ON DELETE SET NULL);
            CREATE TABLE IF NOT EXISTS TicketComments (Id INTEGER PRIMARY KEY AUTOINCREMENT, TicketNumber INTEGER NOT NULL,
                Text TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, TicketNumber INTEGER NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS AssetComments (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL,
                Text TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS AssetActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS BrandingSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), BrandName TEXT NOT NULL,
                DashboardEyebrow TEXT NOT NULL, DashboardTitle TEXT NOT NULL, DashboardDescription TEXT NOT NULL,
                PrimaryColor TEXT NOT NULL, AccentColor TEXT NOT NULL, BackgroundColor TEXT NOT NULL, DarkMode INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS AssetAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, AssetType TEXT NULL,
                FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS AssetAttributeValues (AssetId TEXT NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (AssetId, AttributeDefinitionId),
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE,
                FOREIGN KEY (AttributeDefinitionId) REFERENCES AssetAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Category TEXT NULL, FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS TicketAttributeValues (TicketNumber INTEGER NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, AttributeDefinitionId), FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (AttributeDefinitionId) REFERENCES TicketAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAssets (TicketNumber INTEGER NOT NULL, AssetId TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, AssetId),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
        using var migration = connection.CreateCommand();
        migration.CommandText = "ALTER TABLE Assets ADD COLUMN Make TEXT NOT NULL DEFAULT '';";
        try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var supplierMigration = connection.CreateCommand();
        supplierMigration.CommandText = "ALTER TABLE Assets ADD COLUMN SupplierId TEXT NULL;";
        try { supplierMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var attributeMigration = connection.CreateCommand();
        attributeMigration.CommandText = "ALTER TABLE AssetAttributeDefinitions ADD COLUMN FieldType TEXT NOT NULL DEFAULT 'single-line';";
        try { attributeMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var choicesMigration = connection.CreateCommand();
        choicesMigration.CommandText = "ALTER TABLE AssetAttributeDefinitions ADD COLUMN Choices TEXT NOT NULL DEFAULT '';";
        try { choicesMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var slaMigration = connection.CreateCommand();
        slaMigration.CommandText = "ALTER TABLE Tickets ADD COLUMN SlaId TEXT NULL; ALTER TABLE Tickets ADD COLUMN DueDate TEXT NULL; ALTER TABLE Tickets ADD COLUMN DueDateOverridden INTEGER NOT NULL DEFAULT 0; ALTER TABLE Tickets ADD COLUMN SlaOverridden INTEGER NOT NULL DEFAULT 0; ALTER TABLE Tickets ADD COLUMN TeamName TEXT NULL;";
        try { slaMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        foreach (var sql in new[] { "ALTER TABLE Slas ADD COLUMN Priority TEXT NULL;", "ALTER TABLE Slas ADD COLUMN Description TEXT NULL;", "ALTER TABLE TicketAttributeDefinitions ADD COLUMN FieldType TEXT NOT NULL DEFAULT 'single-line';", "ALTER TABLE TicketAttributeDefinitions ADD COLUMN Choices TEXT NOT NULL DEFAULT '';" })
        { using var m = connection.CreateCommand(); m.CommandText = sql; try { m.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { } }
        MigrateAssetAttributeTypeToNullable(connection);
        using var scopeTables = connection.CreateCommand();
        scopeTables.CommandText = """
            CREATE TABLE IF NOT EXISTS AssetAttributeAssetTypes (AttributeId TEXT NOT NULL, AssetType TEXT NOT NULL,
                PRIMARY KEY (AttributeId, AssetType), FOREIGN KEY (AttributeId) REFERENCES AssetAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAttributeCategories (AttributeId TEXT NOT NULL, Category TEXT NOT NULL,
                PRIMARY KEY (AttributeId, Category), FOREIGN KEY (AttributeId) REFERENCES TicketAttributeDefinitions(Id) ON DELETE CASCADE);
            """;
        scopeTables.ExecuteNonQuery();
        using var auditTable = connection.CreateCommand();
        auditTable.CommandText = "CREATE TABLE IF NOT EXISTS AuditLog (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Area TEXT NOT NULL, EntityType TEXT NULL, EntityKey TEXT NULL, Entity TEXT NOT NULL, Action TEXT NOT NULL, Details TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL);";
        auditTable.ExecuteNonQuery();
        using var rolePermissionTable = connection.CreateCommand();
        // No foreign key to Roles(Name): WriteData deletes and reinserts everything, and a role rename would otherwise
        // have to be ordered against this table. Rows whose role no longer exists are simply ignored on load.
        rolePermissionTable.CommandText = "CREATE TABLE IF NOT EXISTS RolePermissions (RoleName TEXT NOT NULL, Permission TEXT NOT NULL, PRIMARY KEY (RoleName, Permission));";
        rolePermissionTable.ExecuteNonQuery();
        using var demoTable = connection.CreateCommand();
        // No foreign keys on purpose: deleting a demo record by hand should leave a harmless stale pointer here, not
        // break the next save. RemoveDemoData and the Settings panel both ignore ids that no longer resolve.
        demoTable.CommandText = "CREATE TABLE IF NOT EXISTS DemoRecords (EntityType TEXT NOT NULL, EntityKey TEXT NOT NULL, PRIMARY KEY (EntityType, EntityKey));";
        demoTable.ExecuteNonQuery();
        foreach (var sql in new[]
        {
            "ALTER TABLE Assets ADD COLUMN Status TEXT NOT NULL DEFAULT 'In use';",
            "ALTER TABLE Assets ADD COLUMN PurchaseDate TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN PurchasePrice TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN PurchaseOrder TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Assets ADD COLUMN WarrantyEnd TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN ReplacementDate TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN LoanDueDate TEXT NULL;",
            "ALTER TABLE TicketComments ADD COLUMN IsInternal INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Tickets ADD COLUMN TicketType TEXT NOT NULL DEFAULT 'Incident';",
            "ALTER TABLE Tickets ADD COLUMN Location TEXT NULL;",
            "ALTER TABLE Users ADD COLUMN PasswordHash TEXT NULL;",
            "ALTER TABLE Users ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE Technicians ADD COLUMN Role TEXT NOT NULL DEFAULT 'Technician';",
            "ALTER TABLE Technicians ADD COLUMN PasswordHash TEXT NULL;",
            "ALTER TABLE Technicians ADD COLUMN RequirePasswordChange INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Technicians ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;",
            // Finance and audit reporting: the supplier's quote number (orders placed via the Trust often have no PO),
            // and disposal, which is a status rather than a delete so the record survives for an auditor.
            "ALTER TABLE Assets ADD COLUMN QuoteReference TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Assets ADD COLUMN DisposalDate TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN DisposalMethod TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Assets ADD COLUMN DisposalProceeds TEXT NULL;",
            "ALTER TABLE Parts ADD COLUMN Location TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Parts ADD COLUMN ReorderThreshold INTEGER NULL;",
            // Actor attribution. Nullable on purpose: everything recorded before this existed keeps no actor rather
            // than being backfilled with a guess, so "no name shown" honestly means "we did not record it".
            "ALTER TABLE AuditLog ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE AuditLog ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE TicketActivities ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE TicketActivities ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE TicketComments ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE TicketComments ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE AssetActivities ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE AssetActivities ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE AssetComments ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE AssetComments ADD COLUMN ActorId TEXT NULL;",
            // PartActivities is created further down, after this loop, so these two only ever upgrade a database that
            // already has the table - a brand-new one gets the columns from its CREATE instead.
            "ALTER TABLE PartActivities ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE PartActivities ADD COLUMN ActorId TEXT NULL;",
            // AssetAssignments is created after this loop too, so the same applies - see its CREATE below.
            "ALTER TABLE AssetAssignments ADD COLUMN Reason TEXT NULL;",
            "ALTER TABLE AssetAssignments ADD COLUMN KitLoanId TEXT NULL;"
        })
        {
            using var m = connection.CreateCommand();
            m.CommandText = sql;
            try { m.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
        using var assetTables = connection.CreateCommand();
        assetTables.CommandText = """
            CREATE TABLE IF NOT EXISTS AssetStatuses (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS PartCategories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS PartLocations (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetTypeLifespans (AssetType TEXT PRIMARY KEY, Years INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS AssetAssignments (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL, UserId TEXT NULL, UserName TEXT NOT NULL,
                StartedAt TEXT NULL, EndedAt TEXT NULL, DueBack TEXT NULL, Reason TEXT NULL, KitLoanId TEXT NULL,
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            """;
        assetTables.ExecuteNonQuery();
        using var ticketTables = connection.CreateCommand();
        ticketTables.CommandText = """
            CREATE TABLE IF NOT EXISTS TicketAttachments (Id TEXT PRIMARY KEY, TicketNumber INTEGER NOT NULL, FileName TEXT NOT NULL, ContentType TEXT NOT NULL,
                Size INTEGER NOT NULL, UploadedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketLinks (TicketNumber INTEGER NOT NULL, LinkedNumber INTEGER NOT NULL, Kind TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, LinkedNumber),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE, FOREIGN KEY (LinkedNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketTemplates (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, TicketType TEXT NOT NULL, Title TEXT NOT NULL, Description TEXT NOT NULL,
                Category TEXT NOT NULL, Priority TEXT NOT NULL, SlaId TEXT NULL);
            CREATE TABLE IF NOT EXISTS TicketTemplateAttributes (TemplateId TEXT NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (TemplateId, AttributeDefinitionId), FOREIGN KEY (TemplateId) REFERENCES TicketTemplates(Id) ON DELETE CASCADE);
            """;
        ticketTables.ExecuteNonQuery();
        using var partTables = connection.CreateCommand();
        partTables.CommandText = """
            CREATE TABLE IF NOT EXISTS PartSuppliers (PartId TEXT NOT NULL, SupplierId TEXT NOT NULL,
                PRIMARY KEY (PartId, SupplierId),
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE,
                FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PartAssetTypes (PartId TEXT NOT NULL, AssetType TEXT NOT NULL,
                PRIMARY KEY (PartId, AssetType), FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PartActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, PartId TEXT NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE);
            """;
        partTables.ExecuteNonQuery();
        using var loanTables = connection.CreateCommand();
        loanTables.CommandText = """
            CREATE TABLE IF NOT EXISTS LoanReasons (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS LoanKits (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Notes TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL, IsRetired INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS LoanKitAssets (KitId TEXT NOT NULL, AssetId TEXT NOT NULL,
                PRIMARY KEY (KitId, AssetId),
                FOREIGN KEY (KitId) REFERENCES LoanKits(Id) ON DELETE CASCADE,
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS KitLoans (Id TEXT PRIMARY KEY, KitId TEXT NOT NULL, BorrowerUserId TEXT NULL,
                BorrowerName TEXT NOT NULL, Reason TEXT NOT NULL, IssuedAt TEXT NOT NULL, DueBack TEXT NOT NULL,
                ReturnedAt TEXT NULL, IssuedBy TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (KitId) REFERENCES LoanKits(Id) ON DELETE CASCADE);
            """;
        loanTables.ExecuteNonQuery();
        using var roleTable = connection.CreateCommand();
        roleTable.CommandText = """
            CREATE TABLE IF NOT EXISTS Roles (Name TEXT PRIMARY KEY, AllowSettings INTEGER NOT NULL DEFAULT 0, AllowManageRoles INTEGER NOT NULL DEFAULT 0,
                AllowManageStaff INTEGER NOT NULL DEFAULT 0, AllowManageRequesters INTEGER NOT NULL DEFAULT 0, AllowManageAssets INTEGER NOT NULL DEFAULT 0,
                AllowManageSuppliers INTEGER NOT NULL DEFAULT 0, AllowManageParts INTEGER NOT NULL DEFAULT 0, AllowTicketDestructive INTEGER NOT NULL DEFAULT 0,
                AllowChangeWorkingAs INTEGER NOT NULL DEFAULT 0, IsProtected INTEGER NOT NULL DEFAULT 0);
            """;
        roleTable.ExecuteNonQuery();
    }

    private static void MigrateAssetAttributeTypeToNullable(SqliteConnection connection)
    {
        var assetTypeIsNotNull = false;
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(AssetAttributeDefinitions);";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "AssetType", StringComparison.OrdinalIgnoreCase))
                    assetTypeIsNotNull = reader.GetInt32(3) == 1;
            }
        }
        if (!assetTypeIsNotNull) return;
        using var migrate = connection.CreateCommand();
        migrate.CommandText = """
            ALTER TABLE AssetAttributeDefinitions RENAME TO AssetAttributeDefinitions_old;
            CREATE TABLE AssetAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, AssetType TEXT NULL, FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            INSERT INTO AssetAttributeDefinitions (Id, Name, AssetType, FieldType, Choices) SELECT Id, Name, AssetType, FieldType, Choices FROM AssetAttributeDefinitions_old;
            DROP TABLE AssetAttributeDefinitions_old;
            """;
        migrate.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static void DropLegacyStore(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DROP TABLE IF EXISTS Store;";
        command.ExecuteNonQuery();
    }

    private static void SetMetadata(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string Iso(DateTime value) => value.ToUniversalTime().ToString("O");
    private static string? IsoDay(DateOnly? value) => value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static DateOnly? NullableDateOnly(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) || !DateOnly.TryParseExact(reader.GetString(index), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value) ? null : value;
    private static DateTime Date(SqliteDataReader reader, int index) => DateTime.Parse(reader.GetString(index), null, System.Globalization.DateTimeStyles.RoundtripKind);
    private static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static Guid? NullableGuid(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : Guid.Parse(reader.GetString(index));
    // The name column is what decides whether there is an actor at all: rows written before attribution was added have
    // neither, and a portal action has a name but no account id.
    private static Actor? ReadActor(SqliteDataReader reader, int nameIndex, int idIndex) =>
        NullableString(reader, nameIndex) is { Length: > 0 } name ? new Actor(NullableGuid(reader, idIndex), name) : null;

    private static StoreData ReadData(SqliteConnection connection)
    {
        var data = new StoreData();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Email, Department, Location, PasswordHash, IsActive FROM Users;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Users.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), NullableString(reader, 3) ?? "", NullableString(reader, 4) ?? "", NullableString(reader, 5), reader.GetInt32(6) != 0));
        }
        ReadStrings(connection, "TechnicianTeams", data.TechnicianTeams);
        ReadStrings(connection, "Departments", data.Departments);
        ReadStrings(connection, "Locations", data.Locations);
        ReadStrings(connection, "AssetTypes", data.AssetTypes);
        ReadStrings(connection, "AssetMakes", data.AssetMakes);
        ReadStrings(connection, "AssetModels", data.AssetModels);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Model, Make FROM AssetModelMakes;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetModelMakes[reader.GetString(0)] = reader.GetString(1);
        }
        ReadStrings(connection, "AssetStatuses", data.AssetStatuses);
        ReadStrings(connection, "PartCategories", data.PartCategories);
        ReadStrings(connection, "PartLocations", data.PartLocations);
        ReadStrings(connection, "LoanReasons", data.LoanReasons);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Notes, CreatedAt, IsRetired FROM LoanKits;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.LoanKits.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), Date(reader, 3))
                {
                    IsRetired = reader.GetInt32(4) != 0
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT KitId, AssetId FROM LoanKitAssets;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.LoanKits.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.LoanKits[index] = data.LoanKits[index] with { AssetIds = [.. data.LoanKits[index].AssetIds, Guid.Parse(reader.GetString(1))] };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, KitId, BorrowerUserId, BorrowerName, Reason, IssuedAt, DueBack, ReturnedAt, IssuedBy, Notes FROM KitLoans;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.KitLoans.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), NullableGuid(reader, 2),
                    reader.GetString(3), reader.GetString(4), Date(reader, 5), NullableDateOnly(reader, 6) ?? AssetInsights.Today,
                    reader.IsDBNull(7) ? null : Date(reader, 7), reader.GetString(8), reader.GetString(9)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AssetType, Years FROM AssetTypeLifespans;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetTypeLifespans[reader.GetString(0)] = reader.GetInt32(1);
        }
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'AssetReviewDays';") as string, out var reviewDays)) data.AssetReviewDays = reviewDays;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'AcademicYearStartMonth';") as string, out var academicStart)) data.AcademicYearStartMonth = academicStart;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'PermissionModelVersion';") as string, out var permissionVersion)) data.PermissionModelVersion = permissionVersion;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'TicketDueSoonHours';") as string, out var dueSoonHours)) data.TicketDueSoonHours = dueSoonHours;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'PartsDefaultReorderThreshold';") as string, out var reorderThreshold)) data.PartsDefaultReorderThreshold = reorderThreshold;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LoanRepeatCount';") as string, out var loanCount)) data.LoanRepeatCount = loanCount;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LoanRepeatDays';") as string, out var loanDays) ) data.LoanRepeatDays = loanDays;
        ReadStrings(connection, "Categories", data.Categories);
        ReadStrings(connection, "Statuses", data.Statuses);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Status, Description FROM StatusDescriptions;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.StatusDescriptions[reader.GetString(0)] = reader.GetString(1);
        }
        ReadStrings(connection, "Priorities", data.Priorities);
        ReadStrings(connection, "RequireCloseMessagePriorities", data.RequireCloseMessagePriorities);
        ReadStrings(connection, "RequireCloseMessageCategories", data.RequireCloseMessageCategories);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Duration, DurationUnit, Priority, Description FROM Slas ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slaId = Guid.Parse(reader.GetString(0));
                var legacyPriority = NullableString(reader, 4);
                data.Slas.Add(new(slaId, reader.GetString(1), reader.GetInt32(2), NormalizeDurationUnit(reader.GetString(3)), NullableString(reader, 5))
                {
                    Priorities = legacyPriority is not null ? [legacyPriority] : []
                });
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT SlaId, Priority FROM SlaPriorities;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slaId = Guid.Parse(reader.GetString(0));
                var priority = reader.GetString(1);
                var index = data.Slas.FindIndex(x => x.Id == slaId);
                if (index >= 0 && !data.Slas[index].Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase))
                    data.Slas[index] = data.Slas[index] with { Priorities = data.Slas[index].Priorities.Append(priority).ToList() };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT SlaId, Category FROM SlaCategories;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slaId = Guid.Parse(reader.GetString(0));
                var category = reader.GetString(1);
                var index = data.Slas.FindIndex(x => x.Id == slaId);
                if (index >= 0) data.Slas[index] = data.Slas[index] with { Categories = data.Slas[index].Categories.Append(category).ToList() };
            }
        }
        using (var command = connection.CreateCommand())
        {
            // The legacy single AssetType column seeds the scope; rows in AssetAttributeAssetTypes are merged in.
            command.CommandText = "SELECT Id, Name, AssetType, FieldType, Choices FROM AssetAttributeDefinitions ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var legacyAssetType = NullableString(reader, 2);
                data.AssetAttributeDefinitions.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(3), reader.GetString(4))
                {
                    AssetTypes = string.IsNullOrWhiteSpace(legacyAssetType) ? [] : [legacyAssetType]
                });
            }
        }
        ReadAttributeScope(connection, "AssetAttributeAssetTypes", "AssetType", (id, assetType) =>
        {
            var definition = data.AssetAttributeDefinitions.FirstOrDefault(x => x.Id == id);
            if (definition is not null && !definition.AssetTypes.Contains(assetType, StringComparer.OrdinalIgnoreCase)) definition.AssetTypes.Add(assetType);
        });
        using (var command = connection.CreateCommand())
        {
            // The legacy single Category column seeds the scope; rows in TicketAttributeCategories are merged in.
            command.CommandText = "SELECT Id, Name, Category, FieldType, Choices FROM TicketAttributeDefinitions ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var legacyCategory = NullableString(reader, 2);
                data.TicketAttributeDefinitions.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(3), reader.GetString(4))
                {
                    Categories = string.IsNullOrWhiteSpace(legacyCategory) ? [] : [legacyCategory]
                });
            }
        }
        ReadAttributeScope(connection, "TicketAttributeCategories", "Category", (id, category) =>
        {
            var definition = data.TicketAttributeDefinitions.FirstOrDefault(x => x.Id == id);
            if (definition is not null && !definition.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)) definition.Categories.Add(category);
        });
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, AttributeDefinitionId, Value FROM TicketAttributeValues;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketAttributeValues.Add(new(reader.GetInt32(0), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            // The nine Allow* columns from the original permission model are still in the table but are neither read
            // nor written: roles were emptied when permissions moved to independent ticks, so there is nothing left to
            // convert from. They stay only because dropping a column rewrites the table.
            command.CommandText = "SELECT Name, IsProtected FROM Roles ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Roles.Add(new RoleRecord(reader.GetString(0), reader.GetInt32(1) != 0));
        }
        using (var command = connection.CreateCommand())
        {
            // One row per ticked box, stored as "Assets:Edit". Nothing is implied by anything else, so every action a
            // role holds has its own row. Anything without a colon is a flag.
            command.CommandText = "SELECT RoleName, Permission FROM RolePermissions;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var role = data.Roles.FirstOrDefault(x => string.Equals(x.Name, reader.GetString(0), StringComparison.OrdinalIgnoreCase));
                if (role is null) continue;
                var permission = reader.GetString(1);
                var split = permission.IndexOf(':');
                if (split < 0) { role.Flags.Add(permission); continue; }
                var action = ModulePermissions.Parse(permission[(split + 1)..]);
                if (action != ModulePermission.None) role.Grants[permission[..split]] = role.GrantsFor(permission[..split]) | action;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Email, Team, Role, PasswordHash, RequirePasswordChange, IsActive FROM Technicians;";
            using var reader = command.ExecuteReader();
            // Trusts the stored value as-is rather than validating against Roles here: on first run after an upgrade the
            // Roles table is still being seeded (see EnsureSeedRoles, called after Load()), so it can't be checked yet.
            // Permission lookups (RoleGrants) are case-insensitive, so this doesn't need to be exact.
            while (reader.Read()) data.Technicians.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), NullableString(reader, 3) ?? "",
                NullableString(reader, 4) is { Length: > 0 } role ? role : StaffRoles.DefaultRole, NullableString(reader, 5), reader.GetInt32(6) != 0, reader.GetInt32(7) != 0));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AssetId, AttributeDefinitionId, Value FROM AssetAttributeValues;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetAttributeValues.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, ContactName, Email, Phone, AddressLine1, AddressLine2, City, StateRegion, PostalCode, Country, Website, Notes, CreatedAt FROM Suppliers;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Suppliers.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), Date(reader, 13)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Sku, Category, QuantityOnHand, CreatedAt, Location, ReorderThreshold FROM Parts;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.Parts.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), Date(reader, 5))
                {
                    Location = NullableString(reader, 6) ?? "",
                    ReorderThreshold = reader.IsDBNull(7) ? null : reader.GetInt32(7)
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PartId, SupplierId FROM PartSuppliers;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.Parts.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.Parts[index] = data.Parts[index] with { SupplierIds = [.. data.Parts[index].SupplierIds, Guid.Parse(reader.GetString(1))] };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PartId, AssetType FROM PartAssetTypes;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.Parts.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.Parts[index] = data.Parts[index] with { AssetTypes = [.. data.Parts[index].AssetTypes, reader.GetString(1)] };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT EntityType, EntityKey FROM DemoRecords;";
            using var demoReader = command.ExecuteReader();
            while (demoReader.Read()) data.DemoRecords.Add(new DemoRecord(demoReader.GetString(0), demoReader.GetString(1)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PartId, Action, Details, CreatedAt, Actor, ActorId FROM PartActivities ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.Parts.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.Parts[index].History.Add(new(reader.GetString(1), reader.GetString(2), Date(reader, 3)) { By = ReadActor(reader, 4, 5) });
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, PartId, Quantity FROM TicketParts;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketParts.Add(new(reader.GetInt32(0), Guid.Parse(reader.GetString(1)), reader.GetInt32(2)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, TicketType, Title, Description, Category, Priority, SlaId FROM TicketTemplates ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketTemplates.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), TicketTypes.Normalize(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), NullableGuid(reader, 7)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TemplateId, AttributeDefinitionId, Value FROM TicketTemplateAttributes;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (data.TicketTemplates.FirstOrDefault(x => x.Id == Guid.Parse(reader.GetString(0))) is { } template)
                    template.AttributeValues[Guid.Parse(reader.GetString(1))] = reader.GetString(2);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, TicketNumber, FileName, ContentType, Size, UploadedAt FROM TicketAttachments ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketAttachments.Add(new(Guid.Parse(reader.GetString(0)), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), Date(reader, 5)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, LinkedNumber, Kind FROM TicketLinks ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketLinks.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            // This reader is ordinal-indexed, so new columns are appended to the END of the SELECT. Inserting one in the
            // middle shifts every ordinal after it and silently scrambles the whole register - which is exactly how the
            // Type/Model swap bug happened.
            command.CommandText = "SELECT Id, AssetTag, Make, Type, Model, SerialNumber, Location, AssignedUserId, SupplierId, Status, PurchaseDate, PurchasePrice, PurchaseOrder, WarrantyEnd, ReplacementDate, LoanDueDate, QuoteReference, DisposalDate, DisposalMethod, DisposalProceeds FROM Assets;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Assets.Add(new AssetRecord(Guid.Parse(reader.GetString(0)), NullableString(reader, 1) ?? "", NullableString(reader, 2) ?? "", NullableString(reader, 4) ?? "", NullableString(reader, 3) ?? "", NullableString(reader, 5) ?? "", NullableString(reader, 6) ?? "", NullableGuid(reader, 7), NullableGuid(reader, 8))
            {
                Status = NullableString(reader, 9) ?? "In use",
                PurchaseDate = NullableDateOnly(reader, 10),
                PurchasePrice = NullableString(reader, 11) is { } price && decimal.TryParse(price, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice) ? parsedPrice : null,
                PurchaseOrder = NullableString(reader, 12) ?? "",
                WarrantyEnd = NullableDateOnly(reader, 13),
                ReplacementDate = NullableDateOnly(reader, 14),
                LoanDueDate = NullableDateOnly(reader, 15),
                QuoteReference = NullableString(reader, 16) ?? "",
                DisposalDate = NullableDateOnly(reader, 17),
                DisposalMethod = NullableString(reader, 18) ?? "",
                DisposalProceeds = NullableString(reader, 19) is { } proceeds && decimal.TryParse(proceeds, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsedProceeds) ? parsedProceeds : null
            });
        }
        foreach (var asset in data.Assets) ReadAssetChildren(connection, asset);
        var ticketAssetMap = new Dictionary<int, List<Guid>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, AssetId FROM TicketAssets ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var number = reader.GetInt32(0);
                var assetId = Guid.Parse(reader.GetString(1));
                if (!ticketAssetMap.TryGetValue(number, out var list)) ticketAssetMap[number] = list = [];
                list.Add(assetId);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Number, Title, Description, RequesterId, AssetId, TechnicianId, Priority, Status, Category, CreatedAt, ClosedAt, SlaId, DueDate, DueDateOverridden, SlaOverridden, TeamName, TicketType, Location FROM Tickets;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var number = reader.GetInt32(0);
                var legacyAssetId = NullableGuid(reader, 4);
                List<Guid> assetIds;
                if (ticketAssetMap.TryGetValue(number, out var mapped)) assetIds = mapped;
                else if (legacyAssetId.HasValue) assetIds = new List<Guid> { legacyAssetId.Value };
                else assetIds = new List<Guid>();
                var ticket = new TicketRecord(number, reader.GetString(1), reader.GetString(2), Guid.Parse(reader.GetString(3)),
                    assetIds, NullableGuid(reader, 5), reader.GetString(6), reader.GetString(7), reader.GetString(8), Date(reader, 9),
                    reader.IsDBNull(10) ? null : Date(reader, 10), NullableGuid(reader, 11), reader.IsDBNull(12) ? null : Date(reader, 12), !reader.IsDBNull(13) && reader.GetInt32(13) != 0, !reader.IsDBNull(14) && reader.GetInt32(14) != 0, NullableString(reader, 15), NullableString(reader, 17)) { Type = TicketTypes.Normalize(NullableString(reader, 16)) };
                data.Tickets.Add(ticket);
            }
        }
        foreach (var ticket in data.Tickets) ReadTicketChildren(connection, ticket);
        data.LastTicketNumber = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(Number), 1000) FROM Tickets;"));
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT BrandName, DashboardEyebrow, DashboardTitle, DashboardDescription, PrimaryColor, AccentColor, BackgroundColor, DarkMode FROM BrandingSettings WHERE Id = 1;";
            using var reader = command.ExecuteReader();
            if (reader.Read()) data.Branding = new() { BrandName = reader.GetString(0), DashboardEyebrow = reader.GetString(1), DashboardTitle = reader.GetString(2), DashboardDescription = reader.GetString(3), PrimaryColor = reader.GetString(4), AccentColor = reader.GetString(5), BackgroundColor = reader.GetString(6), DarkMode = reader.GetInt32(7) != 0 };
        }
        return data;
    }

    private static void ReadAttributeScope(SqliteConnection connection, string table, string column, Action<Guid, string> add)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT AttributeId, {column} FROM {table};";
        using var reader = command.ExecuteReader();
        while (reader.Read()) add(Guid.Parse(reader.GetString(0)), reader.GetString(1));
    }

    private static void ReadStrings(SqliteConnection connection, string table, List<string> target)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Name FROM {table} ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) target.Add(reader.GetString(0));
    }

    private static void ReadTicketChildren(SqliteConnection connection, TicketRecord ticket)
    {
        using var comments = connection.CreateCommand();
        comments.CommandText = "SELECT Text, CreatedAt, IsInternal, Actor, ActorId FROM TicketComments WHERE TicketNumber = $number ORDER BY Id;";
        comments.Parameters.AddWithValue("$number", ticket.Number);
        using var commentReader = comments.ExecuteReader();
        while (commentReader.Read()) ticket.Comments.Add(new(commentReader.GetString(0), Date(commentReader, 1), commentReader.GetInt32(2) != 0) { By = ReadActor(commentReader, 3, 4) });
        using var activities = connection.CreateCommand();
        activities.CommandText = "SELECT Action, Details, CreatedAt, Actor, ActorId FROM TicketActivities WHERE TicketNumber = $number ORDER BY Id;";
        activities.Parameters.AddWithValue("$number", ticket.Number);
        using var activityReader = activities.ExecuteReader();
        while (activityReader.Read()) ticket.History.Add(new(activityReader.GetString(0), activityReader.GetString(1), Date(activityReader, 2)) { By = ReadActor(activityReader, 3, 4) });
    }

    private static void ReadAssetChildren(SqliteConnection connection, AssetRecord asset)
    {
        using var comments = connection.CreateCommand();
        comments.CommandText = "SELECT Text, CreatedAt, Actor, ActorId FROM AssetComments WHERE AssetId = $id ORDER BY Id;";
        comments.Parameters.AddWithValue("$id", asset.Id.ToString());
        using var commentReader = comments.ExecuteReader();
        while (commentReader.Read()) asset.Comments.Add(new(commentReader.GetString(0), Date(commentReader, 1)) { By = ReadActor(commentReader, 2, 3) });
        using var activities = connection.CreateCommand();
        activities.CommandText = "SELECT Action, Details, CreatedAt, Actor, ActorId FROM AssetActivities WHERE AssetId = $id ORDER BY Id;";
        activities.Parameters.AddWithValue("$id", asset.Id.ToString());
        using var activityReader = activities.ExecuteReader();
        while (activityReader.Read()) asset.History.Add(new(activityReader.GetString(0), activityReader.GetString(1), Date(activityReader, 2)) { By = ReadActor(activityReader, 3, 4) });
        using var assignments = connection.CreateCommand();
        assignments.CommandText = "SELECT UserId, UserName, StartedAt, EndedAt, DueBack, Reason, KitLoanId FROM AssetAssignments WHERE AssetId = $id ORDER BY Id;";
        assignments.Parameters.AddWithValue("$id", asset.Id.ToString());
        using var assignmentReader = assignments.ExecuteReader();
        while (assignmentReader.Read())
            asset.Assignments.Add(new AssetAssignment(NullableGuid(assignmentReader, 0), assignmentReader.GetString(1),
                assignmentReader.IsDBNull(2) ? null : Date(assignmentReader, 2), assignmentReader.IsDBNull(3) ? null : Date(assignmentReader, 3), NullableDateOnly(assignmentReader, 4))
            {
                Reason = NullableString(assignmentReader, 5),
                KitLoanId = NullableGuid(assignmentReader, 6)
            });
    }

    private static void WriteData(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM TicketTemplateAttributes; DELETE FROM TicketTemplates; DELETE FROM TicketLinks; DELETE FROM TicketAttachments; DELETE FROM TicketActivities; DELETE FROM TicketComments; DELETE FROM TicketAttributeValues; DELETE FROM TicketAssets; DELETE FROM TicketParts; DELETE FROM PartSuppliers; DELETE FROM PartAssetTypes; DELETE FROM PartActivities; DELETE FROM Parts; DELETE FROM Tickets; DELETE FROM TicketAttributeCategories; DELETE FROM TicketAttributeDefinitions; DELETE FROM AssetAssignments; DELETE FROM AssetComments; DELETE FROM AssetActivities; DELETE FROM AssetAttributeValues; DELETE FROM Assets; DELETE FROM Suppliers; DELETE FROM Technicians; DELETE FROM Roles; DELETE FROM Users; DELETE FROM AssetAttributeAssetTypes; DELETE FROM AssetAttributeDefinitions; DELETE FROM SlaPriorities; DELETE FROM SlaCategories; DELETE FROM Slas; DELETE FROM TechnicianTeams; DELETE FROM Departments; DELETE FROM Locations; DELETE FROM AssetTypes; DELETE FROM AssetMakes; DELETE FROM AssetModelMakes; DELETE FROM AssetStatuses; DELETE FROM AssetTypeLifespans; DELETE FROM PartCategories; DELETE FROM PartLocations; DELETE FROM KitLoans; DELETE FROM LoanKitAssets; DELETE FROM LoanKits; DELETE FROM LoanReasons; DELETE FROM AssetModels; DELETE FROM Categories; DELETE FROM Statuses; DELETE FROM StatusDescriptions; DELETE FROM Priorities; DELETE FROM RequireCloseMessagePriorities; DELETE FROM RequireCloseMessageCategories; DELETE FROM DemoRecords; DELETE FROM RolePermissions; DELETE FROM BrandingSettings;";
            command.ExecuteNonQuery();
        }
        foreach (var demo in data.DemoRecords.DistinctBy(x => (x.EntityType, x.EntityKey)))
            Execute(connection, transaction, "INSERT INTO DemoRecords (EntityType, EntityKey) VALUES ($type,$key);", ("$type", demo.EntityType), ("$key", demo.EntityKey));
        InsertStrings(connection, transaction, "TechnicianTeams", data.TechnicianTeams);
        InsertStrings(connection, transaction, "Departments", data.Departments);
        InsertStrings(connection, transaction, "Locations", data.Locations);
        InsertStrings(connection, transaction, "AssetTypes", data.AssetTypes);
        InsertStrings(connection, transaction, "AssetMakes", data.AssetMakes);
        InsertStrings(connection, transaction, "AssetModels", data.AssetModels);
        InsertStrings(connection, transaction, "AssetStatuses", data.AssetStatuses);
        InsertStrings(connection, transaction, "PartCategories", data.PartCategories);
        InsertStrings(connection, transaction, "PartLocations", data.PartLocations);
        InsertStrings(connection, transaction, "LoanReasons", data.LoanReasons);
        SetMetadata(connection, transaction, "LoanRepeatCount", data.LoanRepeatCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "LoanRepeatDays", data.LoanRepeatDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Loan kits are written after the Assets loop below, because LoanKitAssets has a foreign key to Assets.
        foreach (var pair in data.AssetTypeLifespans.Where(x => x.Value > 0 && data.AssetTypes.Contains(x.Key, StringComparer.OrdinalIgnoreCase)))
            Execute(connection, transaction, "INSERT INTO AssetTypeLifespans (AssetType, Years) VALUES ($type,$years);", ("$type", pair.Key), ("$years", pair.Value));
        SetMetadata(connection, transaction, "AssetReviewDays", data.AssetReviewDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "AcademicYearStartMonth", data.AcademicYearStartMonth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "PermissionModelVersion", data.PermissionModelVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "TicketDueSoonHours", data.TicketDueSoonHours.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "PartsDefaultReorderThreshold", data.PartsDefaultReorderThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var pair in data.AssetModelMakes.Where(x => data.AssetModels.Contains(x.Key, StringComparer.OrdinalIgnoreCase) && data.AssetMakes.Contains(x.Value, StringComparer.OrdinalIgnoreCase)))
            Execute(connection, transaction, "INSERT INTO AssetModelMakes (Model, Make) VALUES ($model,$make);", ("$model", pair.Key), ("$make", pair.Value));
        InsertStrings(connection, transaction, "Categories", data.Categories);
        InsertStrings(connection, transaction, "Statuses", data.Statuses);
        foreach (var pair in data.StatusDescriptions.Where(x => data.Statuses.Contains(x.Key, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Value)))
            Execute(connection, transaction, "INSERT INTO StatusDescriptions (Status, Description) VALUES ($status,$description);", ("$status", pair.Key), ("$description", pair.Value));
        InsertStrings(connection, transaction, "Priorities", data.Priorities);
        InsertStrings(connection, transaction, "RequireCloseMessagePriorities", data.RequireCloseMessagePriorities);
        InsertStrings(connection, transaction, "RequireCloseMessageCategories", data.RequireCloseMessageCategories);
        foreach (var sla in data.Slas)
        {
            Execute(connection, transaction, "INSERT INTO Slas (Id, Name, Duration, DurationUnit, Description) VALUES ($id,$name,$duration,$unit,$description);", ("$id", sla.Id.ToString()), ("$name", sla.Name), ("$duration", sla.Duration), ("$unit", NormalizeDurationUnit(sla.DurationUnit)), ("$description", sla.Description));
            foreach (var priority in sla.Priorities.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO SlaPriorities (SlaId, Priority) VALUES ($id,$priority);", ("$id", sla.Id.ToString()), ("$priority", priority));
            foreach (var category in sla.Categories.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO SlaCategories (SlaId, Category) VALUES ($id,$category);", ("$id", sla.Id.ToString()), ("$category", category));
        }
        foreach (var item in data.Users)
            Execute(connection, transaction, "INSERT INTO Users (Id, Name, Email, Department, Location, PasswordHash, IsActive) VALUES ($id,$name,$email,$department,$location,$hash,$active);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$email", item.Email), ("$department", item.Department), ("$location", item.Location), ("$hash", item.PasswordHash), ("$active", item.IsActive ? 1 : 0));
        foreach (var item in data.Technicians)
            // A blank team must be written as NULL, not '' - the column has a foreign key to TechnicianTeams(Name), which only exempts NULL.
            Execute(connection, transaction, "INSERT INTO Technicians (Id, Name, Email, Team, Role, PasswordHash, RequirePasswordChange, IsActive) VALUES ($id,$name,$email,$team,$role,$hash,$requireChange,$active);",
                ("$id", item.Id.ToString()), ("$name", item.Name), ("$email", item.Email), ("$team", string.IsNullOrWhiteSpace(item.Team) ? null : item.Team), ("$role", string.IsNullOrWhiteSpace(item.Role) ? StaffRoles.DefaultRole : item.Role), ("$hash", item.PasswordHash), ("$requireChange", item.RequirePasswordChange ? 1 : 0), ("$active", item.IsActive ? 1 : 0));
        foreach (var item in data.Roles)
        {
            // The nine Allow* columns are NOT NULL DEFAULT 0, so leaving them out retires them cleanly - the same way
            // the SLA single-value columns were retired.
            Execute(connection, transaction, "INSERT INTO Roles (Name, IsProtected) VALUES ($name,$protected);",
                ("$name", item.Name), ("$protected", item.IsProtected ? 1 : 0));
            // One row per ticked box: "Assets:Access", "Assets:Edit", and so on.
            foreach (var module in item.Grants.Where(x => x.Value != ModulePermission.None))
                foreach (var action in ModulePermissions.Split(module.Value))
                    Execute(connection, transaction, "INSERT INTO RolePermissions (RoleName, Permission) VALUES ($role,$permission);",
                        ("$role", item.Name), ("$permission", $"{module.Key}:{action}"));
            foreach (var flag in item.Flags)
                Execute(connection, transaction, "INSERT INTO RolePermissions (RoleName, Permission) VALUES ($role,$permission);",
                    ("$role", item.Name), ("$permission", flag));
        }
        foreach (var item in data.Suppliers)
            Execute(connection, transaction, "INSERT INTO Suppliers (Id, Name, ContactName, Email, Phone, AddressLine1, AddressLine2, City, StateRegion, PostalCode, Country, Website, Notes, CreatedAt) VALUES ($id,$name,$contact,$email,$phone,$a1,$a2,$city,$state,$postal,$country,$website,$notes,$created);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$contact", item.ContactName), ("$email", item.Email), ("$phone", item.Phone), ("$a1", item.AddressLine1), ("$a2", item.AddressLine2), ("$city", item.City), ("$state", item.StateRegion), ("$postal", item.PostalCode), ("$country", item.Country), ("$website", item.Website), ("$notes", item.Notes), ("$created", Iso(item.CreatedAt)));
        foreach (var item in data.Parts)
        {
            Execute(connection, transaction, "INSERT INTO Parts (Id, Name, Sku, Category, QuantityOnHand, CreatedAt, Location, ReorderThreshold) VALUES ($id,$name,$sku,$category,$quantity,$created,$location,$reorder);",
                ("$id", item.Id.ToString()), ("$name", item.Name), ("$sku", item.Sku), ("$category", item.Category), ("$quantity", item.QuantityOnHand), ("$created", Iso(item.CreatedAt)), ("$location", item.Location ?? string.Empty), ("$reorder", item.ReorderThreshold));
            foreach (var supplierId in item.SupplierIds.Distinct())
                if (data.Suppliers.Any(x => x.Id == supplierId))
                    Execute(connection, transaction, "INSERT INTO PartSuppliers (PartId, SupplierId) VALUES ($part,$supplier);", ("$part", item.Id.ToString()), ("$supplier", supplierId.ToString()));
            foreach (var assetType in item.AssetTypes.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO PartAssetTypes (PartId, AssetType) VALUES ($part,$type);", ("$part", item.Id.ToString()), ("$type", assetType));
            foreach (var activity in item.History)
                Execute(connection, transaction, "INSERT INTO PartActivities (PartId, Action, Details, CreatedAt, Actor, ActorId) VALUES ($id,$action,$details,$created,$actor,$actorid);", ("$id", item.Id.ToString()), ("$action", activity.Action), ("$details", activity.Details), ("$created", Iso(activity.CreatedAt)), ("$actor", activity.By?.Name), ("$actorid", activity.By?.Id?.ToString()));
        }
        foreach (var item in data.Assets)
        {
            Execute(connection, transaction, "INSERT INTO Assets (Id, AssetTag, Make, Type, Model, SerialNumber, Location, AssignedUserId, SupplierId, Status, PurchaseDate, PurchasePrice, PurchaseOrder, WarrantyEnd, ReplacementDate, LoanDueDate, QuoteReference, DisposalDate, DisposalMethod, DisposalProceeds) VALUES ($id,$tag,$make,$type,$model,$serial,$location,$user,$supplier,$status,$purchased,$price,$po,$warranty,$replacement,$loan,$quote,$disposed,$method,$proceeds);", ("$id", item.Id.ToString()), ("$tag", item.AssetTag), ("$make", item.Make), ("$type", item.Type), ("$model", item.Model), ("$serial", item.SerialNumber), ("$location", item.Location), ("$user", item.AssignedUserId?.ToString()), ("$supplier", item.SupplierId?.ToString()),
                ("$status", string.IsNullOrWhiteSpace(item.Status) ? "In use" : item.Status), ("$purchased", IsoDay(item.PurchaseDate)), ("$price", item.PurchasePrice?.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("$po", item.PurchaseOrder ?? string.Empty), ("$warranty", IsoDay(item.WarrantyEnd)), ("$replacement", IsoDay(item.ReplacementDate)), ("$loan", IsoDay(item.LoanDueDate)),
                ("$quote", item.QuoteReference ?? string.Empty), ("$disposed", IsoDay(item.DisposalDate)), ("$method", item.DisposalMethod ?? string.Empty), ("$proceeds", item.DisposalProceeds?.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            foreach (var assignment in item.Assignments)
                Execute(connection, transaction, "INSERT INTO AssetAssignments (AssetId, UserId, UserName, StartedAt, EndedAt, DueBack, Reason, KitLoanId) VALUES ($id,$user,$name,$started,$ended,$due,$reason,$kitloan);", ("$id", item.Id.ToString()), ("$user", assignment.UserId?.ToString()), ("$name", assignment.UserName), ("$started", assignment.StartedAt.HasValue ? Iso(assignment.StartedAt.Value) : null), ("$ended", assignment.EndedAt.HasValue ? Iso(assignment.EndedAt.Value) : null), ("$due", IsoDay(assignment.DueBack)), ("$reason", assignment.Reason), ("$kitloan", assignment.KitLoanId?.ToString()));
            foreach (var comment in item.Comments)
                Execute(connection, transaction, "INSERT INTO AssetComments (AssetId, Text, CreatedAt, Actor, ActorId) VALUES ($id,$text,$created,$actor,$actorid);", ("$id", item.Id.ToString()), ("$text", comment.Text), ("$created", Iso(comment.CreatedAt)), ("$actor", comment.By?.Name), ("$actorid", comment.By?.Id?.ToString()));
            foreach (var activity in item.History)
                Execute(connection, transaction, "INSERT INTO AssetActivities (AssetId, Action, Details, CreatedAt, Actor, ActorId) VALUES ($id,$action,$details,$created,$actor,$actorid);", ("$id", item.Id.ToString()), ("$action", activity.Action), ("$details", activity.Details), ("$created", Iso(activity.CreatedAt)), ("$actor", activity.By?.Name), ("$actorid", activity.By?.Id?.ToString()));
        }
        // After Assets: LoanKitAssets references Assets(Id), and KitLoans references LoanKits(Id).
        foreach (var kit in data.LoanKits)
        {
            Execute(connection, transaction, "INSERT INTO LoanKits (Id, Name, Notes, CreatedAt, IsRetired) VALUES ($id,$name,$notes,$created,$retired);",
                ("$id", kit.Id.ToString()), ("$name", kit.Name), ("$notes", kit.Notes ?? string.Empty), ("$created", Iso(kit.CreatedAt)), ("$retired", kit.IsRetired ? 1 : 0));
            foreach (var assetId in kit.AssetIds.Distinct())
                if (data.Assets.Any(x => x.Id == assetId))
                    Execute(connection, transaction, "INSERT INTO LoanKitAssets (KitId, AssetId) VALUES ($kit,$asset);", ("$kit", kit.Id.ToString()), ("$asset", assetId.ToString()));
        }
        foreach (var loan in data.KitLoans.Where(x => data.LoanKits.Any(k => k.Id == x.KitId)))
            Execute(connection, transaction, "INSERT INTO KitLoans (Id, KitId, BorrowerUserId, BorrowerName, Reason, IssuedAt, DueBack, ReturnedAt, IssuedBy, Notes) VALUES ($id,$kit,$user,$name,$reason,$issued,$due,$returned,$by,$notes);",
                ("$id", loan.Id.ToString()), ("$kit", loan.KitId.ToString()), ("$user", loan.BorrowerUserId?.ToString()), ("$name", loan.BorrowerName),
                ("$reason", loan.Reason), ("$issued", Iso(loan.IssuedAt)), ("$due", IsoDay(loan.DueBack)),
                ("$returned", loan.ReturnedAt.HasValue ? Iso(loan.ReturnedAt.Value) : null), ("$by", loan.IssuedBy ?? string.Empty), ("$notes", loan.Notes ?? string.Empty));
        foreach (var item in data.AssetAttributeDefinitions)
        {
            Execute(connection, transaction, "INSERT INTO AssetAttributeDefinitions (Id, Name, FieldType, Choices) VALUES ($id,$name,$fieldType,$choices);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$fieldType", NormalizeAttributeType(item.FieldType)), ("$choices", NormalizeChoices(item.Choices)));
            foreach (var assetType in item.AssetTypes.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO AssetAttributeAssetTypes (AttributeId, AssetType) VALUES ($id,$type);", ("$id", item.Id.ToString()), ("$type", assetType));
        }
        foreach (var item in data.AssetAttributeValues)
            if (data.Assets.Any(x => x.Id == item.AssetId) && data.AssetAttributeDefinitions.Any(x => x.Id == item.AttributeDefinitionId))
                Execute(connection, transaction, "INSERT INTO AssetAttributeValues (AssetId, AttributeDefinitionId, Value) VALUES ($asset,$definition,$value);", ("$asset", item.AssetId.ToString()), ("$definition", item.AttributeDefinitionId.ToString()), ("$value", item.Value));
        foreach (var item in data.TicketAttributeDefinitions)
        {
            Execute(connection, transaction, "INSERT INTO TicketAttributeDefinitions (Id, Name, FieldType, Choices) VALUES ($id,$name,$fieldType,$choices);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$fieldType", NormalizeAttributeType(item.FieldType)), ("$choices", NormalizeChoices(item.Choices)));
            foreach (var category in item.Categories.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO TicketAttributeCategories (AttributeId, Category) VALUES ($id,$category);", ("$id", item.Id.ToString()), ("$category", category));
        }
        foreach (var item in data.Tickets)
        {
            Execute(connection, transaction, "INSERT INTO Tickets (Number, Title, Description, RequesterId, TechnicianId, Priority, Status, Category, CreatedAt, ClosedAt, SlaId, DueDate, DueDateOverridden, SlaOverridden, TeamName, TicketType, Location) VALUES ($number,$title,$description,$requester,$technician,$priority,$status,$category,$created,$closed,$sla,$due,$overridden,$slaoverridden,$team,$type,$location);",
                ("$number", item.Number), ("$title", item.Title), ("$description", item.Description), ("$requester", item.RequesterId.ToString()), ("$technician", item.TechnicianId?.ToString()), ("$priority", item.Priority), ("$status", item.Status), ("$category", item.Category), ("$created", Iso(item.CreatedAt)), ("$closed", item.ClosedAt.HasValue ? Iso(item.ClosedAt.Value) : null), ("$sla", item.SlaId?.ToString()), ("$due", item.DueDate.HasValue ? Iso(item.DueDate.Value) : null), ("$overridden", item.DueDateOverridden ? 1 : 0), ("$slaoverridden", item.SlaOverridden ? 1 : 0), ("$team", item.TeamName), ("$type", TicketTypes.Normalize(item.Type)), ("$location", item.Location));
            foreach (var assetId in item.AssetIds.Distinct())
                if (data.Assets.Any(x => x.Id == assetId))
                    Execute(connection, transaction, "INSERT INTO TicketAssets (TicketNumber, AssetId) VALUES ($number,$asset);", ("$number", item.Number), ("$asset", assetId.ToString()));
            foreach (var part in data.TicketParts.Where(x => x.TicketNumber == item.Number))
                if (data.Parts.Any(x => x.Id == part.PartId))
                    Execute(connection, transaction, "INSERT INTO TicketParts (TicketNumber, PartId, Quantity) VALUES ($number,$part,$quantity);", ("$number", item.Number), ("$part", part.PartId.ToString()), ("$quantity", part.Quantity));
            foreach (var value in data.TicketAttributeValues.Where(x => x.TicketNumber == item.Number))
                if (data.TicketAttributeDefinitions.Any(x => x.Id == value.AttributeDefinitionId))
                    Execute(connection, transaction, "INSERT INTO TicketAttributeValues (TicketNumber, AttributeDefinitionId, Value) VALUES ($number,$definition,$value);", ("$number", value.TicketNumber), ("$definition", value.AttributeDefinitionId.ToString()), ("$value", value.Value));
            foreach (var comment in item.Comments)
                Execute(connection, transaction, "INSERT INTO TicketComments (TicketNumber, Text, CreatedAt, IsInternal, Actor, ActorId) VALUES ($number,$text,$created,$internal,$actor,$actorid);", ("$number", item.Number), ("$text", comment.Text), ("$created", Iso(comment.CreatedAt)), ("$internal", comment.IsInternal ? 1 : 0), ("$actor", comment.By?.Name), ("$actorid", comment.By?.Id?.ToString()));
            foreach (var attachment in data.TicketAttachments.Where(x => x.TicketNumber == item.Number))
                Execute(connection, transaction, "INSERT INTO TicketAttachments (Id, TicketNumber, FileName, ContentType, Size, UploadedAt) VALUES ($id,$number,$name,$type,$size,$uploaded);", ("$id", attachment.Id.ToString()), ("$number", item.Number), ("$name", attachment.FileName), ("$type", attachment.ContentType), ("$size", attachment.Size), ("$uploaded", Iso(attachment.UploadedAt)));
            foreach (var activity in item.History)
                Execute(connection, transaction, "INSERT INTO TicketActivities (TicketNumber, Action, Details, CreatedAt, Actor, ActorId) VALUES ($number,$action,$details,$created,$actor,$actorid);", ("$number", item.Number), ("$action", activity.Action), ("$details", activity.Details), ("$created", Iso(activity.CreatedAt)), ("$actor", activity.By?.Name), ("$actorid", activity.By?.Id?.ToString()));
        }
        foreach (var template in data.TicketTemplates)
        {
            Execute(connection, transaction, "INSERT INTO TicketTemplates (Id, Name, TicketType, Title, Description, Category, Priority, SlaId) VALUES ($id,$name,$type,$title,$description,$category,$priority,$sla);",
                ("$id", template.Id.ToString()), ("$name", template.Name), ("$type", TicketTypes.Normalize(template.Type)), ("$title", template.Title), ("$description", template.Description), ("$category", template.Category), ("$priority", template.Priority), ("$sla", template.SlaId?.ToString()));
            foreach (var pair in template.AttributeValues.Where(x => !string.IsNullOrEmpty(x.Value) && data.TicketAttributeDefinitions.Any(d => d.Id == x.Key)))
                Execute(connection, transaction, "INSERT INTO TicketTemplateAttributes (TemplateId, AttributeDefinitionId, Value) VALUES ($template,$definition,$value);", ("$template", template.Id.ToString()), ("$definition", pair.Key.ToString()), ("$value", pair.Value));
        }
        var ticketNumbers = data.Tickets.Select(x => x.Number).ToHashSet();
        foreach (var link in data.TicketLinks.Where(x => ticketNumbers.Contains(x.TicketNumber) && ticketNumbers.Contains(x.LinkedNumber)))
            Execute(connection, transaction, "INSERT INTO TicketLinks (TicketNumber, LinkedNumber, Kind) VALUES ($a,$b,$kind);", ("$a", link.TicketNumber), ("$b", link.LinkedNumber), ("$kind", link.Kind));
        var branding = data.Branding ?? new BrandingSettings();
        Execute(connection, transaction, "INSERT INTO BrandingSettings (Id, BrandName, DashboardEyebrow, DashboardTitle, DashboardDescription, PrimaryColor, AccentColor, BackgroundColor, DarkMode) VALUES (1,$name,$eyebrow,$title,$description,$primary,$accent,$background,$dark);",
            ("$name", branding.BrandName), ("$eyebrow", branding.DashboardEyebrow), ("$title", branding.DashboardTitle), ("$description", branding.DashboardDescription), ("$primary", branding.PrimaryColor), ("$accent", branding.AccentColor), ("$background", branding.BackgroundColor), ("$dark", branding.DarkMode ? 1 : 0));
    }

    private static void InsertStrings(SqliteConnection connection, SqliteTransaction transaction, string table, IEnumerable<string> values)
    {
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, $"INSERT INTO {table} (Name) VALUES ($value);", ("$value", value));
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    // What a brand-new system and a factory reset both start from: the option lists a working system needs, one team so
    // technicians have something to belong to, and a worked demo example (see SeedDemoData).
    // The lists here are seeded rather than put in EnsureFactoryOptions because that runs on every startup: a school
    // that deletes an asset type it does not own should not find it back the next morning.
    private void SeedStarterData()
    {
        // A brand-new set of data is already in the per-module model, so it has to say so. A factory reset replaces
        // _data with a fresh StoreData, whose version starts at 0, and MigrateRolePermissions only runs at startup -
        // so without this the database claimed the old model while holding new-model roles, and the next restart
        // "converted" them from legacy grants that are no longer written, throwing away whatever had been set up.
        _data.PermissionModelVersion = PermissionModelVersion;
        EnsureFactoryOptions();
        EnsureOptions(_data.TechnicianTeams, ["IT Support"]);
        EnsureOptions(_data.AssetTypes, ["Laptop", "Desktop", "Tablet", "Monitor", "Printer", "Projector",
            "Interactive display", "Phone", "Server", "Networking", "Peripheral", "Other"]);
        SeedDemoData();
        SaveBaseline();
    }

    // Marks every seeded record so a school can find the lot by searching "demo" and delete it once they are ready to
    // start for real. Option-list values (departments, makes, part categories) are deliberately NOT marked: those are
    // meant to be kept and edited, while these records are meant to be thrown away.
    private const string DemoSuffix = " (demo)";

    // One worked example of everything, so a new system can be shown to someone rather than described. Records point at
    // each other on purpose - the ticket has the asset, the asset has the requester and supplier, the part is on the
    // ticket - because a pile of disconnected rows demonstrates nothing.
    // Not seeded: attachments (they need a real file on disk) and ticket links (they need a second ticket).
    private void SeedDemoData()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        void Remember(string type, string key) => _data.DemoRecords.Add(new DemoRecord(type, key));

        // Option-list values the demo records need. Realistic and worth keeping, unlike the records themselves.
        EnsureOptions(_data.Departments, ["Teaching"]);
        EnsureOptions(_data.Locations, ["Main Building"]);
        EnsureOptions(_data.AssetMakes, ["Dell"]);
        EnsureOptions(_data.AssetModels, ["Latitude 5440"]);
        _data.AssetModelMakes["Latitude 5440"] = "Dell";
        _data.AssetTypeLifespans["Laptop"] = 4;
        EnsureOptions(_data.PartCategories, ["Consumables"]);
        EnsureOptions(_data.PartLocations, ["IT Store"]);
        _data.StatusDescriptions["On Hold"] = "Waiting on the requester, a part, or a third party.";

        var supplier = new SupplierRecord(Guid.NewGuid(), "Demo Supplies Ltd", "Pat Brennan", "sales@demo-supplies.test",
            "01234 567890", "Unit 4, Trade Park", "", "Exampleton", "", "EX1 2AB", "United Kingdom",
            "https://demo-supplies.test", "Seeded as a demo record - safe to delete.", now.AddDays(-120));
        _data.Suppliers.Add(supplier);
        Remember("Supplier", supplier.Id.ToString());

        var requester = new UserRecord(Guid.NewGuid(), "Sam Taylor" + DemoSuffix, "sam.taylor@demo.local", "Teaching", "Main Building");
        _data.Users.Add(requester);
        Remember("User", requester.Id.ToString());

        // No password, so the demo technician cannot be signed in as - it exists to be assigned work, not to be a
        // second way into the system. The bootstrap administrator stays the only account with credentials.
        var technician = new TechnicianRecord(Guid.NewGuid(), "Jo Bennett" + DemoSuffix, "jo.bennett@demo.local", "IT Support", StaffRoles.DefaultRole);
        _data.Technicians.Add(technician);
        Remember("Technician", technician.Id.ToString());
        var actor = new Actor(technician.Id, technician.Name);

        var sla = new SlaDefinition(Guid.NewGuid(), "Demo SLA - respond in 8 hours", 8, "hours", "Seeded as a demo record - safe to delete.")
        {
            Categories = ["Hardware"]
        };
        _data.Slas.Add(sla);
        Remember("Sla", sla.Id.ToString());

        var ticketAttribute = new TicketAttributeDefinition(Guid.NewGuid(), "Room number" + DemoSuffix, "single-line") { Categories = ["Hardware"] };
        _data.TicketAttributeDefinitions.Add(ticketAttribute);
        Remember("TicketAttribute", ticketAttribute.Id.ToString());
        var assetAttribute = new AssetAttributeDefinition(Guid.NewGuid(), "Warranty provider" + DemoSuffix, "dropdown", "Manufacturer, Supplier, None") { AssetTypes = ["Laptop"] };
        _data.AssetAttributeDefinitions.Add(assetAttribute);
        Remember("AssetAttribute", assetAttribute.Id.ToString());

        var asset = new AssetRecord(Guid.NewGuid(), "DEMO-LT-001", "Dell", "Latitude 5440", "Laptop", "DEMO-SN-0001", "Main Building", requester.Id, supplier.Id)
        {
            Status = "In use",
            PurchaseDate = today.AddYears(-2),
            PurchasePrice = 649.00m,
            PurchaseOrder = "PO-DEMO-001",
            WarrantyEnd = today.AddDays(45),
            Assignments = { new AssetAssignment(requester.Id, requester.Name, now.AddDays(-400), null, null) },
            History = { new AssetActivity("Assigned user changed", $"Assigned to {requester.Name}.", now.AddDays(-400)) { By = actor } },
            Comments = { new AssetComment("Battery health checked at the last service.", now.AddDays(-30)) { By = actor } }
        };
        _data.Assets.Add(asset);
        Remember("Asset", asset.Id.ToString());
        _data.AssetAttributeValues.Add(new AssetAttributeValue(asset.Id, assetAttribute.Id, "Supplier"));

        // A second asset so the demo loan kit has something in it. A kit with no contents would show the feature
        // without showing what it is for - and an empty child collection is exactly what hid an earlier foreign key bug.
        // Out with the demo kit below, so its status and holder match the open loan - the same state IssueKit produces.
        // The assignment carries the kit loan's id, exactly as IssueKit stamps it, so the loan report counts this as one
        // kit loan rather than a kit loan plus a separate loan of the laptop inside it.
        var demoKitLoanId = Guid.NewGuid();
        var loanAsset = new AssetRecord(Guid.NewGuid(), "DEMO-LT-002", "Dell", "Latitude 5440", "Laptop", "DEMO-SN-0002", "Main Building", requester.Id, supplier.Id)
        {
            Status = OnLoanStatus,
            LoanDueDate = today.AddDays(4),
            Assignments = { new AssetAssignment(requester.Id, requester.Name, now.AddDays(-3), null, today.AddDays(4)) { Reason = "Forgot own device", KitLoanId = demoKitLoanId } },
            History = { new AssetActivity("Assigned user changed", $"Assigned to {requester.Name}.", now.AddDays(-3)) { By = actor } },
            PurchaseDate = today.AddYears(-1),
            PurchasePrice = 649.00m,
            PurchaseOrder = "PO-DEMO-001",
            WarrantyEnd = today.AddYears(2)
        };
        _data.Assets.Add(loanAsset);
        Remember("Asset", loanAsset.Id.ToString());

        // Five received, one fitted to the demo ticket below, so the stock history and the ticket agree with each other.
        var part = new PartRecord(Guid.NewGuid(), "Demo laptop charger", "DEMO-CHG-65W", "Consumables", 4, now.AddDays(-60))
        {
            Location = "IT Store",
            ReorderThreshold = 2,
            SupplierIds = [supplier.Id],
            AssetTypes = ["Laptop"],
            History = { new PartActivity("Stock adjusted", "0 -> 5 (Opening stock)", now.AddDays(-60)) { By = actor } }
        };
        _data.Parts.Add(part);
        Remember("Part", part.Id.ToString());

        var createdAt = now.AddDays(-2);
        var number = ++_data.LastTicketNumber;
        var ticket = new TicketRecord(number, "Demo ticket - laptop will not charge",
            "Sam reports the laptop runs from the battery but will not charge from the mains.",
            requester.Id, [asset.Id], technician.Id, "High", "Open", "Hardware", createdAt, null,
            sla.Id, CalculateDueDate(sla.Id, createdAt), false, false, "IT Support", "Main Building")
        {
            Type = TicketTypes.Incident,
            History =
            {
                new TicketActivity("Ticket created", "The ticket was created.", createdAt) { By = actor },
                new TicketActivity("Part assigned", $"Assigned 1x {part.Name}.", createdAt.AddHours(3)) { By = actor }
            },
            Comments = { new TicketComment("Swapped the charger, monitoring before closing.", createdAt.AddHours(3)) { By = actor } }
        };
        _data.Tickets.Add(ticket);
        Remember("Ticket", number.ToString());
        _data.TicketParts.Add(new TicketPartAssignment(number, part.Id, 1));
        _data.TicketAttributeValues.Add(new TicketAttributeValue(number, ticketAttribute.Id, "12"));

        var template = new TicketTemplate(Guid.NewGuid(), "Demo template - new starter laptop", TicketTypes.Request,
            "Laptop for new starter", "Please prepare and hand over a laptop for a new member of staff.", "Hardware", "Normal", sla.Id)
        {
            AttributeValues = { [ticketAttribute.Id] = "" }
        };
        _data.TicketTemplates.Add(template);
        Remember("TicketTemplate", template.Id.ToString());

        var kit = new LoanKit(Guid.NewGuid(), "Demo loan kit A", "Laptop and charger for anyone without a device.", now.AddDays(-90))
        {
            AssetIds = [loanAsset.Id]
        };
        _data.LoanKits.Add(kit);
        Remember("LoanKit", kit.Id.ToString());
        // Currently out and due back shortly, so the loan list and the repeat-borrower report both have something to show.
        _data.KitLoans.Add(new KitLoan(demoKitLoanId, kit.Id, requester.Id, requester.Name, "Forgot own device",
            now.AddDays(-3), today.AddDays(4), null, technician.Name, "Seeded as a demo record - safe to delete."));
    }

    public sealed class StoreData
    {
        public List<UserRecord> Users { get; set; } = [];
        public List<TechnicianRecord> Technicians { get; set; } = [];
        public List<RoleRecord> Roles { get; set; } = [];
        // 0 = the original nine on/off permissions, 2 = one stacked level per module, 3 = independent ticks.
        // Read from Metadata; see MigrateRolePermissions.
        public int PermissionModelVersion { get; set; }
        public List<string> TechnicianTeams { get; set; } = [];
        public List<string> Departments { get; set; } = [];
        public List<string> Locations { get; set; } = [];
        public List<string> AssetTypes { get; set; } = [];
        public List<string> AssetMakes { get; set; } = [];
        public List<string> AssetModels { get; set; } = [];
        public Dictionary<string, string> AssetModelMakes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> AssetStatuses { get; set; } = [];
        public List<string> PartCategories { get; set; } = [];
        public List<string> PartLocations { get; set; } = [];
        public List<string> LoanReasons { get; set; } = [];
        public List<LoanKit> LoanKits { get; set; } = [];
        public List<KitLoan> KitLoans { get; set; } = [];
        // A borrower with this many loans inside this many days is flagged on the loan report.
        public int LoanRepeatCount { get; set; } = 3;
        public int LoanRepeatDays { get; set; } = 30;
        // Expected life in years per asset type, used to work out replacement dates.
        public Dictionary<string, int> AssetTypeLifespans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        // Warranty ends and replacement dates inside this many days go on the overview review list.
        public int AssetReviewDays { get; set; } = 60;
        // The month the academic year starts in, for the finance and audit report. September for most schools.
        public int AcademicYearStartMonth { get; set; } = AcademicYear.DefaultStartMonth;
        // Open tickets due within this many hours count as "due soon" on the ticket list.
        public int TicketDueSoonHours { get; set; } = 24;
        // Default minimum stock level for a part with no ReorderThreshold of its own.
        public int PartsDefaultReorderThreshold { get; set; } = 5;
        public List<string> Categories { get; set; } = [];
        public List<string> Statuses { get; set; } = [];
        public Dictionary<string, string> StatusDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ClosureCommentPriorities { get; set; } = [];
        public List<string> ClosureCommentCategories { get; set; } = [];
        public List<string> Priorities { get; set; } = [];
        public List<string> RequireCloseMessagePriorities { get; set; } = [];
        public List<string> RequireCloseMessageCategories { get; set; } = [];
        public List<AssetRecord> Assets { get; set; } = [];
        public List<SupplierRecord> Suppliers { get; set; } = [];
        public List<PartRecord> Parts { get; set; } = [];
        public List<TicketPartAssignment> TicketParts { get; set; } = [];
        public List<TicketTemplate> TicketTemplates { get; set; } = [];
        public List<TicketAttachment> TicketAttachments { get; set; } = [];
        public List<TicketLink> TicketLinks { get; set; } = [];
        public List<AssetAttributeDefinition> AssetAttributeDefinitions { get; set; } = [];
        public List<AssetAttributeValue> AssetAttributeValues { get; set; } = [];
        public List<SlaDefinition> Slas { get; set; } = [];
        public List<TicketAttributeDefinition> TicketAttributeDefinitions { get; set; } = [];
        public List<TicketAttributeValue> TicketAttributeValues { get; set; } = [];
        public List<TicketRecord> Tickets { get; set; } = [];
        // What SeedDemoData created, so the "Go live" button knows exactly what to remove. Empty on a system that has
        // already gone live, or one upgraded from before demo data existed.
        public List<DemoRecord> DemoRecords { get; set; } = [];
        public int LastTicketNumber { get; set; } = 1000;
        public BrandingSettings Branding { get; set; } = new();
    }

    private List<string> GetOptions(string kind) => kind switch
    {
        "Category" => _data.Categories,
        "Status" => _data.Statuses,
        "Priority" => _data.Priorities,
        _ => throw new ArgumentException("Unknown ticket option.", nameof(kind))
    };
    private static string NormalizeManagedOptionKind(string kind) => (kind ?? string.Empty).Trim() switch
    {
        "Teams" => "Team",
        "Departments" => "Department",
        "Locations" => "Location",
        "AssetTypes" => "Asset type",
        "AssetMakes" => "Asset make",
        "AssetModels" => "Asset model",
        "AssetStatuses" => "Asset status",
        "PartCategories" => "Part category",
        "PartLocations" => "Part location",
        "LoanReasons" => "Loan reason",
        _ => (kind ?? string.Empty).Trim()
    };

    private List<string> GetManagedOptions(string kind) => NormalizeManagedOptionKind(kind) switch
    {
        "Team" => _data.TechnicianTeams,
        "Department" => _data.Departments,
        "Location" => _data.Locations,
        "Asset type" => _data.AssetTypes,
        "Asset make" => _data.AssetMakes,
        "Asset model" => _data.AssetModels,
        "Asset status" => _data.AssetStatuses,
        "Part category" => _data.PartCategories,
        "Part location" => _data.PartLocations,
        "Loan reason" => _data.LoanReasons,
        _ => []
    };
    private static bool IsManagedOptionKind(string kind) =>
        NormalizeManagedOptionKind(kind) is "Team" or "Department" or "Location" or "Asset type" or "Asset make" or "Asset model" or "Asset status" or "Part category" or "Part location" or "Loan reason";
    private static bool IsTicketOptionKind(string kind) =>
        kind is "Category" or "Status" or "Priority";

    public static string NormalizeAttributeType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dropdown" => "dropdown",
        "checkbox" => "checkbox",
        "multi-line" or "multiline" => "multi-line",
        _ => "single-line"
    };

    public static bool IsAttributeType(string? value) =>
        value is "dropdown" or "checkbox" or "single-line" or "multi-line";

    public static string NormalizeChoices(string? value) =>
        string.Join(",", (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal));

    public static IReadOnlyList<string> GetChoices(AssetAttributeDefinition definition) =>
        NormalizeChoices(definition.Choices).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static IReadOnlyList<string> GetChoices(TicketAttributeDefinition definition) =>
        NormalizeChoices(definition.Choices).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string NormalizeDurationUnit(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "days", StringComparison.OrdinalIgnoreCase)) return "days";
        if (string.Equals(trimmed, "minutes", StringComparison.OrdinalIgnoreCase)) return "minutes";
        return "hours";
    }

    private static List<string> NormalizeScope(IEnumerable<string>? values) =>
        (values ?? []).Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Returns the requested values using the casing of the configured options, or null if any value is not a configured option.
    private static List<string>? ResolveScope(IEnumerable<string>? requested, List<string> options)
    {
        var resolved = new List<string>();
        foreach (var value in NormalizeScope(requested))
        {
            var match = options.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            resolved.Add(match);
        }
        return resolved;
    }

    // An empty scope means "applies to everything", so it overlaps with any other scope.
    private static bool ScopesOverlap(List<string> first, List<string> second) =>
        first.Count == 0 || second.Count == 0 || first.Intersect(second, StringComparer.OrdinalIgnoreCase).Any();

    private static List<string> RenameInScope(List<string> scope, string oldValue, string newValue) =>
        scope.Select(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase) ? newValue : x).ToList();

    private static string DuplicateAttributeMessage(List<string> scope, string noun) =>
        scope.Count == 0 ? "That attribute name is already in use." : $"That attribute already exists for one of the selected {noun}.";

    private static void EnsureOptions(List<string> options, IEnumerable<string> defaults)
    {
        foreach (var value in defaults)
        {
            if (!options.Contains(value, StringComparer.OrdinalIgnoreCase))
                options.Add(value);
        }
    }
}
