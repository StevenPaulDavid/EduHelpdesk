using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// The short version of /NewTicket for the staff portal: no requester/technician/team/SLA/asset pickers - just where it
// is, what kind of problem, which item from the service catalogue (or a title typed by hand when it isn't listed), how
// urgent, and an optional description. Type is set server-side.
public class NewTicketModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<ServiceItem> ServiceItems => store.ServiceItems;
    // Nullable on purpose: a non-nullable string property is treated as [Required], which added "The Description field
    // is required." to a box the form calls optional - and failed the ticket. The handler does its own checks.
    [BindProperty] public string? Title { get; set; } = "";
    [BindProperty] public string? Description { get; set; } = "";
    [BindProperty] public string Category { get; set; } = "";
    [BindProperty] public string? Location { get; set; }
    // The catalogue item's name, or blank for "Something else" - then Title is what the requester typed. ChooseOne is the
    // list's placeholder: not an answer, so it can't be submitted.
    public const string ChooseOne = "?";
    [BindProperty] public string? SubCategory { get; set; } = ChooseOne;
    [BindProperty] public string? Priority { get; set; }
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
        Priority = DefaultPriority();
        if (FindPrevious(id.Value) is { } previous)
        {
            if (store.Categories.Contains(previous.Category, StringComparer.OrdinalIgnoreCase)) Category = previous.Category;
            Location = previous.Location ?? Location;
            // Back onto the same catalogue item when it is still there, otherwise the old title to edit.
            if (store.FindServiceItem(previous.Category, previous.SubCategory) is { } item) SubCategory = item.Name;
            else { SubCategory = ""; Title = previous.Title; }
            Priority = store.Priorities.FirstOrDefault(x => string.Equals(x, previous.Priority, StringComparison.OrdinalIgnoreCase)) ?? Priority;
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

        // A catalogue item names the ticket; "Something else" (blank) means the requester typed the title themselves.
        var item = string.IsNullOrWhiteSpace(SubCategory) ? null : store.FindServiceItem(Category, SubCategory);
        if (!string.IsNullOrWhiteSpace(SubCategory) && item is null) ModelState.AddModelError("", "Choose what's wrong from the list, or pick Something else and describe it.");
        else if (item is null && string.IsNullOrWhiteSpace(Title)) ModelState.AddModelError("", "Tell us what the problem is.");
        else if (item is null && Title!.Trim().Length > HelpdeskStore.MaxTicketTitleLength) ModelState.AddModelError("", $"Keep the problem to {HelpdeskStore.MaxTicketTitleLength} characters - put the rest under Tell us more.");
        if ((Description ?? "").Trim().Length > HelpdeskStore.MaxTicketTextLength) ModelState.AddModelError("", $"Keep the details under {HelpdeskStore.MaxTicketTextLength} characters.");
        if (!store.Categories.Contains(Category, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("", "Select a category.");
        if (string.IsNullOrWhiteSpace(Location) ? store.Locations.Count > 0 : !store.Locations.Contains(Location, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("", "Choose where the problem is.");
        var priority = store.Priorities.FirstOrDefault(x => string.Equals(x, Priority?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (priority is null) ModelState.AddModelError("", "Choose how urgent it is.");
        if (!ModelState.IsValid) return Page();

        var createdAt = DateTime.UtcNow;
        var sla = store.SlaFor(priority!, Category);
        var dueDate = store.CalculateDueDate(sla, createdAt);
        var number = store.AddTicket(new TicketRecord(
            0, item?.Name ?? Title!.Trim(), (Description ?? "").Trim(), id.Value, [], null,
            priority!, store.Statuses.FirstOrDefault() ?? "Open", item?.Category ?? Category, createdAt,
            null, sla, dueDate, false, false, null, string.IsNullOrWhiteSpace(Location) ? null : Location.Trim())
        { Type = TicketTypes.Incident, SubCategory = item?.Name ?? "" });
        // Linked both ways, so whoever picks it up sees the history of the first time round.
        if (previous is not null) store.LinkRelatedTickets(number, previous.Number);
        var problems = PortalFiles.Attach(store, number, chosen);

        TempData["Message"] = $"Ticket #{number} submitted - a technician will be in touch.{(problems.Length > 0 ? $" {problems}" : "")}";
        return RedirectToPage("/Portal/Ticket", new { number });
    }

    private string DefaultPriority() => store.Priorities.FirstOrDefault(x => string.Equals(x, "Normal", StringComparison.OrdinalIgnoreCase)) ?? store.Priorities.FirstOrDefault() ?? "Normal";

    // Only the requester's own ticket, and only once it's closed - an open one takes replies instead.
    private TicketRecord? FindPrevious(Guid requesterId)
    {
        Previous = Again is { } number
            ? store.PortalTicket(requesterId, number) is { } ticket && TicketInsights.IsClosed(ticket) ? ticket : null
            : null;
        if (Previous is null) Again = null;
        return Previous;
    }
}
