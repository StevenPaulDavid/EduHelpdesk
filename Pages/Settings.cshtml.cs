using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Settings is an index of the settings pages, grouped by what they are about. It used to be one long page holding
// fifteen forms, and since the page itself only needs Settings: Access, a role given Access and not Edit could post
// every one of them - branding, imports, even the factory reset. Every form now lives on a page under /Settings, which
// needs Settings: Edit (Program.cs), and this page has no handlers at all.
public class SettingsModel(HelpdeskStore store) : PageModel
{
    public sealed record Card(string Title, string Page, string Description, string Keywords, string? Badge = null, bool Warn = false,
        bool Open = true, IDictionary<string, string>? Route = null);
    public sealed record Group(string Title, IReadOnlyList<Card> Cards);

    // Opening a settings page needs Settings: Edit, so a role with only Access sees what is there but no links.
    public bool CanEdit => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public bool CanSeeAudit => store.UserCan(User, Modules.AuditLog, ModulePermission.Access);
    public HelpdeskStore.BackupSettings Backups => store.Backups;
    public string? DataSyncedBy => store.Location?.SyncedBy;
    public bool IsHttps => Request.IsHttps;
    public int RecentLockouts => store.CountAuditEntries("Sign-in", DateTime.UtcNow.AddDays(-7));
    public bool HasDemoData => store.HasDemoData;
    public IReadOnlyList<Group> Groups { get; private set; } = [];

    public void OnGet()
    {
        var edit = CanEdit;
        Card Page(string title, string page, string description, string keywords, string? badge = null, bool warn = false) =>
            new(title, page, description, keywords, badge, warn, edit);
        Card List(string title, string page, IReadOnlyCollection<string> values, string description, string keywords) =>
            Page(title, page, description, keywords, $"{values.Count}");

        var backups = Backups;
        var backupBadge = backups.NeedsAttention(DateTime.UtcNow) ? "Needs attention" : backups.Enabled ? "Nightly" : "Off";
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
            ]),
            new("Projects",
            [
                Page("Spending bands", "/Settings/SpendingBands", "Your finance policy's thresholds, shown on every project.", "spending bands quotes finance policy threshold"),
                List("Purchasing requirements", "/Settings/PurchasingRequirements", store.PurchasingRequirements, "Tick boxes offered when a project is requested.", "purchasing requirement project request"),
            ]),
            new("System",
            [
                Page("Branding & logo", "/Settings/Branding", $"Name, colours, logo, overview wording, and the default appearance ({Themes.Label(store.Branding.DefaultAppearance).ToLowerInvariant()}).",
                    "branding brand name colour color logo crest dark mode light appearance theme overview wording"),
                Page("Backups & data", "/Settings/Backups", lastBackup + (DataSyncedBy is { } synced ? $" The data folder is inside {synced}." : ""),
                    "backup restore data folder onedrive", backupBadge, backups.NeedsAttention(DateTime.UtcNow) || DataSyncedBy is not null),
                new("Sign-in security", "/Settings/Audit",
                    $"{(IsHttps ? "Passwords reach the helpdesk encrypted." : "Passwords cross the network unencrypted.")} Accounts lock for {(int)SignInThrottle.Window.TotalMinutes} minutes after {SignInThrottle.AccountLimit} wrong passwords. {(RecentLockouts == 0 ? "No lockouts" : RecentLockouts == 1 ? "1 lockout" : $"{RecentLockouts} lockouts")} in the last 7 days.",
                    "sign in login password lockout https encryption security", IsHttps ? "HTTPS" : "Not encrypted", !IsHttps,
                    CanSeeAudit, new Dictionary<string, string> { ["section"] = "Sign-in" }),
                new("Audit log", "/Settings/Audit", "Who changed what, and when, across the whole helpdesk.", "audit log history changes who", Open: CanSeeAudit),
                Page("Go live & reset", "/Settings/Reset", HasDemoData ? "Remove the worked example a new install starts with, or reset everything." : "Reset the helpdesk to how a new install starts.",
                    "demo data go live factory reset erase delete everything", HasDemoData ? "Demo data present" : null),
            ]),
        ];
    }
}
