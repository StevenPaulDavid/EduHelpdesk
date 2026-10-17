using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Cleaning rounds: rooms (from Locations) grouped into rounds, the weekdays each is done, and its usual
// cleaner (HelpdeskStore.Rounds). The supervisor ticks them off on /Rounds.
public class RoundsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<CleaningRound> Rounds => store.CleaningRounds;
    public IReadOnlyList<string> Rooms => store.Locations.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x, NaturalComparer.Instance).ToList();
    public IReadOnlyList<string> Cleaners => store.Cleaners;
    public static IReadOnlyList<DayOfWeek> Week => DesksModel.Week;
    // Rooms already on another round, so a room left out (or put on two) is easy to spot.
    public ILookup<string, string> RoundsByRoom => store.CleaningRounds.SelectMany(r => r.Rooms.Select(room => (room, r.Name))).ToLookup(x => x.room, x => x.Name, StringComparer.OrdinalIgnoreCase);
    [TempData] public string? Message { get; set; }

    public static string DaysText(CleaningRound round) =>
        round.Days.Count == 5 && !round.Days.Contains(DayOfWeek.Saturday) && !round.Days.Contains(DayOfWeek.Sunday) ? "Mon to Fri"
        : round.Days.Count == 7 ? "Every day" : string.Join(", ", round.Days.Select(x => x.ToString()[..3]));

    public IActionResult OnPostSave(Guid? id, string? name, string[]? rooms, DayOfWeek[]? days, string? cleaner, bool active, string? startsOn)
    {
        Message = store.SaveCleaningRound(id, name, rooms, days, cleaner, id is null || active, EduHelpdesk.Pages.RoundsModel.ParseDate(startsOn)).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        Message = store.DeleteCleaningRound(id).Message;
        return RedirectToPage();
    }
}
