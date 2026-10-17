namespace EduHelpdesk.Services;

// The parts of the helpdesk a school can switch off (Settings → Features, HelpdeskStore.Features): a school that only
// wants tickets turns off assets, loans, projects and the rest. Tickets, people, reports and settings are the helpdesk
// itself and always stay.
//
// A feature owns role permissions - modules and ticks (Modules) - and switching it off refuses them to everyone,
// Administrators included, in HelpdeskStore.RoleAllows and RoleHasFlag. Every page, menu, report, dashboard chart and
// handler already asks those, so that is most of switching off done in one place; what isn't a permission (settings
// pages, the parts of a ticket or the portal that belong to a feature) asks IsFeatureOn. Nothing is deleted: switching
// a feature back on brings back everything it had.
public static class Features
{
    public const string Assets = "assets";
    public const string Loans = "loans";
    public const string Parts = "parts";
    public const string Projects = "projects";
    public const string Onboarding = "onboarding";
    public const string Compliance = "compliance";
    public const string Rounds = "rounds";
    public const string Checks = "checks";
    public const string Costs = "costs";

    // Needs: features this one can't work without - switching one of those off switches this off too, and switching
    // this on switches them on. Pages: the addresses that are this feature's, which say it's switched off rather than
    // that the role can't open them (Program.cs) - each a path and everything under it, or "=" for that path alone.
    public sealed record Feature(string Key, string Label, string Description, string[] Modules, string[] Flags, string[] Needs, string[] Pages);

    public static readonly IReadOnlyList<Feature> All =
    [
        new(Assets, "Assets", "The asset register, devices on tickets, the asset and finance reports, and the asset settings.",
            [Services.Modules.Assets], [Services.Modules.Flags.ReportAssets, Services.Modules.Flags.ReportFinance], [], ["/Assets", "/Asset", "/PrintLabel", "=/Reports", "/Reports/Print", "/Reports/Finance", "/Reports/FinancePrint", "/Settings/AssetTypes", "/Settings/AssetModels", "/Settings/AssetAttributes"]),
        new(Loans, "Kits and loans", "Lending devices and loan kits to staff, and the loans report.",
            [Services.Modules.Kits, Services.Modules.Loans], [Services.Modules.Flags.ReportLoans], [Assets], ["/Kits", "/Loans", "/Reports/Loans", "/Reports/LoansPrint"]),
        new(Parts, "Parts", "The spare parts inventory and stock levels, parts used on tickets, and the parts report.",
            [Services.Modules.Parts], [Services.Modules.Flags.ReportParts], [], ["/Parts", "/Reports/Parts", "/Reports/PartsPrint"]),
        new(Projects, "Projects", "Purchasing projects raised from the staff portal, their quotes, and the projects report.",
            [Services.Modules.Projects], [Services.Modules.Flags.AssignProjects, Services.Modules.Flags.ReportProjects], [], ["/Projects", "/Project", "/Reports/Projects", "/Reports/ProjectsPrint", "/Portal/NewProject", "/Portal/Projects", "/Portal/Project", "/Settings/SpendingBands"]),
        new(Onboarding, "Onboarding", "New staff checklists and the onboarding report.",
            [Services.Modules.Onboarding], [Services.Modules.Flags.ReportOnboarding], [], ["/Onboarding", "/Reports/Onboarding", "/Settings/Onboarding"]),
        new(Compliance, "Compliance registers", "The DfE contracts register, the access control register and the compliance summary.",
            [Services.Modules.Contracts, Services.Modules.Access], [], [], ["/Contracts", "/Contract", "/Access", "/Compliance"]),
        new(Rounds, "Cleaning rounds", "Rooms in cleaning rounds, the day's sheets, the cleaners list and the rounds report.",
            [], [Services.Modules.Flags.Rounds, Services.Modules.Flags.ReportRounds], [], ["/Rounds", "/RoundSheet", "/Reports/Rounds", "/Reports/RoundsPrint", "/Settings/Rounds", "/Settings/Cleaners"]),
        new(Checks, "Premises checks", "Checklists on recurring tickets, the premises checks page and the yearly log.",
            [], [Services.Modules.Flags.ReportChecks], [], ["/Checks", "/Reports/Checks", "/Reports/ChecksPrint", "/Settings/Checklist"]),
        new(Costs, "Job costs", "What jobs cost on tickets, budget codes, and the job spend report.",
            [], [Services.Modules.Flags.Costs, Services.Modules.Flags.ReportSpend], [], ["/Reports/Spend", "/Settings/BudgetCodes"]),
    ];

    // The switched-off feature an address belongs to, if any.
    public static Feature? ForPath(string path, IReadOnlySet<string> off) => All.Where(f => off.Contains(f.Key)).FirstOrDefault(f => f.Pages.Any(p =>
        p.StartsWith('=') ? string.Equals(path.TrimEnd('/'), p[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(path, p, StringComparison.OrdinalIgnoreCase) || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)));

    public static Feature? Find(string? key) => All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    // Which feature a module or a tick belongs to, if any.
    private static readonly Dictionary<string, string> ByModule = All.SelectMany(f => f.Modules.Select(m => (m, f.Key))).ToDictionary(x => x.m, x => x.Key, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> ByFlag = All.SelectMany(f => f.Flags.Select(m => (m, f.Key))).ToDictionary(x => x.m, x => x.Key, StringComparer.OrdinalIgnoreCase);
    public static string? ForModule(string module) => ByModule.GetValueOrDefault(module);
    public static string? ForFlag(string flag) => ByFlag.GetValueOrDefault(flag);

    // The features that can't work without this one.
    public static IReadOnlyList<Feature> NeededBy(string key) => All.Where(f => f.Needs.Contains(key)).ToList();
}
