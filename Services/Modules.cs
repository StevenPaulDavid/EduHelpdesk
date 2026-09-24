using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The one definition of what a role can be granted. The role editor, the audit log, the policy registrations and the
// seed roles all project from this list, so none of them can drift apart - the previous model kept the permission
// labels in three hand-maintained places and they had already started to diverge.
public static class Modules
{
    public const string Tickets = "Tickets";
    public const string Assets = "Assets";
    public const string Kits = "Kits";
    public const string Loans = "Loans";
    public const string Parts = "Parts";
    public const string Suppliers = "Suppliers";
    public const string Requesters = "Requesters";
    public const string StaffAccounts = "StaffAccounts";
    public const string Roles = "Roles";
    public const string Reports = "Reports";
    public const string Settings = "Settings";
    public const string AuditLog = "AuditLog";

    // Max is the highest level the module supports. Settings and Roles have no record to view or delete, and the audit
    // log is append-only and read-only, so offering those levels would be offering something meaningless.
    public sealed record Definition(string Key, string Label, string Description, PermissionLevel Max);

    public static readonly Definition[] All =
    [
        new(Tickets, "Tickets", "The ticket queues and ticket detail. Delete also covers merging.", PermissionLevel.Delete),
        new(Assets, "Assets", "The asset register. Edit covers loaning, returning and importing; Delete covers disposal.", PermissionLevel.Delete),
        new(Kits, "Loan kits", "The kits themselves and what is in them.", PermissionLevel.Delete),
        new(Loans, "Loans", "Issuing devices and booking them back in.", PermissionLevel.Delete),
        new(Parts, "Parts", "The parts inventory. Edit covers stock adjustments.", PermissionLevel.Delete),
        new(Suppliers, "Suppliers", "The supplier directory.", PermissionLevel.Delete),
        new(Requesters, "Requesters", "The staff directory tickets are raised for. Edit includes resetting a portal password.", PermissionLevel.Delete),
        new(StaffAccounts, "Staff accounts", "Technician accounts, their team and their role. Edit includes resetting passwords.", PermissionLevel.Delete),
        new(Roles, "Roles", "Role definitions and what each one grants. Access to view, Edit to change.", PermissionLevel.Edit),
        new(Reports, "Reports", "Access to the reports area. Which reports are readable is set separately below.", PermissionLevel.Access),
        new(Settings, "Settings", "Branding, option lists, imports, factory reset. Access to view, Edit to change.", PermissionLevel.Edit),
        new(AuditLog, "Audit log", "The record of who changed what. It names individuals.", PermissionLevel.Access)
    ];

    public static Definition? Find(string key) => All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    // The levels a module actually offers, for the role editor's dropdown.
    public static IReadOnlyList<PermissionLevel> LevelsFor(Definition module) =>
        PermissionLevels.Ordered.Where(x => x <= module.Max).ToList();

    // Permissions that are not a level on a record: reading one particular report, exporting, and picking whose queue
    // you are looking at. Each is a plain on/off tick.
    public static class Flags
    {
        public const string WorkingAs = "Tickets.WorkingAs";
        public const string ReportAssets = "Reports.Assets";
        public const string ReportTickets = "Reports.Tickets";
        public const string ReportParts = "Reports.Parts";
        public const string ReportLoans = "Reports.Loans";
        public const string ReportFinance = "Reports.Finance";
        public const string ReportExport = "Reports.Export";

        public sealed record Definition(string Key, string Label, string Description, string Group);

        public static readonly Definition[] All =
        [
            new(WorkingAs, "Change \"Working as\"", "Pick whose ticket queue to look at, instead of always your own.", "Tickets"),
            new(ReportAssets, "Asset reports", "The asset register report: review list, fleet age, warranty, problem devices.", "Reports"),
            new(ReportTickets, "Ticket reports", "SLA performance, workload and repeat problems.", "Reports"),
            new(ReportParts, "Parts reports", "Low stock.", "Reports"),
            new(ReportLoans, "Loan reports", "Who is borrowing devices and how often.", "Reports"),
            new(ReportFinance, "Finance and audit report", "Spend, orders and disposals. Shows purchase prices.", "Reports"),
            new(ReportExport, "Export and print reports", "Download report CSVs and open the print views.", "Reports")
        ];

        public static Definition? Find(string key) => All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}

public static class PermissionLevels
{
    // None is deliberately absent: it is the absence of a grant, not something you pick.
    public static readonly PermissionLevel[] Ordered =
        [PermissionLevel.Access, PermissionLevel.View, PermissionLevel.Edit, PermissionLevel.Delete];

    public static string Label(PermissionLevel level) => level switch
    {
        PermissionLevel.Access => "Access",
        PermissionLevel.View => "View",
        PermissionLevel.Edit => "Edit",
        PermissionLevel.Delete => "Delete",
        _ => "No access"
    };

    public static string Describe(PermissionLevel level) => level switch
    {
        PermissionLevel.Access => "Can open the list, nothing more",
        PermissionLevel.View => "Can open a record and read it",
        PermissionLevel.Edit => "Can change records",
        PermissionLevel.Delete => "Can change and delete records",
        _ => "Hidden from the menu"
    };

    public static PermissionLevel Parse(string? value) =>
        Enum.TryParse<PermissionLevel>(value, ignoreCase: true, out var level) ? level : PermissionLevel.None;
}
