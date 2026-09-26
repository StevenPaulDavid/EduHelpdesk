using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// Purchasing projects for SLT and the project lead: what is open now and with whom, how quickly proposals are turned
// round, and what the decided projects came to. "Now" figures ignore the period - an open project is open whatever year
// is picked. The period (an academic year, or all time) applies to what happened: projects raised, proposals made and
// projects closed in it. Values are always the chosen quotes, as the proposal shows them.
public class ProjectReportsModel(HelpdeskStore store) : PageModel
{
    public const string AllTime = "all";

    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);
    // The report can be given to someone who can't open projects themselves (a business manager), so rows only link
    // through when the project page would let them in.
    public bool CanOpenProjects => store.UserCan(User, Modules.Projects, ModulePermission.View);

    [BindProperty(SupportsGet = true, Name = "year")] public string? Year { get; set; }

    public int StartMonth { get; private set; }
    public IReadOnlyList<int> AvailableYears { get; private set; } = [];
    public int? StartYear { get; private set; }
    public string YearKey => StartYear?.ToString(CultureInfo.InvariantCulture) ?? AllTime;
    public string PeriodLabel { get; private set; } = "";
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);

    public sealed record ProjectRow(ProjectRecord Project, string Requester, string? Technician, QuoteTotals Totals, DateTime? ReadyAt)
    {
        public DateOnly? ReadyOn => ReadyAt is { } at ? DateOnly.FromDateTime(at.ToLocalTime()) : null;
        public DateOnly RaisedOn => DateOnly.FromDateTime(Project.CreatedAt.ToLocalTime());
        public DateOnly? ClosedOn => Project.ClosedAt is { } at ? DateOnly.FromDateTime(at.ToLocalTime()) : null;
        // Calendar days from being raised to the proposal first being marked ready.
        public int? DaysToProposal => ReadyOn is { } ready ? ready.DayNumber - RaisedOn.DayNumber : null;
        public bool? ProposalOnTime => ReadyOn is { } ready ? ready <= Project.DueDate : null;
    }

    public sealed record StatusRow(string Status, int Count, int Overdue, int DueSoon, decimal ValueIncVat);
    public sealed record WorkloadRow(string Name, bool IsUnassigned, int[] ByPriority, int Overdue, int AwaitingDecision, int ProposalsMade, double? MedianDays, int Closed)
    {
        public int Active => ByPriority.Sum();
    }
    public sealed record OutcomeRow(string Outcome, int Count, QuoteTotals Totals);

    public IReadOnlyList<ProjectRow> Open { get; private set; } = [];
    public IReadOnlyList<StatusRow> ByStatus { get; private set; } = [];
    public IReadOnlyList<WorkloadRow> Workload { get; private set; } = [];
    public IReadOnlyList<ProjectRow> Raised { get; private set; } = [];
    public IReadOnlyList<ProjectRow> ProposalsMade { get; private set; } = [];
    public IReadOnlyList<ProjectRow> Closed { get; private set; } = [];
    public IReadOnlyList<OutcomeRow> ByOutcome { get; private set; } = [];

    public int Unassigned => Open.Count(x => x.Project.TechnicianId is null);
    public int OverdueNow => Open.Count(x => x.Project.IsOverdue(Today));
    public IReadOnlyList<ProjectRow> AwaitingDecision => Open.Where(x => x.Project.Status == ProjectStatuses.ProposalReady).ToList();
    public double? MedianDaysToProposal => Median(ProposalsMade.Select(x => x.DaysToProposal).OfType<int>());
    public int OnTime => ProposalsMade.Count(x => x.ProposalOnTime == true);
    public int Late => ProposalsMade.Count(x => x.ProposalOnTime == false);

    public void OnGet()
    {
        StartMonth = store.AcademicYearStartMonth;
        var technicians = store.Technicians;
        var users = store.Users.ToDictionary(x => x.Id, x => x.Name);
        var all = store.Projects.Select(x => new ProjectRow(x, users.GetValueOrDefault(x.RequesterId, "Unknown"),
            x.TechnicianId is { } tech ? technicians.FirstOrDefault(t => t.Id == tech)?.Name ?? "Unknown" : null,
            x.ChosenTotals, HelpdeskStore.ProposalReadyAt(x))).ToList();

        var dates = all.Select(x => x.RaisedOn).Concat(all.Select(x => x.ClosedOn).OfType<DateOnly>());
        AvailableYears = AcademicYear.StartYearsFrom(dates, StartMonth, Today);
        if (string.Equals(Year, AllTime, StringComparison.OrdinalIgnoreCase)) StartYear = null;
        else StartYear = int.TryParse(Year, out var chosen) && AvailableYears.Contains(chosen) ? chosen : AcademicYear.StartYearOf(Today, StartMonth);
        PeriodLabel = StartYear is { } year ? $"Academic year {AcademicYear.Label(year, StartMonth)}" : "All time";
        bool InPeriod(DateOnly? day) => day is { } d && (StartYear is not { } y || AcademicYear.Contains(d, y, StartMonth));

        Open = all.Where(x => x.Project.IsActive)
            .OrderBy(x => x.Project.EffectivePriority).ThenBy(x => x.Project.DueDate).ThenBy(x => x.Project.Number).ToList();
        ByStatus = ProjectStatuses.All.Where(x => x != ProjectStatuses.Closed).Select(status =>
        {
            var rows = Open.Where(x => x.Project.Status == status).ToList();
            return new StatusRow(status, rows.Count, rows.Count(x => x.Project.IsOverdue(Today)), rows.Count(x => x.Project.IsDueSoon(Today)), rows.Sum(x => x.Totals.TermIncVat));
        }).ToList();

        Raised = all.Where(x => InPeriod(x.RaisedOn)).OrderBy(x => x.Project.Number).ToList();
        ProposalsMade = all.Where(x => InPeriod(x.ReadyOn)).OrderBy(x => x.ReadyAt).ToList();
        Closed = all.Where(x => InPeriod(x.ClosedOn)).OrderByDescending(x => x.Project.ClosedAt).ToList();
        ByOutcome = ProjectOutcomes.All.Select(outcome =>
        {
            var rows = Closed.Where(x => x.Project.Outcome == outcome).ToList();
            return new OutcomeRow(outcome, rows.Count, rows.Aggregate(new QuoteTotals(), (sum, x) => sum + x.Totals));
        }).ToList();

        // Everyone who can be given projects, plus anyone else who still holds one (moved role, left the team), so no
        // open project goes missing from the table. Proposals and closures are credited to whoever has the project now.
        var people = technicians.Where(store.CanWorkProjects).Select(x => x.Id)
            .Concat(all.Where(x => x.Project.IsActive || InPeriod(x.ReadyOn) || InPeriod(x.ClosedOn)).Select(x => x.Project.TechnicianId).OfType<Guid>())
            .Distinct().ToList();
        var workload = people.Select(id =>
        {
            var name = technicians.FirstOrDefault(x => x.Id == id)?.Name ?? "Unknown";
            return Row(name, false, all.Where(x => x.Project.TechnicianId == id).ToList(), InPeriod);
        }).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var unassigned = all.Where(x => x.Project.TechnicianId is null && x.Project.IsActive).ToList();
        if (unassigned.Count > 0) workload.Insert(0, Row("Not assigned yet", true, unassigned, InPeriod));
        Workload = workload;
    }

    private WorkloadRow Row(string name, bool isUnassigned, List<ProjectRow> projects, Func<DateOnly?, bool> inPeriod)
    {
        var counts = new int[ProjectPriorities.Lowest];
        foreach (var row in projects.Where(x => x.Project.IsActive))
            counts[Math.Clamp(row.Project.EffectivePriority, ProjectPriorities.Highest, ProjectPriorities.Lowest) - 1]++;
        var made = projects.Where(x => inPeriod(x.ReadyOn)).ToList();
        return new WorkloadRow(name, isUnassigned, counts,
            projects.Count(x => x.Project.IsOverdue(Today)),
            projects.Count(x => x.Project.Status == ProjectStatuses.ProposalReady),
            made.Count,
            Median(made.Select(x => x.DaysToProposal).OfType<int>()),
            projects.Count(x => inPeriod(x.ClosedOn)));
    }

    public static double? Median(IEnumerable<int> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return null;
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
    }

    public static string Days(double? value) => value is { } v ? $"{v.ToString("0.#", CultureInfo.InvariantCulture)} day{(v == 1 ? "" : "s")}" : "—";
    public string YearLabel(int startYear) => AcademicYear.Label(startYear, StartMonth);
    public string Money(decimal value) => HelpdeskStore.FormatMoney(value);

    public IActionResult OnGetExport(string? table)
    {
        if (!CanExport) return Forbid();
        OnGet();
        static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        static string Day(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "";
        var (name, csv) = (table ?? "").ToLowerInvariant() switch
        {
            "open" => ("projects-open", Csv.Table(
                ["Project", "Title", "Requester", "Technician", "Priority", "Priority confirmed", "Status", "Proposal needed by", "Overdue", "Raised", "Proposal first ready",
                 "Chosen quotes first year ex VAT", "Chosen quotes whole term ex VAT", "Chosen quotes whole term inc VAT", "Linked tickets"],
                Open,
                x => [x.Project.Reference, x.Project.Title, x.Requester, x.Technician ?? "", $"P{x.Project.EffectivePriority}", x.Project.Priority is null ? "No" : "Yes",
                      x.Project.Status, Day(x.Project.DueDate), x.Project.IsOverdue(Today) ? "Yes" : "No", Day(x.RaisedOn), Day(x.ReadyOn),
                      Amount(x.Totals.FirstYearExVat), Amount(x.Totals.TermExVat), Amount(x.Totals.TermIncVat), string.Join(" ", x.Project.TicketNumbers.Select(t => $"#{t}"))])),
            "workload" => ("projects-workload", Csv.Table(
                ["Technician", "Active", "P1", "P2", "P3", "P4", "P5", "Overdue", "Awaiting a decision", "Proposals made in period", "Median days to proposal", "Closed in period"],
                Workload,
                x => [x.Name, x.Active.ToString(), .. x.ByPriority.Select(c => c.ToString()), x.Overdue.ToString(), x.AwaitingDecision.ToString(),
                      x.ProposalsMade.ToString(), x.MedianDays?.ToString("0.#", CultureInfo.InvariantCulture) ?? "", x.Closed.ToString()])),
            "closed" => ("projects-closed", Csv.Table(
                ["Project", "Title", "Requester", "Technician", "Outcome", "Closed", "Raised", "Proposal first ready", "Days to proposal",
                 "Whole term ex VAT", "Whole term inc VAT", "First year inc VAT", "Outcome note"],
                Closed,
                x => [x.Project.Reference, x.Project.Title, x.Requester, x.Technician ?? "", x.Project.Outcome ?? "", Day(x.ClosedOn), Day(x.RaisedOn), Day(x.ReadyOn),
                      x.DaysToProposal?.ToString() ?? "", Amount(x.Totals.TermExVat), Amount(x.Totals.TermIncVat), Amount(x.Totals.FirstYearIncVat), x.Project.OutcomeNote ?? ""])),
            _ => ("", "")
        };
        if (name.Length == 0) return NotFound();
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName(name, DateTime.Now));
    }
}
