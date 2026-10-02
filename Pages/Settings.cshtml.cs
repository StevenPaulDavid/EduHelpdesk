using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Settings is an index of the settings pages, grouped by what they are about. It used to be one long page holding
// fifteen forms, and since the page itself only needs Settings: Access, a role given Access and not Edit could post
// every one of them - branding, imports, even the factory reset. Every form now lives on a page under /Settings, which
// needs Settings: Edit (Program.cs), and this page has no handlers at all.
public class SettingsModel(HelpdeskStore store, FileLogProvider log) : PageModel
{
    public sealed record Card(string Title, string Page, string Description, string Keywords, string? Badge = null, bool Warn = false,
        bool Open = true, IDictionary<string, string>? Route = null, bool Info = false);
    public sealed record Group(string Title, IReadOnlyList<Card> Cards);

    // Opening a settings page needs Settings: Edit, so a role with only Access sees what is there but no links.
    public bool CanEdit => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public bool CanSeeAudit => store.UserCan(User, Modules.AuditLog, ModulePermission.Access);
    public bool CanSeeRaw => store.UserHasFlag(User, Modules.Flags.RawDatabase);
    public HelpdeskStore.BackupSettings Backups => store.Backups;
    public string? DataSyncedBy => store.Location?.SyncedBy;
    public bool IsHttps => Request.IsHttps;
    public int RecentLockouts => store.CountLockouts(DateTime.UtcNow.AddDays(-7));
    public int ActiveStaff => store.Technicians.Count(x => x.IsActive);
    public int TwoFactorCount => store.Technicians.Count(x => x.IsActive && x.TwoFactor is not null);
    public bool HasDemoData => store.HasDemoData;
    public int RecentErrors { get; private set; }
    public IReadOnlyList<Group> Groups { get; private set; } = [];
    // 1.2.0, from the version New-Release.ps1 builds with.
    public static string AppVersion => typeof(SettingsModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "development";

    private string NotificationSummary()
    {
        var settings = store.NotificationSettings;
        var who = !settings.Enabled ? "Switched off for everyone."
            : settings.Staff && settings.Requesters ? "On for staff and requesters."
            : settings.Staff ? "On for staff only."
            : settings.Requesters ? "On for requesters only."
            : "Switched off for everyone.";
        return $"Pings and pop-ups for new tickets, replies and updates. {who}{(settings.Prompt ? " People who haven't switched them on are reminded." : "")}";
    }

    private string RetentionSummary()
    {
        var rules = store.Retention;
        if (!rules.AnyOn) return "How long old tickets, leavers' details and the audit log are kept. Everything is kept for now.";
        var parts = new List<string>();
        if (rules.TicketMonths > 0) parts.Add($"closed tickets {rules.TicketMonths} months");
        if (rules.LeaverMonths > 0) parts.Add($"leavers {rules.LeaverMonths} months");
        if (rules.AuditMonths > 0) parts.Add($"audit log {rules.AuditMonths} months");
        return "Keeps " + string.Join(", ", parts) + ", then removes them each night.";
    }

    public void OnGet()
    {
        var edit = CanEdit;
        RecentErrors = FileLog.CountSince(log.Folder, DateTime.Now.AddDays(-7));
        Card Page(string title, string page, string description, string keywords, string? badge = null, bool warn = false) =>
            new(title, page, description, keywords, badge, warn, edit);
        Card List(string title, string page, IReadOnlyCollection<string> values, string description, string keywords) =>
            Page(title, page, description, keywords, $"{values.Count}");

        var backups = Backups;
        // Backups beside the data survive a mistake or a bad update, but not the drive failing - worth a flag here too.
        var sameDrive = string.Equals(Path.GetPathRoot(Path.GetFullPath(backups.Folder)), Path.GetPathRoot(Path.GetFullPath(store.DataFolder)), StringComparison.OrdinalIgnoreCase);
        var backupBadge = backups.NeedsAttention(DateTime.UtcNow) ? "Needs attention" : !backups.Enabled ? "Off" : sameDrive ? "Same drive as data" : "Nightly";
        var lastBackup = backups.LastSuccessAt is { } last ? $"Last backup {last.ToLocalTime():ddd d MMM, HH:mm}." : "No backup has been made yet.";

        Groups =
        [
            new("Tickets",
            [
                Page("Ticket queues & closing", "/Settings/TicketRules", "Due-soon window, how long replies reopen closed tickets, which tickets need a closing message, and the print template.",
                    "due soon overdue window hours reopen reply closed days closing message requirement print template word docx",
                    store.HasPrintTemplate ? "Template uploaded" : null),
                List("Statuses", "/Settings/Statuses", store.Statuses, "The steps a ticket moves through, what each means, and which stop the SLA clock.", "status on hold pause sla clock"),
                List("Categories", "/Settings/Categories", store.Categories, "What a ticket is about, for filters, reports and templates.", "category hardware software"),
                List("Priorities", "/Settings/Priorities", store.Priorities, "How urgent a ticket is.", "priority urgent high normal low"),
                List("SLAs", "/Settings/Slas", store.Slas.Select(x => x.Name).ToList(), "How long tickets have, by priority and category.", "sla service level due date target"),
                Page("School day and periods", "/Settings/SchoolDay", "The school week and lesson times that work-day and period SLAs count against.", "school day week periods lessons timetable working hours"),
                List("Service catalogue", "/Settings/ServiceCatalogue", store.ServiceItems.Select(x => $"{x.Category} › {x.Name}").ToList(), "The common problems staff pick from when they report one in the portal.", "service catalogue catalog sub category portal common problems"),
                List("Ticket templates", "/Settings/TicketTemplates", store.TicketTemplates.Select(x => x.Name).ToList(), "Saved starting points for repeat jobs.", "template repeat job"),
                List("Ticket custom attributes", "/Settings/TicketAttributes", store.TicketAttributeDefinitions.Select(x => x.Name).ToList(), "Extra fields on tickets, by category.", "custom fields attributes extra"),
            ]),
            new("Assets & inventory",
            [
                Page("Asset, part & loan rules", "/Settings/InventoryRules", "Asset review window, academic year, parts reorder threshold and repeat-borrowing flag.",
                    "review warranty replacement academic year finance reorder threshold low stock loan repeat borrowing"),
                List("Asset types", "/Settings/AssetTypes", store.AssetTypes, "Kinds of device, and how long each should last.", "type lifespan laptop"),
                List("Asset makes", "/Settings/AssetMakes", store.AssetMakes, "Manufacturers.", "make manufacturer brand"),
                List("Asset models", "/Settings/AssetModels", store.AssetModels, "Models, each linked to its make.", "model"),
                List("Asset statuses", "/Settings/AssetStatuses", store.AssetStatuses, "Where an asset is in its life.", "asset status disposed"),
                List("Custom asset attributes", "/Settings/AssetAttributes", store.AssetAttributeDefinitions.Select(x => x.Name).ToList(), "Extra fields on assets, by type.", "custom fields attributes extra"),
                List("Part categories", "/Settings/PartCategories", store.PartCategories, "How parts are grouped.", "part category"),
                List("Part locations", "/Settings/PartLocations", store.PartLocations, "Where parts are kept.", "part location store cupboard"),
                List("Loan reasons", "/Settings/LoanReasons", store.LoanReasons, "Why a device was borrowed.", "loan reason kit"),
            ]),
            new("People & places",
            [
                List("Teams", "/Settings/Teams", store.TechnicianTeams, "Technician teams that tickets can be assigned to.", "team"),
                List("Departments", "/Settings/Departments", store.Departments, "Requesters' departments.", "department"),
                List("Locations", "/Settings/Locations", store.Locations, "Rooms and areas, for tickets, assets and people.", "location room building site"),
                Page("Imports", "/Settings/Imports", "Requesters, staff accounts and option lists from CSV files.", "import csv upload users technicians lists bulk"),
                Page("Onboarding checklists", "/Settings/Onboarding", "The tasks for each kind of new starter, and when they're due.", "onboarding new starter staff checklist template tasks"),
            ]),
            new("Projects",
            [
                Page("Spending bands", "/Settings/SpendingBands", "Your finance policy's thresholds, shown on every project, and whether project pages show amounts with or without VAT.", "spending bands quotes finance policy threshold vat ex inc excluding including amounts project page"),
                List("Purchasing requirements", "/Settings/PurchasingRequirements", store.PurchasingRequirements, "Tick boxes offered when a project is requested.", "purchasing requirement project request"),
            ]),
            new("System",
            [
                Page("Branding & logo", "/Settings/Branding", $"Name, colours, logo, overview wording, and the default appearance ({Themes.Label(store.Branding.DefaultAppearance).ToLowerInvariant()}).",
                    "branding brand name colour color logo crest dark mode light appearance theme overview wording"),
                Page("Backups & data", "/Settings/Backups", lastBackup + (DataSyncedBy is { } synced ? $" The data folder is inside {synced}." : ""),
                    "backup restore data folder onedrive", backupBadge, backups.NeedsAttention(DateTime.UtcNow) || DataSyncedBy is not null || (backups.Enabled && sameDrive)),
                Page("Sign-in security", "/Settings/SignIn",
                    $"{(IsHttps ? "Passwords reach the helpdesk encrypted." : "Passwords cross the network unencrypted.")} Two-step sign-in {(store.RequireTwoFactor ? "is required" : $"is set up for {TwoFactorCount} of {ActiveStaff} staff")}. {(RecentLockouts == 0 ? "No lockouts" : RecentLockouts == 1 ? "1 lockout" : $"{RecentLockouts} lockouts")} in the last 7 days.",
                    "sign in login password lockout https encryption security two-step 2fa mfa authenticator multi-factor", !IsHttps ? "Not encrypted" : store.RequireTwoFactor ? "Two-step required" : "HTTPS",
                    !IsHttps || (!store.RequireTwoFactor && TwoFactorCount < ActiveStaff)),
                Page("Notifications", "/Settings/Notifications", NotificationSummary(),
                    "notifications pings pop-ups toast alerts banner reminder enable disable staff requesters portal sound",
                    !store.NotificationSettings.Enabled ? "Off" : null),
                Page("Data retention", "/Settings/Retention", RetentionSummary(),
                    "retention gdpr delete old tickets anonymise leavers former staff audit log months data protection",
                    store.Retention.AnyOn ? "On" : null),
                new("Audit log", "/Settings/Audit", "Who changed what, and when, across the whole helpdesk.", "audit log history changes who", Open: CanSeeAudit),
                new("Database", "/Settings/Database", "How big the data is and how fast it's growing, disk space and save times - and every table exactly as it is stored, read-only.",
                    "database raw data tables rows sql sqlite debug problem investigate health size growth disk space slow save", "Read-only", Open: CanSeeRaw),
                Page("Error log", "/Settings/Log", RecentErrors == 0 ? "Warnings and errors the helpdesk has recorded. None in the last 7 days." : $"Warnings and errors the helpdesk has recorded. {RecentErrors} error{(RecentErrors == 1 ? "" : "s")} in the last 7 days.",
                    "error log problems crash warning logs reference", RecentErrors > 0 ? $"{RecentErrors} this week" : null, RecentErrors > 0),
                new("About EduHelpdesk", "/Settings", $"Version {AppVersion}. Made by Steven Davidson; free to use and share under the MIT licence, provided as it is with no warranty.",
                    "about version licence license author release update", $"v{AppVersion}", Info: true),
                Page("Go live & reset", "/Settings/Reset", HasDemoData ? "Remove the worked example a new install starts with, or reset everything." : "Reset the helpdesk to how a new install starts.",
                    "demo data go live factory reset erase delete everything", HasDemoData ? "Demo data present" : null),
            ]),
        ];
    }
}
