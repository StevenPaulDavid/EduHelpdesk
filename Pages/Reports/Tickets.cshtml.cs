using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

public class TicketReportsModel(HelpdeskStore store) : PageModel
{
    public const int RowLimit = 15;
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
        OverdueNow = open.Count(t => t.DueDate is { } due && due < Now);
        OverdueOpen = open.Where(t => t.DueDate is { } due && due < Now).OrderBy(t => t.DueDate).Take(RowLimit).ToList();

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
