using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Onboarding;

// The Onboarding menu: every new starter's onboarding, in tabs like the ticket queues, with a search.
// - active: still to finish, soonest start date first;
// - upcoming: still to finish and not started yet;
// - overdue: still to finish, with a task past its due date;
// - it: still to finish, with IT tasks left - what the technicians' Onboarding queue shows;
// - finished: every task done, or cancelled, most recent start first.
public class IndexModel(HelpdeskStore store) : PageModel
{
    public static readonly string[] Views = ["active", "upcoming", "overdue", "it", "finished"];

    public sealed record Row(OnboardingRecord Record, TicketRecord? Ticket, UserRecord? Starter)
    {
        public bool IsFinished => Record.IsCancelled || Record.AllDone;
    }

    [BindProperty(SupportsGet = true, Name = "view")] public string View { get; set; } = "active";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public IReadOnlyDictionary<string, int> ViewCounts { get; private set; } = new Dictionary<string, int>();
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public bool CanStart => store.UserCan(User, Modules.Onboarding, ModulePermission.New);
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        if (!Views.Contains(View)) View = "active";
        var tickets = store.Tickets.ToDictionary(x => x.Number);
        var users = store.Users.ToDictionary(x => x.Id);
        var all = store.Onboardings.Select(x => new Row(x, tickets.GetValueOrDefault(x.TicketNumber), users.GetValueOrDefault(x.StarterId))).ToList();
        ViewCounts = Views.ToDictionary(view => view, view => all.Count(row => InView(row, view)));
        var matches = all.Where(row => InView(row, View) && Matches(row));
        Rows = (View == "finished"
            ? matches.OrderByDescending(x => x.Record.StartDate)
            : matches.OrderBy(x => x.Record.StartDate)).ThenBy(x => x.Record.TicketNumber).ToList();
    }

    private bool InView(Row row, string view) => view switch
    {
        "finished" => row.IsFinished,
        "upcoming" => !row.IsFinished && row.Record.StartDate > Today,
        "overdue" => !row.IsFinished && row.Record.OverdueCount(Today) > 0,
        "it" => !row.IsFinished && row.Record.ItTasks.Any(x => !x.IsDone),
        _ => !row.IsFinished
    };

    // Every word somewhere in the name, email, job title, department or template.
    private bool Matches(Row row)
    {
        var words = (Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = string.Join(" ", row.Starter?.Name, row.Starter?.Email, row.Record.JobTitle, row.Starter?.Department, row.Record.TemplateName, $"#{row.Record.TicketNumber}");
        return words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static string ViewLabel(string view) => view switch
    {
        "upcoming" => "Not started yet",
        "overdue" => "Overdue tasks",
        "it" => "IT tasks left",
        "finished" => "Finished",
        _ => "In progress"
    };

    public string? ViewUrl(string view) => Url.Page("/Onboarding/Index", new { view = view == "active" ? null : view, q = string.IsNullOrWhiteSpace(Search) ? null : Search });

    public static string Starts(DateOnly start, DateOnly today) => (start.DayNumber - today.DayNumber) switch
    {
        0 => "Starts today",
        1 => "Starts tomorrow",
        > 1 and var days => $"Starts in {days} days",
        -1 => "Started yesterday",
        var days => $"Started {-days} days ago"
    };
}
