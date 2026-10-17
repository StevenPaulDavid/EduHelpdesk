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
            new("Desks", "/Settings/Desks", "desk desks it facilities site premises caretaker cleaning estates departments prefix ticket number portal tiles separate teams switch off rename"),
            new("Ticket queues & closing", "/Settings/TicketRules", "due soon overdue window hours reopen reply closed days closing message requirement print template word docx"),
            new("Statuses", "/Settings/Statuses", "status on hold pause sla clock"),
            new("Categories", "/Settings/Categories", "category hardware software"),
            new("Priorities", ListPage("tickets"), "priorities priority urgent high normal low"),
            new("SLAs", "/Settings/Slas", "sla service level due date target"),
            new("School day and periods", "/Settings/SchoolDay", "school day week periods lessons timetable working hours"),
            new("Service catalogue", "/Settings/ServiceCatalogue", "service catalogue catalog sub category portal common problems"),
            new("Ticket templates", "/Settings/TicketTemplates", "template repeat job"),
            new("Recurring tickets & checks", "/Settings/RecurringTickets", "recurring reoccurring repeating scheduled schedule routine regular weekly monthly termly yearly checks maintenance checklist readings premises fire alarm water temperature legionella emergency lighting", Need.Facilities, Also: ["/Settings/Checklist"]),
            new("Ticket custom attributes", "/Settings/TicketAttributes", "custom fields attributes extra"),
        ]),
        new("Assets & inventory",
        [
            new("Asset, part & loan rules", "/Settings/InventoryRules", "review warranty replacement academic year finance reorder threshold low stock loan repeat borrowing dfe check interval end of support unsupported"),
            new("Asset types", "/Settings/AssetTypes", "type lifespan laptop"),
            new("Asset models", "/Settings/AssetModels", "model"),
            new("Custom asset attributes", "/Settings/AssetAttributes", "custom fields attributes extra"),
            new("Asset, part & loan lists", ListPage("assets"), "lists dropdown options asset makes asset statuses asset conditions part categories part locations loan reasons make manufacturer brand asset status disposed condition new used refurbished donated dfe part category location store cupboard loan reason kit"),
        ]),
        // The premises side: what the cleaning supervisor and site manager set up, open to "Set up facilities" too.
        new("Facilities",
        [
            new("Cleaning rounds", "/Settings/Rounds", "cleaning rounds rooms cleaner cleaners rota sheet supervisor spot check daily weekly", Need.Facilities),
            new("Cleaners", "/Settings/Cleaners", "cleaner cleaners cleaning staff names contractor rounds", Need.Facilities),
            new("Budget codes", "/Settings/BudgetCodes", "budget code cost spend reactive planned maintenance ppm finance job costs", Need.Facilities),
        ]),
        new("People & places",
        [
            new("Teams", "/Settings/Teams", "team"),
            new("Onboarding checklists", "/Settings/Onboarding", "onboarding new starter staff checklist template tasks"),
            new("Departments, buildings & rooms", ListPage("places"), "lists dropdown options departments buildings locations rooms department building block site campus room area"),
        ]),
        new("Projects",
        [
            new("Spending bands", "/Settings/SpendingBands", "spending bands quotes finance policy threshold vat ex inc excluding including amounts project page"),
            new("Purchasing requirements", ListPage("projects"), "purchasing requirements requirement project request tick boxes"),
        ]),
        new("Compliance",
        [
            new("Contract & access lists", ListPage("compliance"), "lists dropdown options contract types spend categories contract durations contract statuses person types system and area categories reasons for removing access contract type licence subscription lease support register spend contracts duration length yearly rolling active expired person staff pupil governor contractor service account access system area mis cloud key fob revoke remove leaver"),
        ]),
        new("System",
        [
            new("Features", "/Settings/Features", "features switch off turn off disable enable modules assets loans kits parts projects onboarding compliance contracts access register cleaning rounds premises checks job costs spend simplify"),
            new("Branding & logo", "/Settings/Branding", "branding brand name colour color logo crest dark mode light appearance theme overview wording"),
            new("Imports", "/Settings/Imports", "import csv upload users technicians lists bulk spiceworks", Also: ["/Settings/SpiceworksImport"]),
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

    // The plain lists the dropdowns offer - a name each, renamed or removed. They share one page, Settings → Lists
    // (Pages/Settings/Lists.cshtml), but each sits in the section it belongs to: the page at /Settings/Lists/<group>
    // shows that group's lists, a tab each. Lists with more to them (categories and teams on desks, statuses that pause
    // the clock, cleaners and budget codes for "Set up facilities") keep pages of their own.
    // Feature: the switchable part of the helpdesk the list is for (Services/Features.cs), if any.
    public sealed record ListItem(string Kind, string Title, string Group, string Description, string? Feature = null);
    public sealed record ListGroup(string Key, string Title, string Description);

    public static readonly IReadOnlyList<ListGroup> ListGroups =
    [
        new("tickets", "Priorities", "How urgent a ticket is."),
        new("assets", "Asset, part & loan lists", "Makes, statuses and conditions for assets, where parts are kept and how they're grouped, and why devices are borrowed."),
        new("places", "Departments, buildings & rooms", "Where people work and where things are: for requesters, tickets, assets and cleaning rounds."),
        new("projects", "Purchasing requirements", "Tick boxes offered when a project is requested."),
        new("compliance", "Contract & access lists", "The dropdowns of the contracts register and the access control register."),
    ];

    public static readonly IReadOnlyList<ListItem> Lists =
    [
        new("Priorities", "Priorities", "tickets", "How urgent a ticket is."),
        new("AssetMakes", "Asset makes", "assets", "Manufacturers.", Features.Assets),
        new("AssetStatuses", "Asset statuses", "assets", "Where an asset is in its life.", Features.Assets),
        new("AssetConditions", "Asset conditions", "assets", "The condition column of the DfE asset register.", Features.Assets),
        new("PartCategories", "Part categories", "assets", "How parts are grouped.", Features.Parts),
        new("PartLocations", "Part locations", "assets", "Where parts are kept.", Features.Parts),
        new("LoanReasons", "Loan reasons", "assets", "Why a device was borrowed.", Features.Loans),
        new("Departments", "Departments", "places", "Requesters' departments."),
        new("Buildings", "Buildings", "places", "Buildings or sites, recorded with the room on each asset."),
        new("Locations", "Locations (rooms)", "places", "Rooms and areas, for tickets, assets, people and cleaning rounds."),
        new("PurchasingRequirements", "Purchasing requirements", "projects", "Tick boxes offered when a project is requested.", Features.Projects),
        new("ContractTypes", "Contract types", "compliance", "What kind of contract each is: licence, subscription, lease.", Features.Compliance),
        new("SpendCategories", "Spend categories", "compliance", "The DfE spend categories contracts are grouped under.", Features.Compliance),
        new("ContractDurations", "Contract durations", "compliance", "How long contracts run for.", Features.Compliance),
        new("ContractStatuses", "Contract statuses", "compliance", "Where a contract is. Expired takes it out of the warnings.", Features.Compliance),
        new("PersonTypes", "Person types", "compliance", "Staff, pupils, governors, contractors and service accounts, for the access register.", Features.Compliance),
        new("AccessCategories", "System and area categories", "compliance", "How systems and areas are grouped on the access register.", Features.Compliance),
        new("RevokeReasons", "Reasons for removing access", "compliance", "Why access was taken away: leaver, changed role, removed at review.", Features.Compliance),
    ];

    public static string ListPage(string group) => "/Settings/Lists/" + group;
    public static ListItem? FindList(string? kind) => Lists.FirstOrDefault(x => string.Equals(x.Kind, kind, StringComparison.OrdinalIgnoreCase));
    public static ListGroup? FindListGroup(string? key) => ListGroups.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
    // Where a list is edited: /Settings/Lists/assets?kind=LoanReasons.
    public static string ListUrl(string kind) => FindList(kind) is { } list ? $"{ListPage(list.Group)}?kind={list.Kind}" : "/Settings/Lists";

    // Whether the entry is there at all: not while the feature it belongs to is switched off (Services/Features.cs). The
    // asset, part and loan rules stay while any of the three is on, and a group of lists while any of its lists' is.
    public static bool Shown(HelpdeskStore store, Item item)
    {
        var off = store.FeaturesOff;
        if (off.Count == 0) return true;
        if (item.Page == "/Settings/InventoryRules") return new[] { Features.Assets, Features.Parts, Features.Loans }.Any(x => !off.Contains(x));
        if (ListGroups.FirstOrDefault(g => item.Page == ListPage(g.Key)) is { } group) return ListsShown(store, group.Key).Count > 0;
        return Features.ForPath(item.Page, off) is null;
    }

    // A group's lists, less those of switched-off features.
    public static IReadOnlyList<ListItem> ListsShown(HelpdeskStore store, string group)
    {
        var off = store.FeaturesOff;
        return Lists.Where(x => x.Group == group && (x.Feature is null || !off.Contains(x.Feature))).ToList();
    }

    public static bool CanOpen(HelpdeskStore store, ClaimsPrincipal user, Item item) => item.Need switch
    {
        Need.AuditLog => store.UserCan(user, Modules.AuditLog, ModulePermission.Access),
        Need.RawDatabase => store.UserHasFlag(user, Modules.Flags.RawDatabase),
        Need.Info => false,
        Need.Facilities => store.CanSetUpFacilities(user),
        _ => store.UserCan(user, Modules.Settings, ModulePermission.Edit)
    };

    // The menu entry for the page at this path, or for the page it sits inside. /Settings/Lists on its own (where a
    // list's forms post back to) is the entry for the group of the list asked for.
    public static Item? For(string path, string? listKind = null)
    {
        if (string.Equals(path.TrimEnd('/'), "/Settings/Lists", StringComparison.OrdinalIgnoreCase))
            path = ListPage(FindList(listKind)?.Group ?? Lists[0].Group);
        return Sections.SelectMany(x => x.Items).Where(x => x.Need != Need.Info).FirstOrDefault(x =>
            string.Equals(x.Page, path, StringComparison.OrdinalIgnoreCase) || (x.Also?.Contains(path, StringComparer.OrdinalIgnoreCase) ?? false));
    }
}
