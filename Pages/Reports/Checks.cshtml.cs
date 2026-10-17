using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// The premises compliance log (HelpdeskStore.Checks): every check done in an academic year, by recurring ticket, with
// when it was done, by whom, on time or not, and what failed - for governors, insurers and an inspection. Only checks
// on tickets this person may see (HelpdeskStore.Desks).
public class ChecksReportModel(HelpdeskStore store) : PageModel
{
    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);
    [BindProperty(SupportsGet = true, Name = "year")] public int? Year { get; set; }

    public sealed record Check(TicketRecord Ticket, string Ref, IReadOnlyList<CheckLine> Lines, DateTime? DoneAt, string DoneBy)
    {
        public int Failed => Lines.Count(x => x.Failed);
        public int ToDo => Lines.Count(x => !x.Answered);
        public bool Closed => TicketInsights.IsClosed(Ticket);
        public bool Late => Ticket.DueDate is { } due && (DoneAt ?? DateTime.UtcNow) > due;
        public string Result => !Closed ? (ToDo > 0 ? $"In progress, {ToDo} to do" : "Filled in, not closed") : Failed > 0 ? $"{Failed} failed" : "All passed";
    }
    public sealed record Schedule(string Name, IReadOnlyList<Check> Checks)
    {
        public int Done => Checks.Count(x => x.Closed);
        public int OnTime => Checks.Count(x => x.Closed && !x.Late);
        public int WithFailures => Checks.Count(x => x.Failed > 0);
    }

    public IReadOnlyList<int> Years { get; private set; } = [];
    public int StartMonth { get; private set; }
    public string YearLabel => AcademicYear.Label(Year!.Value, StartMonth);
    public IReadOnlyList<Schedule> Schedules { get; private set; } = [];

    public void OnGet()
    {
        StartMonth = store.AcademicYearStartMonth;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var deskOf = store.DeskLookup();
        var tickets = store.TicketsFor(User).ToDictionary(x => x.Number);
        var lines = store.CheckLines.Where(x => tickets.ContainsKey(x.TicketNumber)).ToList();
        Years = AcademicYear.StartYearsFrom(lines.Select(x => DateOnly.FromDateTime(tickets[x.TicketNumber].CreatedAt.ToLocalTime())), StartMonth, today);
        Year = Year is { } asked && Years.Contains(asked) ? asked : AcademicYear.StartYearOf(today, StartMonth);
        Schedules = lines
            .Where(x => AcademicYear.Contains(DateOnly.FromDateTime(tickets[x.TicketNumber].CreatedAt.ToLocalTime()), Year.Value, StartMonth))
            .GroupBy(x => x.ScheduleId)
            .Select(g =>
            {
                var checks = g.GroupBy(x => x.TicketNumber).Select(t =>
                {
                    var ticket = tickets[t.Key];
                    var ordered = t.OrderBy(x => x.Position).ToList();
                    var lastBy = ordered.Where(x => x.AnsweredAt is not null).OrderByDescending(x => x.AnsweredAt).FirstOrDefault()?.By?.Name ?? "";
                    return new Check(ticket, HelpdeskStore.RefFor(deskOf(ticket.Category), ticket), ordered, ticket.ClosedAt, lastBy);
                }).OrderBy(x => x.Ticket.CreatedAt).ToList();
                // The name it had when last done, in case the schedule has been renamed since.
                return new Schedule(checks[^1].Lines[0].ScheduleName, checks);
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // A line as it reads in the log: "55 °C (pass)", "Fail - stuck", "Flushed taps".
    public static string Recorded(CheckLine line) =>
        (line.Kind == CheckKinds.Reading && line.Reading is { } value ? $"{value.ToString("0.##", CultureInfo.InvariantCulture)}{(line.Unit.Length > 0 ? " " + line.Unit : "")} " : "")
        + (line.Result switch { HelpdeskStore.CheckPass => line.Kind == CheckKinds.Reading ? "(pass)" : "Pass", HelpdeskStore.CheckFail => line.Kind == CheckKinds.Reading ? "(fail)" : "Fail", _ => line.Kind == CheckKinds.Note ? "" : "Not done" })
        + (line.Note.Length > 0 ? $"{(line.Kind == CheckKinds.Note ? "" : " - ")}{line.Note}" : "");

    public IActionResult OnGetExport()
    {
        if (!CanExport) return Forbid();
        OnGet();
        var rows = Schedules.SelectMany(s => s.Checks.SelectMany(c => c.Lines.Select(l => (s, c, l))));
        var csv = Csv.Table(["Check", "Ticket", "Logged", "Due", "Done", "Done by", "Line", "Limits", "Recorded", "Result"], rows,
            x => [x.s.Name, x.c.Ref, x.c.Ticket.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), x.c.Ticket.DueDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "",
                  x.c.DoneAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", x.c.DoneBy, x.l.Label, HelpdeskStore.DescribeLimits(x.l.Min, x.l.Max, x.l.Unit), Recorded(x.l), x.l.Result]);
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName("premises-checks", DateTime.Now));
    }
}
