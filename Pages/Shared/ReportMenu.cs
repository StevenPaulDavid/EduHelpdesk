using System.Security.Claims;
using EduHelpdesk.Models;
using EduHelpdesk.Services;

namespace EduHelpdesk.Pages;

// Every report, in order: the top bar's Reports menu and the tab strip across the report pages (_ReportTabs.cshtml)
// are both drawn from this, so they can't drift apart. Area is the part of the top bar the report belongs to
// (Services/DeskAreas.cs): the projects report goes when the desks being worked on don't use Projects. The ticket,
// spend and checks reports are every desk's.
public static class ReportMenu
{
    public sealed record Item(string Page, string Label, string Tab, string Flag, string? Area);

    public static readonly IReadOnlyList<Item> Items =
    [
        new("/Reports", "Assets", "Asset reports", Modules.Flags.ReportAssets, DeskAreas.Assets),
        new("/Reports/Tickets", "Tickets", "Ticket reports", Modules.Flags.ReportTickets, null),
        new("/Reports/Parts", "Parts", "Parts reports", Modules.Flags.ReportParts, DeskAreas.Parts),
        new("/Reports/Loans", "Loans", "Loan reports", Modules.Flags.ReportLoans, DeskAreas.Loans),
        new("/Reports/Finance", "Asset finance", "Finance", Modules.Flags.ReportFinance, DeskAreas.Assets),
        new("/Reports/Projects", "Projects", "Projects", Modules.Flags.ReportProjects, DeskAreas.Projects),
        new("/Reports/Onboarding", "Onboarding", "Onboarding", Modules.Flags.ReportOnboarding, DeskAreas.People),
        new("/Reports/Spend", "Job spend", "Job spend", Modules.Flags.ReportSpend, null),
        new("/Reports/Checks", "Premises checks", "Premises checks", Modules.Flags.ReportChecks, null),
        new("/Reports/Rounds", "Cleaning rounds", "Cleaning rounds", Modules.Flags.ReportRounds, DeskAreas.Rounds),
    ];

    // The reports this person can open, for the areas the desks being worked on use. current - the report being looked
    // at - always stays, so its own tab is never missing.
    public static IReadOnlyList<Item> For(HelpdeskStore store, ClaimsPrincipal user, string? current = null) =>
        store.UserCan(user, Modules.Reports, ModulePermission.Access)
            ? Items.Where(x => store.UserHasFlag(user, x.Flag) && (x.Area is null || x.Page == current || store.AreaOn(user, x.Area))).ToList()
            : [];
}
