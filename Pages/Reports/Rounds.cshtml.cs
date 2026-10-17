using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// The cleaning rounds report (HelpdeskStore.Rounds): rooms missed, and spot-check scores, over a span of days. Days a
// round was due but nothing at all was recorded are listed on their own rather than counted as every room missed - the
// helpdesk doesn't know term dates, so most of them will be holidays, and the rest a supervisor who was off.
public class RoundsReportModel(HelpdeskStore store) : PageModel
{
    public const int DefaultDays = 28;
    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);
    [BindProperty(SupportsGet = true, Name = "from")] public string? FromText { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? ToText { get; set; }
    [BindProperty(SupportsGet = true, Name = "round")] public Guid? RoundId { get; set; }

    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public IReadOnlyList<CleaningRound> AllRounds { get; private set; } = [];
    public IReadOnlyList<HelpdeskStore.RoundDay> Days { get; private set; } = [];

    public sealed record RoundLine(CleaningRound Round, int Due, int Recorded, int RoomsDue, int RoomsDone, double? AverageScore, int Scores)
    {
        public int NotRecorded => Due - Recorded;
        public int Missed => RoomsDue - RoomsDone;
        public int? PercentDone => RoomsDue == 0 ? null : (int)Math.Round(100.0 * RoomsDone / RoomsDue);
    }
    public sealed record MissedRoom(string Room, string Round, int Times, int Of, DateOnly Last, IReadOnlyList<string> Notes);
    public sealed record RoomScore(string Room, string Round, double Average, int Count, int Lowest, IReadOnlyList<string> Notes);

    public IReadOnlyList<RoundLine> ByRound { get; private set; } = [];
    public IReadOnlyList<MissedRoom> Missed { get; private set; } = [];
    public IReadOnlyList<HelpdeskStore.RoundDay> Unrecorded { get; private set; } = [];
    public IReadOnlyList<RoomScore> Scores { get; private set; } = [];
    public string Span => $"{From:ddd d MMM yyyy} to {To:ddd d MMM yyyy}";

    public void OnGet()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        To = EduHelpdesk.Pages.RoundsModel.ParseDate(ToText) is { } to && to <= today ? to : today;
        From = EduHelpdesk.Pages.RoundsModel.ParseDate(FromText) is { } from && from <= To ? from : To.AddDays(1 - DefaultDays);
        AllRounds = store.CleaningRounds;
        if (RoundId is { } chosen && AllRounds.All(x => x.Id != chosen)) RoundId = null;
        Days = store.RoundDays(From, To).Where(x => RoundId is null || x.Round.Id == RoundId).ToList();
        Unrecorded = Days.Where(x => !x.Recorded).ToList();
        var recorded = Days.Where(x => x.Recorded).ToList();
        var visits = Days.SelectMany(x => x.Visits.Select(v => (Day: x, Visit: v))).ToList();

        ByRound = Days.GroupBy(x => x.Round.Id).Select(g =>
        {
            var round = g.First().Round;
            var done = g.Where(x => x.Recorded).ToList();
            var scored = g.SelectMany(x => x.Visits).Where(x => x.Score is not null).ToList();
            return new RoundLine(round, g.Count(), done.Count, done.Sum(x => x.Round.Rooms.Count), done.Sum(x => x.Round.Rooms.Count - x.Missed.Count),
                scored.Count == 0 ? null : scored.Average(x => x.Score!.Value), scored.Count);
        }).OrderBy(x => x.Round.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Missed = recorded.SelectMany(d => d.Missed.Select(room => (d, room)))
            .GroupBy(x => (x.room.ToLowerInvariant(), x.d.Round.Id))
            .Select(g =>
            {
                var first = g.First();
                var of = recorded.Count(x => x.Round.Id == first.d.Round.Id && x.Round.Rooms.Contains(first.room, StringComparer.OrdinalIgnoreCase));
                var notes = g.Select(x => x.d.Visits.FirstOrDefault(v => string.Equals(v.Room, x.room, StringComparison.OrdinalIgnoreCase))?.Note)
                    .Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
                return new MissedRoom(first.room, first.d.Round.Name, g.Count(), of, g.Max(x => x.d.Date), notes);
            })
            .OrderByDescending(x => x.Times).ThenBy(x => x.Room, NaturalComparer.Instance).ToList();

        Scores = visits.Where(x => x.Visit.Score is not null)
            .GroupBy(x => (x.Visit.Room.ToLowerInvariant(), x.Day.Round.Id))
            .Select(g => new RoomScore(g.First().Visit.Room, g.First().Day.Round.Name, g.Average(x => x.Visit.Score!.Value), g.Count(), g.Min(x => x.Visit.Score!.Value),
                g.Select(x => x.Visit.ScoreNote).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(x => x.Average).ThenBy(x => x.Room, NaturalComparer.Instance).ToList();
    }

    public int RoomsDue => ByRound.Sum(x => x.RoomsDue);
    public int RoomsDone => ByRound.Sum(x => x.RoomsDone);
    public double? AverageScore => ByRound.Sum(x => x.Scores) == 0 ? null : ByRound.Sum(x => (x.AverageScore ?? 0) * x.Scores) / ByRound.Sum(x => x.Scores);
    public static string Score(double? value) => value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "-";
    public string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // One row per room per day it was due, nothing-recorded days included, so the spreadsheet can be cut any way.
    public IActionResult OnGetExport()
    {
        if (!CanExport) return Forbid();
        OnGet();
        var rows = Days.SelectMany(d => d.Round.Rooms.Select(room => (d, room, visit: d.Visits.FirstOrDefault(v => string.Equals(v.Room, room, StringComparison.OrdinalIgnoreCase)))));
        var csv = Csv.Table(["Date", "Round", "Room", "Recorded", "Done", "Cleaner", "Note", "Spot-check score", "Spot-check note", "Recorded by"], rows,
            x => [Iso(x.d.Date), x.d.Round.Name, x.room, x.d.Recorded ? "Yes" : "No", !x.d.Recorded ? "" : x.visit?.Done == true ? "Yes" : "No", x.visit?.Cleaner ?? "",
                  x.visit?.Note ?? "", x.visit?.Score?.ToString(CultureInfo.InvariantCulture) ?? "", x.visit?.ScoreNote ?? "", x.visit?.By?.Name ?? ""]);
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName("cleaning-rounds", DateTime.Now));
    }
}
