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
        bool Open = true, IDictionary<string, string>? Route = null, bool Info = false, bool Good = false);
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

    private string TeamsSummary()
    {
        var teams = store.TeamsSettings;
        if (!teams.HasUrl) return "Post to a Teams channel when a requester submits a ticket or a ticket goes overdue. No webhook is set up yet.";
        if (!teams.Enabled) return "A webhook is saved but posting is switched off.";
        var events = new List<string>();
        if (teams.NewTicket) events.Add("new tickets from the portal");
        if (teams.Overdue) events.Add("overdue tickets");
        return events.Count == 0 ? "Posting is on, but no events are ticked." : $"Posts {string.Join(" and ", events)} to Teams.";
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

    // What each card says beyond its title: a description, and an optional badge (a count, or a state worth seeing).
    private sealed record Detail(string Description, string? Badge = null, bool Warn = false, bool Good = false);

    public void OnGet()
    {
        RecentErrors = FileLog.CountSince(log.Folder, DateTime.Now.AddDays(-7));
        static Detail List(IReadOnlyCollection<string> values, string description) => new(description, $"{values.Count}");

        var backups = Backups;
        // Backups beside the data survive a mistake or a bad update, but not the drive failing - worth a flag here too.
        var sameDrive = string.Equals(Path.GetPathRoot(Path.GetFullPath(backups.Folder)), Path.GetPathRoot(Path.GetFullPath(store.DataFolder)), StringComparison.OrdinalIgnoreCase);
        var backupBadge = backups.NeedsAttention(DateTime.UtcNow) ? "Needs attention" : !backups.Enabled ? "Off" : sameDrive ? "Same drive as data" : "Nightly";
        var lastBackup = backups.LastSuccessAt is { } last ? $"Last backup {last.ToLocalTime():ddd d MMM, HH:mm}." : "No backup has been made yet.";

        // Keyed by page. The titles, order, grouping and who can open each come from SettingsMenu, which the settings
        // pages' own menu uses too.
        var details = new Dictionary<string, Detail>(StringComparer.OrdinalIgnoreCase)
        {
            ["/Settings/TicketRules"] = new("Due-soon window, how long replies reopen closed tickets, which tickets need a closing message, and the print template.", store.HasPrintTemplate ? "Template uploaded" : null),
            ["/Settings/Statuses"] = List(store.Statuses, "The steps a ticket moves through, what each means, and which stop the SLA clock."),
            ["/Settings/Categories"] = List(store.Categories, "What a ticket is about, for filters, reports and templates."),
            ["/Settings/Priorities"] = List(store.Priorities, "How urgent a ticket is."),
            ["/Settings/Slas"] = List(store.Slas.Select(x => x.Name).ToList(), "How long tickets have, by priority and category."),
            ["/Settings/SchoolDay"] = new("The school week and lesson times that work-day and period SLAs count against."),
            ["/Settings/ServiceCatalogue"] = List(store.ServiceItems.Select(x => $"{x.Category} › {x.Name}").ToList(), "The common problems staff pick from when they report one in the portal."),
            ["/Settings/TicketTemplates"] = List(store.TicketTemplates.Select(x => x.Name).ToList(), "Saved starting points for repeat jobs."),
            ["/Settings/TicketAttributes"] = List(store.TicketAttributeDefinitions.Select(x => x.Name).ToList(), "Extra fields on tickets, by category."),
            ["/Settings/InventoryRules"] = new("Asset review window, DfE check and end-of-support windows, academic year, parts reorder threshold and repeat-borrowing flag."),
            ["/Settings/AssetTypes"] = List(store.AssetTypes, "Kinds of device, and how long each should last."),
            ["/Settings/AssetMakes"] = List(store.AssetMakes, "Manufacturers."),
            ["/Settings/AssetModels"] = List(store.AssetModels, "Models, each linked to its make."),
            ["/Settings/AssetStatuses"] = List(store.AssetStatuses, "Where an asset is in its life."),
            ["/Settings/AssetConditions"] = List(store.AssetConditions, "The condition column of the DfE asset register."),
            ["/Settings/AssetAttributes"] = List(store.AssetAttributeDefinitions.Select(x => x.Name).ToList(), "Extra fields on assets, by type."),
            ["/Settings/PartCategories"] = List(store.PartCategories, "How parts are grouped."),
            ["/Settings/PartLocations"] = List(store.PartLocations, "Where parts are kept."),
            ["/Settings/LoanReasons"] = List(store.LoanReasons, "Why a device was borrowed."),
            ["/Settings/Teams"] = List(store.TechnicianTeams, "Technician teams that tickets can be assigned to."),
            ["/Settings/Departments"] = List(store.Departments, "Requesters' departments."),
            ["/Settings/Buildings"] = List(store.Buildings, "Buildings or sites, recorded with the room on each asset."),
            ["/Settings/Locations"] = List(store.Locations, "Rooms and areas, for tickets, assets and people."),
            ["/Settings/Imports"] = new("Requesters, staff accounts and option lists from CSV files."),
            ["/Settings/Onboarding"] = new("The tasks for each kind of new starter, and when they're due."),
            ["/Settings/SpendingBands"] = new("Your finance policy's thresholds, shown on every project, and whether project pages show amounts with or without VAT."),
            ["/Settings/PurchasingRequirements"] = List(store.PurchasingRequirements, "Tick boxes offered when a project is requested."),
            ["/Settings/Branding"] = new($"Name, colours, logo, overview wording, and the default appearance ({Themes.Label(store.Branding.DefaultAppearance).ToLowerInvariant()})."),
            ["/Settings/Backups"] = new(lastBackup + (DataSyncedBy is { } synced ? $" The data folder is inside {synced}." : ""), backupBadge,
                backups.NeedsAttention(DateTime.UtcNow) || DataSyncedBy is not null || (backups.Enabled && sameDrive)),
            ["/Settings/SignIn"] = new($"{(IsHttps ? "Passwords reach the helpdesk encrypted." : "Passwords cross the network unencrypted.")} Two-step sign-in {(store.RequireTwoFactor ? "is required" : $"is set up for {TwoFactorCount} of {ActiveStaff} staff")}. {(RecentLockouts == 0 ? "No lockouts" : RecentLockouts == 1 ? "1 lockout" : $"{RecentLockouts} lockouts")} in the last 7 days.",
                IsHttps ? "Encrypted" : "Not encrypted", Warn: !IsHttps, Good: IsHttps),
            ["/Settings/Notifications"] = new(NotificationSummary(), !store.NotificationSettings.Enabled ? "Off" : null),
            ["/Settings/TeamsChannel"] = new(TeamsSummary(), store.TeamsSettings.Ready ? "On" : null),
            ["/Settings/Retention"] = new(RetentionSummary(), store.Retention.AnyOn ? "On" : null),
            ["/Settings/Audit"] = new("Who changed what, and when, across the whole helpdesk."),
            ["/Settings/Database"] = new("How big the data is and how fast it's growing, disk space and save times - and every table exactly as it is stored, read-only.", "Read-only"),
            ["/Settings/Log"] = new(RecentErrors == 0 ? "Warnings and errors the helpdesk has recorded. None in the last 7 days." : $"Warnings and errors the helpdesk has recorded. {RecentErrors} error{(RecentErrors == 1 ? "" : "s")} in the last 7 days.",
                RecentErrors > 0 ? $"{RecentErrors} this week" : null, RecentErrors > 0),
            ["/Settings"] = new($"Version {AppVersion}. Made by Steven Davidson; free to use and share under the MIT licence, provided as it is with no warranty.", $"v{AppVersion}"),
            ["/Settings/Reset"] = new(HasDemoData ? "Remove the worked example a new install starts with, or reset everything." : "Reset the helpdesk to how a new install starts.", HasDemoData ? "Demo data present" : null),
        };

        Groups = SettingsMenu.Sections.Select(section => new Group(section.Title, section.Items.Select(item =>
        {
            var detail = details.GetValueOrDefault(item.Page) ?? new Detail("");
            return new Card(item.Title, item.Page, detail.Description, item.Keywords, detail.Badge, detail.Warn,
                Open: SettingsMenu.CanOpen(store, User, item), Info: item.Need == SettingsMenu.Need.Info, Good: detail.Good);
        }).ToList())).ToList();
    }
}
