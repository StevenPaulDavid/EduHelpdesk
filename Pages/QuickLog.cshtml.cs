using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// "Log a job already done": for the spill radioed in and cleaned up before anyone reached a computer. A few boxes, saved
// as a closed ticket (HelpdeskStore.LogDoneJob) so the work is on the record and in the reports. Needs Tickets: New.
public class QuickLogModel(HelpdeskStore store) : PageModel
{
    // Only the desks this person sees, and only those desks' categories and people.
    public IReadOnlyList<Desk> Desks => store.DesksVisibleTo(User);
    public IReadOnlyList<(Desk Desk, IReadOnlyList<string> Categories)> CategoriesByDesk =>
        Desks.Select(d => (d, store.CategoriesFor(d.Id))).Where(x => x.Item2.Count > 0).ToList();
    public IReadOnlyList<TechnicianRecord> People => store.Technicians.Where(x => x.IsActive && Desks.Any(d => HelpdeskStore.TechnicianOnDesk(x, d.Id))).OrderBy(x => x.Name).ToList();
    public IReadOnlyList<string> Locations => store.Locations;
    public Guid? Me => WorkingAs.SignedInTechnicianId(store, User);

    [BindProperty] public string? Category { get; set; }
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public string? Location { get; set; }
    [BindProperty] public Guid? TechnicianId { get; set; }
    [BindProperty] public DateTime? At { get; set; }
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        // Starts on the first category of the desk being worked on (or their own first desk), with themselves as who
        // dealt with it, now.
        Category = store.StartingDeskFor(User) is { } starting ? store.CategoriesFor(starting.Id).FirstOrDefault() : CategoriesByDesk.FirstOrDefault().Categories?.FirstOrDefault();
        TechnicianId = Me;
        At = DateTime.Now;
    }

    public IActionResult OnPost(bool another)
    {
        if (Me is not { } me) { ModelState.AddModelError("", "Only a staff account can log jobs."); return Page(); }
        // A category on a desk they can't see is refused like one that doesn't exist.
        if (Category is not null && !CategoriesByDesk.Any(x => x.Categories.Contains(Category, StringComparer.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError("", "Choose what kind of job it was.");
            return Page();
        }
        var (number, error) = store.LogDoneJob(me, new HelpdeskStore.DoneJob(Category, Title, Notes, Location, TechnicianId, At));
        if (number is null)
        {
            ModelState.AddModelError("", error ?? "The job couldn't be logged.");
            return Page();
        }
        Message = $"Logged {store.Ref(number.Value)} as dealt with.";
        // "Save and log another" comes back to a fresh form for the next one off the radio.
        return another ? RedirectToPage() : RedirectToPage("/Job", new { number });
    }
}
