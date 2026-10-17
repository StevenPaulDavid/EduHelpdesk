using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Facilities → Premises checks: the checks to do now - each a ticket with its checklist (HelpdeskStore.Checks) - what
// is coming up in the next month, and how each check went last time. The yearly log is Reports → Premises checks.
// Follows the desk switcher, and shows only checks on tickets this person may see.
public class ChecksModel(HelpdeskStore store) : PageModel
{
    public sealed record Open(TicketRecord Ticket, string Ref, string Name, int Answered, int Lines, string? Technician);
    public sealed record Coming(RecurringTicket Schedule, DateTime When, string Repeats);
    public sealed record Last(string Name, TicketRecord Ticket, string Ref, int Failed, bool Late);

    public DateTime Now { get; } = DateTime.UtcNow;
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public IReadOnlyList<Open> Overdue { get; private set; } = [];
    public IReadOnlyList<Open> DueToday { get; private set; } = [];
    public IReadOnlyList<Open> DueLater { get; private set; } = [];
    public IReadOnlyList<Coming> ThisWeek { get; private set; } = [];
    public IReadOnlyList<Coming> ThisMonth { get; private set; } = [];
    public IReadOnlyList<Last> LastResults { get; private set; } = [];
    public bool HasSchedules { get; private set; }
    public bool CanReport => store.UserHasFlag(User, Modules.Flags.ReportChecks);
    public bool CanSetUp => store.CanSetUpFacilities(User);

    public void OnGet()
    {
        var working = store.WorkingDesks(User).Select(x => x.Id).ToHashSet();
        var deskOf = store.DeskLookup();
        bool OnDesks(string category) => working.Count == 0 || working.Contains(deskOf(category).Id);
        var tickets = store.TicketsFor(User).Where(t => OnDesks(t.Category)).ToDictionary(x => x.Number);
        var lines = store.CheckLines.Where(x => tickets.ContainsKey(x.TicketNumber)).GroupBy(x => x.TicketNumber).ToList();
        var names = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
        string Ref(TicketRecord ticket) => HelpdeskStore.RefFor(deskOf(ticket.Category), ticket);

        // To do: open checks, the most overdue first.
        var open = lines.Where(g => !TicketInsights.IsClosed(tickets[g.Key]))
            .Select(g =>
            {
                var ticket = tickets[g.Key];
                return new Open(ticket, Ref(ticket), g.First().ScheduleName, g.Count(x => x.AnsweredAt is not null), g.Count(),
                    ticket.TechnicianId is { } id ? names.GetValueOrDefault(id) : ticket.TeamName);
            })
            .OrderBy(x => x.Ticket.DueDate ?? DateTime.MaxValue).ToList();
        var endOfToday = DateTime.Today.AddDays(1).ToUniversalTime();
        Overdue = open.Where(x => x.Ticket.DueDate is { } due && due < Now).ToList();
        DueToday = open.Where(x => x.Ticket.DueDate is { } due && due >= Now && due < endOfToday).ToList();
        DueLater = open.Except(Overdue).Except(DueToday).ToList();

        // Coming up: when each active checklist schedule on these desks next logs its check, within a month.
        var schedules = store.RecurringTickets.Where(x => x.Checklist.Count > 0 && OnDesks(x.Category)).ToList();
        HasSchedules = schedules.Count > 0;
        var coming = schedules.Where(x => x.IsActive)
            .Select(x => (Schedule: x, When: store.NextRecurringDue(x)))
            .Where(x => x.When is { } when && DateOnly.FromDateTime(when) <= Today.AddDays(31))
            .Select(x => new Coming(x.Schedule, x.When!.Value, Recurrence.Describe(x.Schedule.Rule)))
            .OrderBy(x => x.When).ToList();
        var weekEnd = Today.AddDays(7 - ((int)Today.DayOfWeek + 6) % 7);
        ThisWeek = coming.Where(x => DateOnly.FromDateTime(x.When) < weekEnd).ToList();
        ThisMonth = coming.Except(ThisWeek).ToList();

        // How each check went the last time it was closed.
        LastResults = lines.Where(g => TicketInsights.IsClosed(tickets[g.Key]))
            .GroupBy(g => g.First().ScheduleId)
            .Select(s =>
            {
                var latest = s.OrderByDescending(g => tickets[g.Key].ClosedAt).First();
                var ticket = tickets[latest.Key];
                return new Last(latest.First().ScheduleName, ticket, Ref(ticket), latest.Count(x => x.Result == HelpdeskStore.CheckFail),
                    ticket.DueDate is { } due && ticket.ClosedAt > due);
            })
            .OrderByDescending(x => x.Failed > 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string DueText(TicketRecord ticket) => TicketInsights.DueText(ticket, Now);
}
