using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// The dashboard: the helpdesk as charts, for technicians day to day. It follows the desk switcher and a period, its
// layout is the same for everyone, and each part shows only when this person has that report and the desks being
// worked on use that area (Sections). Clicking a mark opens the matching list. wwwroot/js/pages/dashboard.js redraws
// it every five minutes while it is on screen.
public class DashboardModel(HelpdeskStore store) : PageModel
{
    // Sent with the five-minute redraw, which must not count as someone using the helpdesk (Program.cs).
    public const string RefreshHeader = "dashboard-refresh";

    // What can appear, each with the report it needs (ReportMenu) and the area of the top bar it belongs to.
    public sealed record Section(string Key, string Flag, string? Area);
    public static readonly IReadOnlyList<Section> AllSections =
    [
        new("tickets", Modules.Flags.ReportTickets, null),
        new("assets", Modules.Flags.ReportAssets, DeskAreas.Assets),
        new("spend", Modules.Flags.ReportSpend, null),
        new("loans", Modules.Flags.ReportLoans, DeskAreas.Loans),
        new("parts", Modules.Flags.ReportParts, DeskAreas.Parts),
        new("projects", Modules.Flags.ReportProjects, DeskAreas.Projects),
    ];

    public static IReadOnlyList<string> Sections(HelpdeskStore store, System.Security.Claims.ClaimsPrincipal user) =>
        store.UserCan(user, Modules.Reports, ModulePermission.Access)
            ? AllSections.Where(x => store.UserHasFlag(user, x.Flag) && (x.Area is null || store.AreaOn(user, x.Area))).Select(x => x.Key).ToList()
            : [];

    public static readonly (string Key, string Label, int? Days, int? Months)[] Periods =
    [
        ("7d", "Last 7 days", 7, null), ("30d", "Last 30 days", 30, null), ("3m", "Last 3 months", null, 3), ("12m", "Last 12 months", null, 12)
    ];
    [BindProperty(SupportsGet = true, Name = "period")] public string? Period { get; set; }
    public string PeriodKey { get; private set; } = "30d";
    public string PeriodLabel { get; private set; } = "";
    public string WhenText => PeriodLabel.ToLowerInvariant();

    public IReadOnlyList<string> Shown { get; private set; } = [];
    public bool Shows(string section) => Shown.Contains(section);
    public string DeskText { get; private set; } = "";
    public DateTime Now { get; } = DateTime.UtcNow;
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public string StepUnit { get; private set; } = TimeSteps.Day;
    public string StepName => char.ToUpperInvariant(StepUnit[0]) + StepUnit[1..];

    // ---- Tickets ----
    public IReadOnlyList<Charts.Step> TicketSteps { get; private set; } = [];
    public IReadOnlyList<Charts.Step> LoggedSteps { get; private set; } = [];
    public Charts.Series OpenOverTime { get; private set; } = new("Open", "brand", []);
    public IReadOnlyList<Charts.Series> LoggedAndClosed { get; private set; } = [];
    public int OpenNow { get; private set; }
    public int LoggedInPeriod { get; private set; }
    public int ClosedInPeriod { get; private set; }
    public IReadOnlyList<Charts.Key> PriorityKeys { get; private set; } = [];
    public IReadOnlyList<Charts.Bar> Workload { get; private set; } = [];
    public int Unassigned { get; private set; }
    public IReadOnlyList<Charts.Bar> Due { get; private set; } = [];
    public int Overdue { get; private set; }
    public int DueSoonHours { get; private set; }
    public IReadOnlyList<string> HeatDays { get; private set; } = [];
    public IReadOnlyList<string> HeatHours { get; private set; } = [];
    public int[,] Heat { get; private set; } = new int[0, 0];
    private int[] _heatDayIndex = [];
    private int _firstHour;
    public string HeatTip(int row, int column)
    {
        var count = Heat[row, column];
        var hour = _firstHour + column;
        return $"{CultureInfo.InvariantCulture.DateTimeFormat.GetDayName((DayOfWeek)_heatDayIndex[row])} {hour:00}:00-{hour + 1:00}:00: {count} ticket{(count == 1 ? "" : "s")}";
    }

