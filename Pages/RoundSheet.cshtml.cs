using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// A cleaning round's printed sheet for the day: its rooms with boxes to tick and initial, for a cleaner who doesn't
// sign in. The supervisor copies it back onto /Rounds afterwards.
public class RoundSheetModel(HelpdeskStore store) : PageModel
{
    public CleaningRound Round { get; private set; } = null!;
    public DateOnly Date { get; private set; }

    public IActionResult OnGet(Guid id, string? date)
    {
        if (store.GetCleaningRound(id) is not { } round) return NotFound();
        Round = round;
        Date = RoundsModel.ParseDate(date) ?? DateOnly.FromDateTime(DateTime.Now);
        return Page();
    }
}
