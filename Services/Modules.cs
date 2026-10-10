using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The one definition of what a role can be granted. The role editor, the audit log, the policy registrations and the
// seed roles all project from this list, so none of them can drift apart - the original model kept the permission
// labels in three hand-maintained places and they had already started to diverge.
public static class Modules
{
    public const string Tickets = "Tickets";
    public const string Projects = "Projects";
    public const string Onboarding = "Onboarding";
    public const string Assets = "Assets";
    public const string Kits = "Kits";
    public const string Loans = "Loans";
    public const string Parts = "Parts";
    public const string Suppliers = "Suppliers";
    public const string Contracts = "Contracts";
    public const string Access = "Access";
    public const string Requesters = "Requesters";
    public const string StaffAccounts = "StaffAccounts";
    public const string Roles = "Roles";
    public const string Reports = "Reports";
    public const string Settings = "Settings";
    public const string AuditLog = "AuditLog";

    // Supports is which of the five boxes this module offers. Settings has no records to view, create or delete - it is
    // option lists and a reset button - and the audit log is append-only and read-only, so offering those boxes would
    // be offering something that gates nothing.
    public sealed record Definition(string Key, string Label, string Description, ModulePermission Supports);

    private const ModulePermission Full = ModulePermission.Access | ModulePermission.View | ModulePermission.New
        | ModulePermission.Edit | ModulePermission.Delete;

    public static readonly Definition[] All =
    [
        new(Tickets, "Tickets", "The ticket queues and ticket detail. Delete also covers merging.", Full),
        new(Projects, "Projects", "Purchasing projects raised from the staff portal. Edit covers tidying them up, notes and status; assigning is separate below.", Full),
        new(Onboarding, "Onboarding", "New staff checklists. Technicians can open an onboarding from the ticket list and tick its IT tasks with Tickets permissions alone; this covers the rest.", Full),
        new(Assets, "Assets", "The asset register. New covers the CSV import; Delete covers disposal.", Full),
        new(Kits, "Loan kits", "The kits themselves and what is in them.", Full),
        new(Loans, "Loans", "New issues a device, Edit books it back in.", Full),
        new(Parts, "Parts", "The parts inventory. Edit covers stock adjustments.", Full),
        new(Suppliers, "Suppliers", "The supplier directory.", Full),
        new(Contracts, "Contracts", "The DfE contracts register: what the school pays for, renewal and notice dates. It shows what each contract costs.", Full),
        new(Access, "Access control", "The DfE access control register: who can get into which systems and areas, and the termly review. It names individuals. Edit covers removing access and recording a review; Delete is for access recorded by mistake.", Full),
        new(Requesters, "Requesters", "The staff directory tickets are raised for. Edit includes resetting a portal password.", Full),
        new(StaffAccounts, "Staff accounts", "Technician accounts, their team and their role. Edit includes resetting passwords.", Full),
        new(Roles, "Roles", "Role definitions and what each one grants.", Full),
        new(Reports, "Reports", "Access to the reports area. Which reports are readable is set separately below.", ModulePermission.Access),
        new(Settings, "Settings", "Branding, option lists, imports, factory reset.", ModulePermission.Access | ModulePermission.Edit),
        new(AuditLog, "Audit log", "The record of who changed what. It names individuals.", ModulePermission.Access)
    ];

    public static Definition? Find(string key) => All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    // The boxes a module actually offers, in a fixed order, for the role editor's grid.
    public static IReadOnlyList<ModulePermission> ActionsFor(Definition module) =>
        ModulePermissions.Ordered.Where(x => module.Supports.HasFlag(x)).ToList();

    public static bool Supports(string moduleKey, ModulePermission action) =>
        Find(moduleKey) is { } module && module.Supports.HasFlag(action);

    // Permissions that are not an action on a record: reading one particular report, exporting, and picking whose
    // queue you are looking at. Each is a plain on/off tick.
    public static class Flags
    {
        public const string WorkingAs = "Tickets.WorkingAs";
        public const string AssignProjects = "Projects.Assign";
        public const string ReportAssets = "Reports.Assets";
        public const string ReportTickets = "Reports.Tickets";
        public const string ReportParts = "Reports.Parts";
        public const string ReportLoans = "Reports.Loans";
        public const string ReportFinance = "Reports.Finance";
        public const string ReportProjects = "Reports.Projects";
        public const string ReportOnboarding = "Reports.Onboarding";
        public const string ReportExport = "Reports.Export";
        public const string RawDatabase = "System.RawDatabase";