    // ---- Assets ----
    public IReadOnlyList<Charts.Step> AgeSteps { get; private set; } = [];
    public IReadOnlyList<Charts.Series> Age { get; private set; } = [];
    public int PastLife { get; private set; }
    public int NoPurchaseDate { get; private set; }
    public IReadOnlyList<Charts.Step> WarrantySteps { get; private set; } = [];
    public Charts.Series Warranty { get; private set; } = new("Warranties ending", "brand", []);
    public int WarrantyEnding { get; private set; }
    public IReadOnlyList<Charts.Key> StatusKeys { get; private set; } = [];
    public Charts.Bar? Statuses { get; private set; }
    public int AssetCount { get; private set; }
    public IReadOnlyList<Charts.Bar> RepeatFaults { get; private set; } = [];

    // ---- Spend, loans, parts, projects ----
    public IReadOnlyList<Charts.Step> SpendSteps { get; private set; } = [];
    public IReadOnlyList<Charts.Series> Spend { get; private set; } = [];
    public decimal SpendTotal { get; private set; }
    public IReadOnlyList<Charts.Step> LoanSteps { get; private set; } = [];
    public IReadOnlyList<Charts.Series> Loans { get; private set; } = [];
    public int LoansOut { get; private set; }
    public int LoansOverdue { get; private set; }
    public string? LoansUrl { get; private set; }
    public string? OverdueLoansUrl { get; private set; }
    public IReadOnlyList<Charts.Bar> LowParts { get; private set; } = [];
    public int LowPartCount { get; private set; }
    public IReadOnlyList<Charts.Bar> ProjectStages { get; private set; } = [];
    public int ActiveProjects { get; private set; }

    public IActionResult OnGet()
    {
        Shown = Sections(store, User);
        if (Shown.Count == 0) return Forbid();
        var period = Periods.FirstOrDefault(x => x.Key == Period);
        if (period.Key is null) period = Periods[1];
        (PeriodKey, PeriodLabel) = (period.Key, period.Label);
        var since = period.Days is { } days ? Now.AddDays(-days) : Now.AddMonths(-period.Months!.Value);
        var steps = TimeSteps.Between(since, Now);
        StepUnit = steps[0].Unit;

        var working = store.WorkingDesks(User);
        DeskText = store.HasSeveralDesks ? working.Count == 0 ? "every desk" : HelpdeskStore.DeskPhrase(working) : "";
        var deskIds = working.Select(x => x.Id).ToHashSet();
        var deskOf = store.DeskLookup();
        var tickets = store.TicketsFor(User).Where(t => deskIds.Count == 0 || deskIds.Contains(deskOf(t.Category).Id)).ToList();

        if (Shows("tickets")) BuildTickets(tickets, since, steps);
        if (Shows("assets")) BuildAssets(tickets, since);
        if (Shows("spend")) BuildSpend(tickets, since);
        if (Shows("loans")) BuildLoans(steps);
        if (Shows("parts")) BuildParts();
        if (Shows("projects")) BuildProjects();
        return Page();
    }

    private string? Jobs(object values) => Url.Page("/Jobs", values);

