using System.Security.Claims;
using EduHelpdesk.Models;
using EduHelpdesk.Services;

namespace EduHelpdesk.Pages;

// Every settings page, grouped and in order. The Settings index draws its cards from this (adding a description and
// badge to each) and every page under /Settings draws its left-hand menu from it (Pages/Shared/_SettingsLayout.cshtml),
// so the two can't drift apart: a new settings page is added here once.
public static class SettingsMenu
{
    // What opening the page takes. Most need Settings: Edit (Program.cs); the audit log and the raw database have
    // their own permissions, so someone given only those - a DPO, say - still reaches them. Info is the About card,
    // which isn't a page.
    public enum Need { Settings, AuditLog, RawDatabase, Info }

    // Also: pages inside this one (a table under Database, the Spiceworks import under Imports), which highlight it.
    public sealed record Item(string Title, string Page, string Keywords, Need Need = Need.Settings, string[]? Also = null);
    public sealed record Section(string Title, IReadOnlyList<Item> Items);

    public static readonly IReadOnlyList<Section> Sections =
    [
        new("Tickets",
        [
            new("Ticket queues & closing", "/Settings/TicketRules", "due soon overdue window hours reopen reply closed days closing message requirement print template word docx"),
            new("Statuses", "/Settings/Statuses", "status on hold pause sla clock"),
            new("Categories", "/Settings/Categories", "category hardware software"),
            new("Priorities", "/Settings/Priorities", "priority urgent high normal low"),
            new("SLAs", "/Settings/Slas", "sla service level due date target"),
            new("School day and periods", "/Settings/SchoolDay", "school day week periods lessons timetable working hours"),
            new("Service catalogue", "/Settings/ServiceCatalogue", "service catalogue catalog sub category portal common problems"),
            new("Ticket templates", "/Settings/TicketTemplates", "template repeat job"),
            new("Ticket custom attributes", "/Settings/TicketAttributes", "custom fields attributes extra"),
        ]),
        new("Assets & inventory",
        [
            new("Asset, part & loan rules", "/Settings/InventoryRules", "review warranty replacement academic year finance reorder threshold low stock loan repeat borrowing dfe check interval end of support unsupported"),
            new("Asset types", "/Settings/AssetTypes", "type lifespan laptop"),
            new("Asset makes", "/Settings/AssetMakes", "make manufacturer brand"),
            new("Asset models", "/Settings/AssetModels", "model"),
            new("Asset statuses", "/Settings/AssetStatuses", "asset status disposed"),
            new("Asset conditions", "/Settings/AssetConditions", "asset condition new used refurbished donated dfe"),
            new("Custom asset attributes", "/Settings/AssetAttributes", "custom fields attributes extra"),
            new("Part categories", "/Settings/PartCategories", "part category"),
            new("Part locations", "/Settings/PartLocations", "part location store cupboard"),
            new("Loan reasons", "/Settings/LoanReasons", "loan reason kit"),
        ]),
        new("People & places",
        [
            new("Teams", "/Settings/Teams", "team"),
            new("Departments", "/Settings/Departments", "department"),
            new("Buildings", "/Settings/Buildings", "building block site campus dfe"),
            new("Locations (rooms)", "/Settings/Locations", "location room building site"),
            new("Imports", "/Settings/Imports", "import csv upload users technicians lists bulk spiceworks", Also: ["/Settings/SpiceworksImport"]),
            new("Onboarding checklists", "/Settings/Onboarding", "onboarding new starter staff checklist template tasks"),
        ]),
        new("Projects",
        [
            new("Spending bands", "/Settings/SpendingBands", "spending bands quotes finance policy threshold vat ex inc excluding including amounts project page"),
            new("Purchasing requirements", "/Settings/PurchasingRequirements", "purchasing requirement project request"),
        ]),
        new("System",
        [
            new("Branding & logo", "/Settings/Branding", "branding brand name colour color logo crest dark mode light appearance theme overview wording"),
            new("Backups & data", "/Settings/Backups", "backup restore data folder onedrive"),
            new("Sign-in security", "/Settings/SignIn", "sign in login password lockout https encryption security two-step 2fa mfa authenticator multi-factor"),
            new("Notifications", "/Settings/Notifications", "notifications pings pop-ups toast alerts banner reminder enable disable staff requesters portal sound"),
            new("Teams channel", "/Settings/TeamsChannel", "teams microsoft channel webhook post message new ticket overdue alert workflow"),
            new("Data retention", "/Settings/Retention", "retention gdpr delete old tickets anonymise leavers former staff audit log months data protection"),
            new("Audit log", "/Settings/Audit", "audit log history changes who", Need.AuditLog),
            new("Database", "/Settings/Database", "database raw data tables rows sql sqlite debug problem investigate health size growth disk space slow save", Need.RawDatabase, ["/Settings/DatabaseTable", "/Settings/DatabaseRecord"]),
            new("Error log", "/Settings/Log", "error log problems crash warning logs reference"),
            new("About EduHelpdesk", "/Settings", "about version licence license author release update", Need.Info),
            new("Go live & reset", "/Settings/Reset", "demo data go live factory reset erase delete everything"),
        ]),
    ];

    public static bool CanOpen(HelpdeskStore store, ClaimsPrincipal user, Item item) => item.Need switch
    {
        Need.AuditLog => store.UserCan(user, Modules.AuditLog, ModulePermission.Access),
        Need.RawDatabase => store.UserHasFlag(user, Modules.Flags.RawDatabase),
        Need.Info => false,
        _ => store.UserCan(user, Modules.Settings, ModulePermission.Edit)
    };

    // The menu entry for the page at this path, or for the page it sits inside.
    public static Item? For(string path) => Sections.SelectMany(x => x.Items).Where(x => x.Need != Need.Info).FirstOrDefault(x =>
        string.Equals(x.Page, path, StringComparison.OrdinalIgnoreCase) || (x.Also?.Contains(path, StringComparer.OrdinalIgnoreCase) ?? false));
}
