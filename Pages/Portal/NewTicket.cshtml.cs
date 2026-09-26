using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// The short version of /NewTicket for the staff portal: no requester/technician/team/SLA/asset pickers - just what
// happened, where, and roughly what kind of problem it is. Priority and type are set server-side.
public class NewTicketModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Locations => store.Locations;
    // Nullable on purpose: a non-nullable string property is treated as [Required], which added "The Description field
    // is required." to a box the form calls optional - and failed the ticket. The handler does its own checks.
    [BindProperty] public string? Title { get; set; } = "";
    [BindProperty] public string? Description { get; set; } = "";
    [BindProperty] public string Category { get; set; } = "";
    [BindProperty] public string? Location { get; set; }
    // "Report it again": the requester's own closed ticket this one follows on from. It is linked to it once raised.
    [BindProperty(SupportsGet = true)] public int? Again { get; set; }
    public TicketRecord? Previous { get; private set; }

    public IActionResult OnGet()
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == id);
        Category = store.Categories.FirstOrDefault() ?? "";
        Location = CurrentUser?.Location;
        if (FindPrevious(id.Value) is { } previous)
        {
            Title = previous.Title;
            if (store.Categories.Contains(previous.Category, StringComparer.OrdinalIgnoreCase)) Category = previous.Category;
            Location = previous.Location ?? Location;
        }
        return Page();
    }

    public IActionResult OnPost(List<IFormFile>? files)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == id);
        var previous = FindPrevious(id.Value);
        var chosen = PortalFiles.Chosen(files);
        if (PortalFiles.TooMany(chosen) is { } tooMany) ModelState.AddModelError("", tooMany);

        if (string.IsNullOrWhiteSpace(Title)) ModelState.AddModelError("", "Tell us what the problem is.");
        else if (Title.Trim().Length > HelpdeskStore.MaxTicketTitleLength) ModelState.AddModelError("", $"Keep the problem to {HelpdeskStore.MaxTicketTitleLength} characters - put the rest under Tell us more.");
        if ((Description ?? "").Trim().Length > HelpdeskStore.MaxTicketTextLength) ModelState.AddModelError("", $"Keep the details under {HelpdeskStore.MaxTicketTextLength} characters.");
        if (!store.Categories.Contains(Category, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("", "Select a category.");
        if (!string.IsNullOrWhiteSpace(Location) && !store.Locations.Contains(Location, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("", "Select a valid location.");
        if (!ModelState.IsValid) return Page();

        var priority = store.Priorities.FirstOrDefault(x => string.Equals(x, "Normal", StringComparison.OrdinalIgnoreCase)) ?? store.Priorities.FirstOrDefault() ?? "Normal";
        var createdAt = DateTime.UtcNow;
        var sla = store.SlaFor(priority, Category);
        var dueDate = store.CalculateDueDate(sla, createdAt);
        var number = store.AddTicket(new TicketRecord(
            0, Title!.Trim(), (Description ?? "").Trim(), id.Value, [], null,
            priority, store.Statuses.FirstOrDefault() ?? "Open", Category, createdAt,
            null, sla, dueDate, false, false, null, string.IsNullOrWhiteSpace(Location) ? null : Location.Trim())
        { Type = TicketTypes.Incident });
        // Linked both ways, so whoever picks it up sees the history of the first time round.
        if (previous is not null) store.LinkRelatedTickets(number, previous.Number);
        var problems = PortalFiles.Attach(store, number, chosen);

        TempData["Message"] = $"Ticket #{number} submitted - a technician will be in touch.{(problems.Length > 0 ? $" {problems}" : "")}";
        return RedirectToPage("/Portal/Ticket", new { number });
    }

    // Only the requester's own ticket, and only once it's closed - an open one takes replies instead.
    private TicketRecord? FindPrevious(Guid requesterId)
    {
        Previous = Again is { } number
            ? store.Tickets.FirstOrDefault(x => x.Number == number && x.RequesterId == requesterId && TicketInsights.IsClosed(x))
            : null;
        if (Previous is null) Again = null;
        return Previous;
    }
}
