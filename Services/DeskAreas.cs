namespace EduHelpdesk.Services;

// The parts of the top bar a desk can do without (Settings → Desks): the Facilities desk has no use for Kits or Projects,
// IT none for the Facilities menu. Tickets, Reports and Settings are every desk's. The bar shows an area when any desk being
// worked on (HelpdeskStore.DeskSwitch) uses it, and only to whoever's role can open it - an area ticked here never
// opens a page the role can't. With one desk the setting is out of sight and everything shows, as before desks.
// A desk stores the areas it doesn't use, so an area added later shows on every desk until someone unticks it.
public static class DeskAreas
{
    public const string Assets = "assets";
    public const string Kits = "kits";
    public const string Loans = "loans";
    public const string Parts = "parts";
    public const string Suppliers = "suppliers";
    public const string Projects = "projects";
    // The Facilities menu: premises checks, cleaning rounds, job spend and contractors. It was the cleaning rounds link,
    // stored as RoundsBefore, which reads as this.
    public const string Facilities = "facilities";
    public const string RoundsBefore = "rounds";
    public const string People = "people";
    public const string Compliance = "compliance";

    public sealed record Area(string Key, string Label, string Description);

    public static readonly IReadOnlyList<Area> All =
    [
        new(Assets, "Assets", "The asset register and its reports: kit on IT, plant on Site."),
        new(Kits, "Kits", "Loan kits made up of several assets."),
        new(Loans, "Loans", "Lending assets and kits to staff."),
        new(Parts, "Parts", "Spare parts and stock levels."),
        new(Suppliers, "Suppliers", "The supplier directory."),
        new(Projects, "Projects", "Projects, their quotes and the projects report."),
        new(Facilities, "Facilities", "The Facilities menu: premises checks, cleaning rounds, job spend and contractors."),
        new(People, "People & onboarding", "Requesters, technician accounts, roles and new starters."),
        new(Compliance, "Compliance registers", "The compliance summary, contracts register and access control."),
    ];

    public static bool IsKnown(string? key) => All.Any(x => x.Key == key);
}