        public sealed record Definition(string Key, string Label, string Description, string Group);

        public static readonly Definition[] All =
        [
            new(WorkingAs, "Change \"Working as\"", "Pick whose ticket queue to look at, instead of always your own.", "Tickets"),
            // The project lead assigns from the staff portal (the "Project lead" tick in People); this is the helpdesk-side backup.
            new(AssignProjects, "Assign projects", "Reassign a project from the helpdesk when the project lead can't. The lead normally does this from the staff portal.", "Projects"),
            new(ReportAssets, "Asset reports", "The asset register report: review list, fleet age, warranty, problem devices.", "Reports"),
            new(ReportTickets, "Ticket reports", "SLA performance, workload and repeat problems.", "Reports"),
            new(ReportParts, "Parts reports", "Low stock.", "Reports"),
            new(ReportLoans, "Loan reports", "Who is borrowing devices and how often.", "Reports"),
            new(ReportFinance, "Finance and audit report", "Spend, orders and disposals. Shows purchase prices.", "Reports"),
            new(ReportProjects, "Project reports", "Purchasing projects: where they are, technician workload, turnaround, and spend by outcome. Shows quote values.", "Reports"),
            new(ReportOnboarding, "Onboarding report", "New staff onboardings: how many, how long they take to finish, and which tasks run late.", "Reports"),
            new(ReportExport, "Export and print reports", "Download report CSVs and open the print views.", "Reports"),
            // Every table exactly as stored, for looking into a problem. Read-only, and each look is in the audit log.
            // Password hashes, two-step secrets and session tokens show only to Administrators, masked for anyone else.
            new(RawDatabase, "View raw database", "Every table exactly as it is stored, for looking into a problem. Read-only. It shows personal data, so each look is recorded in the audit log; passwords and sign-in secrets stay hidden unless you are an Administrator.", "System")
        ];

        public static Definition? Find(string key) => All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}

public static class ModulePermissions
{
    // None is deliberately absent: it is the absence of a tick, not something you pick. The order is the column order
    // in the role editor and reads as a workflow - reach it, read it, add one, change one, remove one.
    public static readonly ModulePermission[] Ordered =
        [ModulePermission.Access, ModulePermission.View, ModulePermission.New, ModulePermission.Edit, ModulePermission.Delete];

    public static string Label(ModulePermission action) => action switch
    {
        ModulePermission.Access => "Access",
        ModulePermission.View => "View",
        ModulePermission.New => "New",
        ModulePermission.Edit => "Edit",
        ModulePermission.Delete => "Delete",
        _ => "None"
    };

    public static string Describe(ModulePermission action) => action switch
    {
        ModulePermission.Access => "Open the list and see what is there",
        ModulePermission.View => "Open a record and read it",
        ModulePermission.New => "Create records",
        ModulePermission.Edit => "Change existing records",
        ModulePermission.Delete => "Remove records",
        _ => ""
    };

    public static ModulePermission Parse(string? value) =>
        Enum.TryParse<ModulePermission>(value, ignoreCase: true, out var action) && Ordered.Contains(action)
            ? action
            : ModulePermission.None;

    // The ticked boxes, in column order, for anything that lists what a role holds.
    public static IEnumerable<ModulePermission> Split(ModulePermission granted) => Ordered.Where(x => granted.HasFlag(x));

    // Everything a role holds as one line, for audit entries: "Tickets Access, View, Edit; Reports Access; Loan reports".
    public static string Summarise(RoleRecord role)
    {
        var modules = Modules.All
            .Where(x => role.GrantsFor(x.Key) != ModulePermission.None)
            .Select(x => $"{x.Label} {string.Join(", ", Split(role.GrantsFor(x.Key)).Select(Label))}");
        var flags = Modules.Flags.All.Where(x => role.Has(x.Key)).Select(x => x.Label);
        return string.Join("; ", modules.Concat(flags));
    }
}
