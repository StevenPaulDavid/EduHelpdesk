using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

public class TicketReportsModel(HelpdeskStore store) : PageModel
{
    // Taking CSV and print away is a separate decision from taking the report away, so the download links and the print
    // button hang off this rather than off the report's own flag.
    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);
    // Top-N shortlists (most tickets, repeat faults, top requesters). Deliberately the same on screen and on paper:
    // these are rankings, and a printed list of every requester would be noise rather than information.
    public const int RowLimit = 15;
    // The overdue list is a truncation rather than a ranking, so the print view and the CSV export lift it.
    public virtual int OverdueLimit => _exportingEverything ? int.MaxValue : 15;
    // Days, or null for all time. Months are worked out from today, not counted as 30 days.
    public static readonly (string Key, string Label, int? Days, int? Months)[] Periods =
    [
        ("30d", "Last 30 days", 30, null), ("3m", "Last 3 months", null, 3), ("6m", "Last 6 months", null, 6), ("12m", "Last 12 months", null, 12), ("all", "All time", null, null)
    ];

    [BindProperty(SupportsGet = true, Name = "period")] public string? Period { get; set; }
    // Incident, Request, or blank for both.
    [BindProperty(SupportsGet = true, Name = "type")] public string? Type { get; set; }

    public DateTime Now { get; } = DateTime.UtcNow;
    public string PeriodKey { get; private set; } = "3m";
    public string PeriodLabel { get; private set; } = "";
    // "in the last 3 months" or "over all time", for sentences.
    public string WhenText => PeriodKey == "all" ? "over all time" : "in the " + PeriodLabel.ToLowerInvariant();
    public string TypeLabel => string.IsNullOrEmpty(Type) ? "Incidents and requests" : Type == TicketTypes.Request ? "Requests only" : "Incidents only";
    public DateTime Since { get; private set; }
    public bool HasPreviousPeriod { get; private set; }


    public int TicketsInPeriod { get; private set; }
    public TicketReports.SlaRow Overall { get; private set; } = new("", 0, 0, 0, 0, 0, 0, 0, null, null);
    public int OpenNow { get; private set; }
    public int OverdueNow { get; private set; }
    public IReadOnlyList<TicketReports.SlaRow> ByType { get; private set; } = [];
    public IReadOnlyList<TicketReports.SlaRow> ByPriority { get; private set; } = [];
    public IReadOnlyList<TicketReports.SlaRow> ByCategory { get; private set; } = [];
    public IReadOnlyList<TicketReports.SlaRow> ByTechnician { get; private set; } = [];
    public IReadOnlyList<TicketRecord> OverdueOpen { get; private set; } = [];
    public IReadOnlyList<TicketReports.WorkloadRow> Workload { get; private set; } = [];
    public IReadOnlyList<TicketReports.TeamRow> Teams { get; private set; } = [];
    public IReadOnlyList<TicketReports.AssetRow> Assets { get; private set; } = [];
    public IReadOnlyList<TicketReports.RepeatFaultRow> RepeatFaults { get; private set; } = [];
    public IReadOnlyList<TicketReports.RequesterRow> Requesters { get; private set; } = [];
    public IReadOnlyList<TicketReports.CategoryRow> Categories { get; private set; } = [];


    // One CSV per table. The four SLA groupings share a shape, so they come out as one tidy file with a Grouping
    // column rather than four near-identical downloads.
    public IActionResult OnGetExport(string? table)
    {
        if (!CanExport) return Forbid();
        _exportingEverything = true;
        OnGet();
        var (name, csv) = (table ?? "").ToLowerInvariant() switch
        {
            "sla" => ("ticket-sla", Csv.Table(
                ["Grouping", "Name", "Tickets", "On time", "Late", "Open and overdue", "Open within time", "No due date", "Closed", "On-time rate %", "Average resolution (hours)", "Median resolution (hours)"],
                new[] { ("Overall", Overall) }
                    .Concat(ByType.Select(x => ("Type", x))).Concat(ByPriority.Select(x => ("Priority", x)))
                    .Concat(ByCategory.Select(x => ("Category", x))).Concat(ByTechnician.Select(x => ("Technician", x))),
                x => [x.Item1, x.Item2.Name, x.Item2.Tickets.ToString(), x.Item2.OnTime.ToString(), x.Item2.Late.ToString(), x.Item2.OpenOverdue.ToString(),
                      x.Item2.OpenWithinTime.ToString(), x.Item2.NoDueDate.ToString(), x.Item2.Closed.ToString(), Number(x.Item2.OnTimeRate), Number(x.Item2.AverageResolutionHours), Number(x.Item2.MedianResolutionHours)])),
            "overdue" => ("ticket-overdue", Csv.Table(
                ["Ticket", "Title", "Type", "Priority", "Status", "Category", "Due (local)"],
                OverdueOpen,
                x => [x.Number.ToString(), x.Title, x.Type, x.Priority, x.Status, x.Category, x.DueDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? ""])),
            "workload" => ("ticket-workload", Csv.Table(
                ["Technician", "Team", "Open", "Open incidents", "Open requests", "Overdue", "Closed in period", "Oldest open (local)", "Average resolution (hours)"],
                Workload,
                x => [x.Name, x.Team, x.Open.ToString(), x.OpenIncidents.ToString(), x.OpenRequests.ToString(), x.Overdue.ToString(), x.ClosedInPeriod.ToString(),
                      x.OldestOpen?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", Number(x.AverageResolutionHours)])),
            "teams" => ("ticket-teams", Csv.Table(
                ["Team", "Technicians", "Open", "Overdue", "Closed in period"],
                Teams,
                x => [x.Team, x.Technicians.ToString(), x.Open.ToString(), x.Overdue.ToString(), x.ClosedInPeriod.ToString()])),
            "assets" => ("ticket-assets", Csv.Table(
                ["Asset tag", "Type", "Make", "Model", "Tickets", "Incidents", "Open", "Most recent (local)"],
                Assets,
                x => [x.Asset.AssetTag, x.Asset.Type, x.Asset.Make, x.Asset.Model, x.Tickets.ToString(), x.Incidents.ToString(), x.Open.ToString(), x.LastTicket.ToLocalTime().ToString("yyyy-MM-dd HH:mm")])),
            "repeat-faults" => ("ticket-repeat-faults", Csv.Table(
                ["Asset tag", "Type", "Category", "Tickets", "First (local)", "Last (local)"],
                RepeatFaults,
                x => [x.Asset.AssetTag, x.Asset.Type, x.Category, x.Tickets.ToString(), x.First.ToLocalTime().ToString("yyyy-MM-dd"), x.Last.ToLocalTime().ToString("yyyy-MM-dd")])),
            "requesters" => ("ticket-requesters", Csv.Table(
                ["Person", "Email", "Department", "Location", "Tickets", "Open", "Most recent (local)"],
                Requesters,
                x => [x.User.Name, x.User.Email, x.User.Department, x.User.Location, x.Tickets.ToString(), x.Open.ToString(), x.LastTicket.ToLocalTime().ToString("yyyy-MM-dd")])),
            "categories" => ("ticket-categories", Csv.Table(
                ["Category", "Tickets", "Share %", "Previous period"],
                Categories,
                x => [x.Category, x.Tickets.ToString(), x.SharePercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), x.Previous.ToString()])),
            _ => ("", "")
        };
        if (name.Length == 0) return NotFound();
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName(name, DateTime.Now));
    }

    private bool _exportingEverything;
    private static string Number(double? value) => value?.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public void OnGet()
    {
        var period = Periods.FirstOrDefault(x => x.Key == Period);
        if (period.Key is null) period = Periods[1];
        PeriodKey = period.Key;
        PeriodLabel = period.Label;
        Type = TicketTypes.All.FirstOrDefault(x => string.Equals(x, Type?.Trim(), StringComparison.OrdinalIgnoreCase));
        Since = period.Days is { } days ? Now.AddDays(-days) : period.Months is { } months ? Now.AddMonths(-months) : DateTime.MinValue;
        HasPreviousPeriod = period.Key != "all";

        var all = store.Tickets.Where(t => Type is null || t.Type == Type).ToList();
        var cohort = all.Where(t => t.CreatedAt >= Since).ToList();
        var previous = HasPreviousPeriod ? all.Where(t => t.CreatedAt < Since && t.CreatedAt >= Since - (Now - Since)).ToList() : [];
        var users = store.Users;
        var technicians = store.Technicians;
        var technicianNames = technicians.ToDictionary(x => x.Id, x => x.Name);

        TicketsInPeriod = cohort.Count;
        Overall = TicketReports.Sla("All tickets", cohort, Now);
        var open = all.Where(t => !TicketInsights.IsClosed(t)).ToList();
        OpenNow = open.Count;
        OverdueNow = open.Count(t => TicketInsights.IsOverdue(t, Now));
        OverdueOpen = open.Where(t => TicketInsights.IsOverdue(t, Now)).OrderBy(t => t.DueDate).Take(OverdueLimit).ToList();

        ByType = TicketReports.SlaBy(cohort, t => t.Type, Now);
        ByPriority = TicketReports.SlaBy(cohort, t => t.Priority, Now);
        ByCategory = TicketReports.SlaBy(cohort, t => t.Category, Now);
        ByTechnician = TicketReports.SlaBy(cohort, t => t.TechnicianId is { } id && technicianNames.TryGetValue(id, out var name) ? name : "Unassigned", Now);

        Workload = TicketReports.Workload(all, technicians, Now, Since);
        Teams = TicketReports.WorkloadByTeam(all, technicians, Now, Since);

        Assets = TicketReports.AssetsWithMostTickets(cohort, store.Assets, RowLimit);
        RepeatFaults = TicketReports.RepeatFaults(cohort, store.Assets, RowLimit);
        Requesters = TicketReports.RequestersWithMostTickets(cohort, users, RowLimit);
        Categories = TicketReports.Categories(cohort, previous);

    }

    public static string Percent(double? value) => value.HasValue ? $"{value.Value:0}%" : "—";
    public static string Change(int current, int previous) => current == previous ? "no change" : current > previous ? $"up {current - previous}" : $"down {previous - current}";
}
