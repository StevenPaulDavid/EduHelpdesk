using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Onboarding;

// The Onboarding menu: every new starter's onboarding, the ones still in progress first, soonest start date first.
public class IndexModel(HelpdeskStore store) : PageModel
{
    public sealed record Row(OnboardingRecord Record, TicketRecord? Ticket, UserRecord? Starter)
    {
        public bool IsFinished => Record.IsCancelled || Record.AllDone;
    }

    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public bool CanStart => store.UserCan(User, Modules.Onboarding, ModulePermission.New);
    [Microsoft.AspNetCore.Mvc.TempData] public string? Message { get; set; }

    public void OnGet()
    {
        var tickets = store.Tickets.ToDictionary(x => x.Number);
        var users = store.Users.ToDictionary(x => x.Id);
        Rows = store.Onboardings
            .Select(x => new Row(x, tickets.GetValueOrDefault(x.TicketNumber), users.GetValueOrDefault(x.StarterId)))
            .OrderBy(x => x.IsFinished)
            .ThenBy(x => x.IsFinished ? DateOnly.MaxValue.DayNumber - x.Record.StartDate.DayNumber : x.Record.StartDate.DayNumber)
            .ToList();
    }

    public static string Starts(DateOnly start, DateOnly today) => (start.DayNumber - today.DayNumber) switch
    {
        0 => "Starts today",
        1 => "Starts tomorrow",
        > 1 and var days => $"Starts in {days} days",
        -1 => "Started yesterday",
        var days => $"Started {-days} days ago"
    };
}
