using EduHelpdesk.Models;

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
    private readonly PortalIdentity? _portalIdentity;

    public HelpdeskStore(IHostEnvironment environment, IHttpContextAccessor? httpContext = null, PortalIdentity? portalIdentity = null, DataLocation? location = null)
    {
        _httpContext = httpContext;
        _portalIdentity = portalIdentity;
        Location = location;
        _webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
        var folder = location?.Folder ?? Path.Combine(environment.ContentRootPath, "App_Data");
        _path = Path.Combine(folder, "helpdesk.db");
        _legacyPath = Path.Combine(folder, "helpdesk.json");
        _templatePath = Path.Combine(folder, "print-template.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _data = Load();
        _audit = LoadAudit();
        Prepare();
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

    // Where the data lives (see DataLocation); null only when the store is built outside the app.
    public DataLocation? Location { get; }
    private readonly string _webRoot;
    public string DataFolder => Path.GetDirectoryName(_path)!;

    // Everything done to freshly loaded data before it is used: tidying values older versions stored loosely, and
    // putting back what the code relies on. Nothing here saves, so it can run again on its own after a failed save
    // reloads the database (see Persist).
    private void Prepare()
    {
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
        UpgradeDefaultBranding(_data.Branding);
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
        EnsureSystemOptions();
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
        _data.SchoolDays = (_data.SchoolDays ?? []).Distinct().ToList();
        if (_data.SchoolDays.Count == 0) _data.SchoolDays = [.. SlaClock.DefaultSchoolDays];
        _data.Periods = (_data.Periods ?? []).Where(x => x.End > x.Start).OrderBy(x => x.Start).ToList();
        _data.LoanKits ??= [];
        _data.KitLoans ??= [];
        _data.LoanKits = _data.LoanKits.Select(x => x with { AssetIds = (x.AssetIds ?? []).Where(id => _data.Assets.Any(a => a.Id == id)).Distinct().ToList() }).ToList();
        // Assets that already had a holder before ownership was tracked get an open period with an unknown start.
        foreach (var asset in _data.Assets.Where(x => x.AssignedUserId.HasValue && !x.Assignments.Any(a => a.EndedAt is null)))
            asset.Assignments.Add(new AssetAssignment(asset.AssignedUserId, _data.Users.FirstOrDefault(u => u.Id == asset.AssignedUserId)?.Name ?? "Unknown user", null, null, asset.LoanDueDate));
        EnsureProjectDefaults();
        if (_data.ReopenWindowDays is < 0 or > MaxReopenWindowDays) _data.ReopenWindowDays = DefaultReopenWindowDays;
        _data.SlaPauseStatuses = _data.SlaPauseStatuses.Where(x => _data.Statuses.Contains(x, StringComparer.OrdinalIgnoreCase) && !IsBuiltInStatus(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Every ticket's clock matches its status: after upgrading, restoring a backup, or a failed save reloading.
        ReconcileSlaPauses(_ => true);
        // Everyone inactive has a leaver date, and nobody active does (HelpdeskStore.Lifecycle).
        ReconcileLeaverDates();
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
    public void UpdateBranding(BrandingSettings item) { lock (_sync) { _data.Branding = item; Save(); } }

    // The first dashboard wording said the helpdesk had no staff portal, which stopped being true when /Portal was
    // built, and it used the strapline as the title. Every save writes the branding row, so databases created before
    // the defaults changed hold the old text even though nobody chose it - this swaps it for the current defaults.
    // Only text that is still exactly the old default is touched: anything a school has typed for itself is left alone.
    private const string OldDefaultEyebrow = "EDUHELPDESK / TECHNICIAN WORKSPACE";
    private const string OldDefaultTitle = "Keep every school device moving.";
    private const string OldDefaultDescription = "Log jobs, link them to assets, and keep a complete repair history without exposing a staff-facing portal.";
    private static void UpgradeDefaultBranding(BrandingSettings branding)
    {
        var defaults = new BrandingSettings();
        // The eyebrow and title moved together (the old title became the new eyebrow), so they are only swapped as a
        // pair. Checking the title on its own would undo a school that later chose that strapline as its title.
        if (branding.DashboardEyebrow == OldDefaultEyebrow && branding.DashboardTitle == OldDefaultTitle)
        {
            branding.DashboardEyebrow = defaults.DashboardEyebrow;
            branding.DashboardTitle = defaults.DashboardTitle;
        }
        // Nobody would type the old description back in - it describes a system without a portal.
        if (branding.DashboardDescription == OldDefaultDescription) branding.DashboardDescription = defaults.DashboardDescription;
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
}
