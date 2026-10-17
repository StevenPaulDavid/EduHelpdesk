using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The cleaning supervisor's page (HelpdeskStore.Rounds): a day's rounds, each with its rooms to tick off - done or not,
// who cleaned it, a note, and an optional spot-check score. Cleaners work from the printed sheet (/RoundSheet).
public class RoundsModel(HelpdeskStore store) : PageModel
{
    public DateOnly Date { get; private set; }
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public IReadOnlyList<CleaningRound> Due { get; private set; } = [];
    public IReadOnlyList<string> Cleaners => store.Cleaners;
    public bool HasRounds => store.CleaningRounds.Any(x => x.IsActive);
    public bool CanReport => store.UserHasFlag(User, Modules.Flags.ReportRounds);
    [TempData] public string? Message { get; set; }

    public sealed class VisitForm
    {
        public string Room { get; set; } = "";
        public bool Done { get; set; }
        public string? Cleaner { get; set; }
        public string? Note { get; set; }
        public int? Score { get; set; }
        public string? ScoreNote { get; set; }
    }

    public IReadOnlyList<RoundVisit> VisitsFor(CleaningRound round) => store.VisitsFor(round.Id, Date);
    public string Who(RoundVisit visit) => visit.By?.Name ?? "";

    public void OnGet(string? date)
    {
        Date = ParseDate(date) ?? Today;
        if (Date > Today) Date = Today;
        Due = store.RoundsDueOn(Date);
    }

    public IActionResult OnPost(Guid id, string? date, List<VisitForm>? v)
    {
        var day = ParseDate(date) ?? Today;
        var rooms = (v ?? []).Select(x => new HelpdeskStore.VisitInput(x.Room, x.Done, x.Cleaner, x.Note, x.Score, x.ScoreNote));
        Message = store.RecordRound(id, day, rooms).Message;
        return RedirectToPage(null, null, new { date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, $"round-{id}");
    }

    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
}