    private void BuildTickets(List<TicketRecord> tickets, DateTime since, IReadOnlyList<TimeSteps.Step> steps)
    {
        var trend = TicketReports.Trend(tickets, since, Now);
        TicketSteps = trend.Select(x => new Charts.Step(x.Label, x.Step.Name)).ToList();
        LoggedSteps = trend.Select(x => new Charts.Step(x.Label, x.Step.Name,
            Jobs(new { view = "all", from = x.Step.From.ToString("yyyy-MM-dd"), to = x.Step.To.ToString("yyyy-MM-dd") }))).ToList();
        OpenOverTime = new("Open", "brand", trend.Select(x => (double)x.OpenAtEnd).ToList());
        LoggedAndClosed = [new("Logged", "c1", trend.Select(x => (double)x.Logged).ToList()), new("Closed", "c2", trend.Select(x => (double)x.Closed).ToList())];
        LoggedInPeriod = trend.Sum(x => x.Logged);
        ClosedInPeriod = trend.Sum(x => x.Closed);

        var open = tickets.Where(t => !TicketInsights.IsClosed(t)).ToList();
        OpenNow = open.Count;

        // Workload: each technician's open tickets, split by priority from the most urgent, darkest, at the left.
        var priorities = OrderedPriorities(open);
        PriorityKeys = priorities.Select((p, i) => new Charts.Key(p, $"p{Math.Max(1, 4 - i)}")).ToList();
        var names = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
        Workload = open.GroupBy(t => t.TechnicianId)
            .Select(g =>
            {
                var tech = g.Key is { } id ? id.ToString() : "none";
                var overdue = g.Count(t => TicketInsights.IsOverdue(t, Now));
                return new Charts.Bar(g.Key is { } techId ? names.GetValueOrDefault(techId, "Unknown") : "Unassigned",
                    priorities.Select(p => (double)g.Count(t => Same(t.Priority, p))).ToList(),
                    Href: Jobs(new { tech }),
                    Note: overdue > 0 ? $"{overdue} overdue" : null, NoteWarn: overdue > 0,
                    Hrefs: priorities.Select(p => Jobs(new { tech, priority = p })).ToList());
            })
            .OrderBy(x => x.Label == "Unassigned").ThenByDescending(x => x.Values.Sum()).ThenBy(x => x.Label).Take(12).ToList();
        Unassigned = open.Count(t => t.TechnicianId is null);

        // When open tickets are due.
        DueSoonHours = store.TicketDueSoonHours;
        var states = open.Select(t => TicketInsights.DueStateOf(t, Now, DueSoonHours)).ToList();
        Overdue = states.Count(x => x == DueState.Overdue);
        var later = open.Count(t => t.DueDate is not null && TicketInsights.DueStateOf(t, Now, DueSoonHours) == DueState.None);
        Due =
        [
            new("Overdue", [Overdue], Jobs(new { view = "overdue" }), Tone: "critical"),
            new($"Due in the next {DueSoonHours} hours", [states.Count(x => x == DueState.Soon)], Jobs(new { view = "overdue" }), Tone: "serious"),
            new("Due later", [later], Jobs(new { sort = "due", dir = "asc" }), Tone: "brand"),
            new("Clock paused", [states.Count(x => x == DueState.Paused)], Jobs(new { sort = "due", dir = "asc" }), Tone: "quiet"),
            new("No due date", [open.Count(t => t.DueDate is null)], Jobs(new { sort = "due", dir = "asc" }), Tone: "quiet"),
        ];

        // Busiest days and times: tickets logged in the period by local day and hour. The school day is always shown;
        // weekends and the small hours only when something came in then.
        var logged = tickets.Where(t => t.CreatedAt >= since).Select(t => t.CreatedAt.ToLocalTime()).ToList();
        var dayOrder = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var days = dayOrder.Where(d => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday) || logged.Any(x => x.DayOfWeek == d)).ToList();
        _firstHour = Math.Min(7, logged.Count == 0 ? 7 : logged.Min(x => x.Hour));
        var lastHour = Math.Max(17, logged.Count == 0 ? 17 : logged.Max(x => x.Hour));
        HeatDays = days.Select(d => d.ToString()[..3]).ToList();
        HeatHours = Enumerable.Range(_firstHour, lastHour - _firstHour + 1).Select(h => h.ToString("00")).ToList();
        _heatDayIndex = days.Select(d => (int)d).ToArray();
        Heat = new int[days.Count, HeatHours.Count];
        foreach (var when in logged)
        {
            var row = days.IndexOf(when.DayOfWeek);
            if (row >= 0) Heat[row, when.Hour - _firstHour]++;
        }
    }

    // Urgent, High, Normal, Low, then any others the school has added, in its list order - only those in use.
    private List<string> OrderedPriorities(IReadOnlyCollection<TicketRecord> open)
    {
        var known = new[] { "Urgent", "High", "Normal", "Low" };
        var order = known.Concat(store.Priorities.Where(p => !known.Any(k => Same(k, p)))).ToList();
        var used = open.Select(t => t.Priority).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return order.Where(p => used.Any(u => Same(u, p))).Concat(used.Where(u => !order.Any(p => Same(p, u)))).ToList();
    }

    private void BuildAssets(List<TicketRecord> tickets, DateTime since)
    {
        var shown = store.AssetDesksShown(User, null);
        var deskOf = store.AssetDeskLookup();
        var shownIds = shown.Select(x => x.Id).ToHashSet();
        var register = store.Assets.Where(x => shownIds.Count == 0 || shownIds.Contains(deskOf(x.Type).Id)).ToList();
        var assets = register.Where(x => !HelpdeskStore.IsDisposed(x)).ToList();
        AssetCount = assets.Count;
        var lifespans = store.AssetTypeLifespans;
        var reports = Url.Page("/Reports");

        // Fleet age: devices by the year they were bought, those past their expected life on top.
        var dated = assets.Where(x => x.PurchaseDate is not null).ToList();
        NoPurchaseDate = assets.Count - dated.Count;
        bool Past(AssetRecord asset) => AssetInsights.ReplacementDate(asset, lifespans) is { } due && due <= Today;
        PastLife = dated.Count(Past);
        if (dated.Count > 0)
        {
            var years = Enumerable.Range(dated.Min(x => x.PurchaseDate!.Value.Year), Today.Year - dated.Min(x => x.PurchaseDate!.Value.Year) + 1).ToList();
            AgeSteps = years.Select(y => new Charts.Step(y.ToString(CultureInfo.InvariantCulture), $"Bought in {y}", reports + "#age")).ToList();
            Age =
            [
                new("Within its expected life", "brand", years.Select(y => (double)dated.Count(x => x.PurchaseDate!.Value.Year == y && !Past(x))).ToList()),
                new("Past its expected life", "serious", years.Select(y => (double)dated.Count(x => x.PurchaseDate!.Value.Year == y && Past(x))).ToList()),
            ];
        }

        // Warranties ending in each of the next twelve months.
        var months = Enumerable.Range(0, 12).Select(i => new DateOnly(Today.Year, Today.Month, 1).AddMonths(i)).ToList();
        WarrantySteps = months.Select(m => new Charts.Step(m.ToString("MMM yy", CultureInfo.GetCultureInfo("en-GB")), m.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB")),
            Url.Page("/Reports", null, new { warranty = 365 }, null, null, "warranty"))).ToList();
        Warranty = new("Warranties ending", "brand", months.Select(m => (double)assets.Count(x => x.WarrantyEnd is { } end && end >= Today && end.Year == m.Year && end.Month == m.Month)).ToList());
        WarrantyEnding = (int)Warranty.Values.Sum();

        // By status, as one bar: the four biggest, then the rest together.
        var byStatus = assets.GroupBy(x => string.IsNullOrWhiteSpace(x.Status) ? "(none)" : x.Status, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Status: g.Key, Count: g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Status).ToList();
        var top = byStatus.Take(byStatus.Count > 5 ? 4 : 5).ToList();
        var rest = byStatus.Skip(top.Count).Sum(x => x.Count);
        StatusKeys = top.Select((x, i) => new Charts.Key($"{x.Status} ({x.Count})", $"c{i + 1}")).Concat(rest > 0 ? [new Charts.Key($"Other ({rest})", "quiet")] : []).ToList();
        if (assets.Count > 0)
            Statuses = new("All assets", top.Select(x => (double)x.Count).Concat(rest > 0 ? [rest] : []).ToList(),
                Hrefs: top.Select(x => Url.Page("/Assets", new { status = x.Status })).Concat(rest > 0 ? [Url.Page("/Assets")] : []).ToList());

        // Repeat-fault devices: most tickets in the period, two or more.
        var byId = assets.ToDictionary(x => x.Id);
        RepeatFaults = tickets.Where(t => t.CreatedAt >= since).SelectMany(t => t.AssetIds.Distinct().Select(id => (Id: id, Ticket: t)))
            .Where(x => byId.ContainsKey(x.Id)).GroupBy(x => x.Id)
            .Select(g => (Asset: byId[g.Key], Count: g.Count(), Open: g.Count(x => !TicketInsights.IsClosed(x.Ticket))))
            .Where(x => x.Count >= 2).OrderByDescending(x => x.Count).ThenBy(x => x.Asset.AssetTag).Take(8)
            .Select(x => new Charts.Bar(x.Asset.AssetTag, [x.Count], Url.Page("/Asset", new { id = x.Asset.Id }), $"{x.Asset.Make} {x.Asset.Model}".Trim(),
                x.Open > 0 ? $"{x.Open} open" : null))
            .ToList();
    }

    private void BuildSpend(List<TicketRecord> tickets, DateTime since)
    {
        var numbers = tickets.Select(t => t.Number).ToHashSet();
        var from = DateOnly.FromDateTime(since.ToLocalTime());
        var costs = store.TicketCosts.Where(x => numbers.Contains(x.TicketNumber) && x.Date >= from && x.Date <= Today).ToList();
        SpendTotal = costs.Sum(x => x.Net);
        var months = TimeSteps.Between(since, Now, TimeSteps.Month);
        var spendPage = Url.Page("/Reports/Spend");
        SpendSteps = months.Select(m => new Charts.Step(m.From.ToString("MMM", CultureInfo.GetCultureInfo("en-GB")), m.Name, spendPage)).ToList();
        // The four biggest budget codes, then the rest together.
        static string Code(TicketCost cost) => cost.BudgetCode.Length == 0 ? "No budget code" : cost.BudgetCode;
        var codes = costs.GroupBy(Code).OrderByDescending(g => g.Sum(x => x.Net)).Select(g => g.Key).ToList();
        var top = codes.Take(codes.Count > 5 ? 4 : 5).ToList();
        double In(TimeSteps.Step month, Func<TicketCost, bool> which) =>
            (double)costs.Where(x => x.Date >= month.From && x.Date <= month.To && which(x)).Sum(x => x.Net);
        Spend = top.Select((code, i) => new Charts.Series(code, $"c{i + 1}", months.Select(m => In(m, x => Code(x) == code)).ToList()))
            .Concat(codes.Count > top.Count ? [new Charts.Series("Other codes", "quiet", months.Select(m => In(m, x => !top.Contains(Code(x)))).ToList())] : [])
            .ToList();
    }

    private void BuildLoans(IReadOnlyList<TimeSteps.Step> steps)
    {
        var loans = store.AllLoans();
        var outNow = LoanInsights.CurrentlyOut(loans);
        LoansOut = outNow.Count;
        LoansOverdue = outNow.Count(x => x.IsOverdue(Today));
        LoansUrl = Url.Page("/Loans");
        OverdueLoansUrl = Url.Page("/Loans", new { overdue = true });
        LoanSteps = steps.Select(x => new Charts.Step(x.Label, x.Name)).ToList();
        // Out, and overdue, as they stood at the end of each step (or now, for the step still running).
        bool OutAt(LoanInsights.LoanEntry loan, DateTime end) => loan.SortDate < end && (loan.ReturnedAt is not { } back || back >= end);
        Loans =
        [
            new("On loan", "brand", steps.Select(s => (double)loans.Count(l => OutAt(l, Min(s.End)))).ToList()),
            new("Overdue", "critical", steps.Select(s => (double)loans.Count(l => OutAt(l, Min(s.End)) && l.DueBack is { } due
                && due < DateOnly.FromDateTime(Min(s.End).ToLocalTime()))).ToList()),
        ];
    }
    private DateTime Min(DateTime end) => end < Now ? end : Now;

    private void BuildParts()
    {
        var threshold = store.PartsDefaultReorderThreshold;
        var low = PartInsights.LowStock(store.Parts, threshold);
        LowPartCount = low.Count;
        var canEdit = store.UserCan(User, Modules.Parts, ModulePermission.Edit);
        LowParts = low.Take(10).Select(p =>
        {
            var reorder = p.ReorderThreshold ?? threshold;
            return new Charts.Bar(p.Name, [Math.Max(0, p.QuantityOnHand)],
                canEdit ? Url.Page("/Parts/Edit", new { id = p.Id }) : Url.Page("/Parts", new { flag = "low" }),
                Note: $"reorder at {reorder}", Tone: p.QuantityOnHand <= 0 ? "critical" : "serious", Marker: reorder, MarkerTip: $"Reorder at {reorder}");
        }).ToList();
    }

    private void BuildProjects()
    {
        var active = store.Projects.Where(x => x.IsActive).ToList();
        ActiveProjects = active.Count;
        ProjectStages = ProjectStatuses.All.Where(x => x != ProjectStatuses.Closed).Select(status =>
        {
            var at = active.Where(x => x.Status == status).ToList();
            var late = at.Count(x => x.IsOverdue(Today));
            return new Charts.Bar(status, [at.Count], Url.Page("/Projects", new { status }), Note: late > 0 ? $"{late} past due" : null, NoteWarn: late > 0);
        }).ToList();
    }

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
