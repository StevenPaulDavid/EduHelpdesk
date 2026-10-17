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
    // which isn't a page. Facilities is Settings: Edit or "Set up facilities" (Modules.FacilitiesSetupPages).
    public enum Need { Settings, AuditLog, RawDatabase, Info, Facilities }

    // Also: pages inside this one (a table under Database, the Spiceworks import under Imports), which highlight it.
    public sealed record Item(string Title, string Page, string Keywords, Need Need = Need.Settings, string[]? Also = null);
    public sealed record Section(string Title, IReadOnlyList<Item> Items);

    public static readonly IReadOnlyList<Section> Sections =
    [
        new("Tickets",
        [
            new("Desks", "/Settings/Desks", "desk desks site facilities premises caretaker cleaning cleaners estates departments prefix ticket number portal tiles separate teams"),
            new("Ticket queues & closing", "/Settings/TicketRules", "due soon overdue window hours reopen reply closed days closing message requirement print template word docx"),
            new("Statuses", "/Settings/Statuses", "status on hold pause sla clock"),
            new("Categories", "/Settings/Categories", "category hardware software"),
            new("SLAs", "/Settings/Slas", "sla service level due date target"),
            new("School day and periods", "/Settings/SchoolDay", "school day week periods lessons timetable working hours"),
            new("Service catalogue", "/Settings/ServiceCatalogue", "service catalogue catalog sub category portal common problems"),
            new("Ticket templates", "/Settings/TicketTemplates", "template repeat job"),
            new("Recurring tickets", "/Settings/RecurringTickets", "recurring reoccurring repeating scheduled schedule routine regular weekly monthly termly yearly checks maintenance checklist readings premises fire alarm water temperature legionella emergency lighting", Need.Facilities, Also: ["/Settings/Checklist"]),
            new("Ticket custom attributes", "/Settings/TicketAttributes", "custom fields attributes extra"),
            new("Budget codes", "/Settings/BudgetCodes", "budget code cost spend reactive planned maintenance ppm finance job costs", Need.Facilities),
        ]),
        new("Assets & inventory",
        [
            new("Asset, part & loan rules", "/Settings/InventoryRules", "review warranty replacement academic year finance reorder threshold low stock loan repeat borrowing dfe check interval end of support unsupported"),
            new("Asset types", "/Settings/AssetTypes", "type lifespan laptop"),
            new("Asset models", "/Settings/AssetModels", "model"),
            new("Custom asset attributes", "/Settings/AssetAttributes", "custom fields attributes extra"),
        ]),
        new("People & places",
        [
            new("Teams", "/Settings/Teams", "team"),
            new("Cleaning rounds", "/Settings/Rounds", "cleaning rounds rooms cleaner cleaners rota sheet supervisor spot check daily weekly", Need.Facilities),
            new("Cleaners", "/Settings/Cleaners", "cleaner cleaners cleaning staff names contractor rounds", Need.Facilities),
            new("Imports", "/Settings/Imports", "import csv upload users technicians lists bulk spiceworks", Also: ["/Settings/SpiceworksImport"]),
            new("Onboarding checklists", "/Settings/Onboarding", "onboarding new starter staff checklist template tasks"),
            new("Lists", "/Settings/Lists", "lists dropdown options priorities asset makes asset statuses asset conditions part categories part locations loan reasons departments buildings locations (rooms) contract types spend categories contract durations contract statuses person types system and area categories reasons for removing access purchasing requirements priority urgent high normal low make manufacturer brand asset status disposed condition new used refurbished donated dfe part category location store cupboard loan reason kit department building block site campus room contract type licence subscription lease support register spend contracts duration length yearly rolling active expired person staff pupil governor contractor service account access system area mis cloud key fob revoke remove leaver purchasing requirement project request"),
        ]),
        new("Projects",
        [
            new("Spending bands", "/Settings/SpendingBands", "spending bands quotes finance policy threshold vat ex inc excluding including amounts project page"),
        ]),
        new("System",
        [
            new("Branding & logo", "/Settings/Branding", "branding brand name colour color logo crest dark mode light appearance theme overview wording"),
            new("Backups & data", "/Settings/Backups", "backup restore data folder onedrive"),
            new("Sign-in security", "/Settings/SignIn", "sign in login password lockout https encryption security two-step 2fa mfa authenticator multi-factor"),
            new("Notifications", "/Settings/Notifications", "notifications pings pop-ups toast alerts banner reminder enable disable staff requesters portal sound"),
            new("Teams channel", "/Settings/TeamsChannel", "teams microsoft channel webhook post message new ticket overdue alert workflow"),
            new("EduNotify", "/Settings/EduNotify", "edunotify notify staff pcs outage notice banner message api key tell staff recall integration"),
            new("Data retention", "/Settings/Retention", "retention gdpr delete old tickets anonymise leavers former staff audit log months data protection"),
            new("Audit log", "/Settings/Audit", "audit log history changes who", Need.AuditLog),
            new("Database", "/Settings/Database", "database raw data tables rows sql sqlite debug problem investigate health size growth disk space slow save", Need.RawDatabase, ["/Settings/DatabaseTable", "/Settings/DatabaseRecord"]),
            new("Error log", "/Settings/Log", "error log problems crash warning logs reference"),
            new("About EduHelpdesk", "/Settings", "about version licence license author release update", Need.Info),
            new("Go live & reset", "/Settings/Reset", "demo data go live factory reset erase delete everything"),
        ]),
    ];

    // The plain lists the dropdowns offer - a name each, renamed or removed - on one page, Settings → Lists
    // (Pages/Settings/Lists.cshtml), a tab each. Lists with more to them (categories and teams on desks, statuses that
    // pause the clock, cleaners and budget codes for "Set up facilities") keep pages of their own.
    public sealed record ListItem(string Kind, string Title, string Group, string Description);

    public static readonly IReadOnlyList<ListItem> Lists =
    [
        new("Priorities", "Priorities", "Tickets", "How urgent a ticket is."),
        new("AssetMakes", "Asset makes", "Assets & inventory", "Manufacturers."),
        new("AssetStatuses", "Asset statuses", "Assets & inventory", "Where an asset is in its life."),
        new("AssetConditions", "Asset conditions", "Assets & inventory", "The condition column of the DfE asset register."),
        new("PartCategories", "Part categories", "Assets & inventory", "How parts are grouped."),
        new("PartLocations", "Part locations", "Assets & inventory", "Where parts are kept."),
        new("LoanReasons", "Loan reasons", "Assets & inventory", "Why a device was borrowed."),
        new("Departments", "Departments", "People & places", "Requesters' departments."),
        new("Buildings", "Buildings", "People & places", "Buildings or sites, recorded with the room on each asset."),
        new("Locations", "Locations (rooms)", "People & places", "Rooms and areas, for tickets, assets and people."),
        new("ContractTypes", "Contract types", "Compliance", "What kind of contract each is: licence, subscription, lease."),
        new("SpendCategories", "Spend categories", "Compliance", "The DfE spend categories contracts are grouped under."),
        new("ContractDurations", "Contract durations", "Compliance", "How long contracts run for."),
        new("ContractStatuses", "Contract statuses", "Compliance", "Where a contract is. Expired takes it out of the warnings."),
        new("PersonTypes", "Person types", "Compliance", "Staff, pupils, governors, contractors and service accounts, for the access register."),
        new("AccessCategories", "System and area categories", "Compliance", "How systems and areas are grouped on the access register."),
        new("RevokeReasons", "Reasons for removing access", "Compliance", "Why access was taken away: leaver, changed role, removed at review."),
        new("PurchasingRequirements", "Purchasing requirements", "Projects", "Tick boxes offered when a project is requested."),
    ];

    public static ListItem? FindList(string? kind) => Lists.FirstOrDefault(x => string.Equals(x.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public static bool CanOpen(HelpdeskStore store, ClaimsPrincipal user, Item item) => item.Need switch
    {
        Need.AuditLog => store.UserCan(user, Modules.AuditLog, ModulePermission.Access),
        Need.RawDatabase => store.UserHasFlag(user, Modules.Flags.RawDatabase),
        Need.Info => false,
        Need.Facilities => store.CanSetUpFacilities(user),
        _ => store.UserCan(user, Modules.Settings, ModulePermission.Edit)
    };

    // The menu entry for the page at this path, or for the page it sits inside.
    public static Item? For(string path) => Sections.SelectMany(x => x.Items).Where(x => x.Need != Need.Info).FirstOrDefault(x =>
        string.Equals(x.Page, path, StringComparison.OrdinalIgnoreCase) || (x.Also?.Contains(path, StringComparer.OrdinalIgnoreCase) ?? false));
}
