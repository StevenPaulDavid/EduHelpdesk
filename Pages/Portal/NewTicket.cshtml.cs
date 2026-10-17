using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// Report a problem in the staff portal, in up to three short steps: big category tiles, then that category's buttons
// (the service catalogue's items), then a confirm step asking only
// where it is, how urgent, and an optional description. "Something else" opens a plain form for anything not listed,
// so the buttons never stop anyone reporting a problem. Type, requester and team are set server-side.
// With more than one desk offered in the portal (HelpdeskStore.Desks) there is a step before the categories: who they
// need - IT, Site, Cleaning - and the steps after it show only that desk's categories.
public class NewTicketModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public enum Step { Desks, Categories, Choices, Confirm, Describe }

    public UserRecord? CurrentUser { get; private set; }
    public IReadOnlyList<Desk> PortalDesks => _portalDesks ??= store.PortalDesks();
    private IReadOnlyList<Desk>? _portalDesks;
    public bool AsksForDesk => PortalDesks.Count > 1;
    // The desk chosen, or the one the chosen category is on. Null before one is chosen, and with only one desk.
    public Desk? ChosenDesk { get; private set; }
    [BindProperty(SupportsGet = true)] public string? Desk { get; set; }
    public CategoryStyle StyleForDesk(Desk desk) => store.StyleForDesk(desk);
    // Categories staff may report under: on a desk offered in the portal, and on the chosen desk once there is one.
    public IReadOnlyList<string> Categories => store.Categories.Where(InScope).ToList();
    public IReadOnlyList<(Desk Desk, IReadOnlyList<string> Categories)> CategoriesByDesk =>
        PortalDesks.Select(d => (d, (IReadOnlyList<string>)store.CategoriesFor(d.Id))).Where(x => x.Item2.Count > 0).ToList();
    private bool InScope(string category)
    {
        var desk = store.DeskForCategory(category);
        return (ChosenDesk is null || desk.Id == ChosenDesk.Id) && (!AsksForDesk || PortalDesks.Any(x => x.Id == desk.Id));
    }
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<(string Category, int Count)> PortalCategories => store.PortalCategories().Where(x => InScope(x.Category)).ToList();
    // How many categories a desk's tile leads to, for the line under it.
    public int CategoriesOn(Desk desk) => store.CategoriesFor(desk.Id).Count;
    public IReadOnlyList<ServiceItem> Choices => store.PortalChoices(Category);
    // The icon and colour chosen for a category's tile in Settings, or its built-in look.
    public CategoryStyle StyleFor(string? category) => store.StyleFor(category);

    public Step Mode { get; private set; }
    // What was tapped, shown at the top of the confirm step.
    public string? ChosenLabel { get; private set; }
    public string? ChosenHelper { get; private set; }

    // Nullable on purpose: a non-nullable string property is treated as [Required], which added "The Description field
    // is required." to a box the form calls optional - and failed the ticket. The handler does its own checks.
    [BindProperty] public string? Title { get; set; } = "";
    [BindProperty] public string? Description { get; set; } = "";
    // Category and SubCategory (a catalogue item's name) are also the query string of the earlier steps.
    // Never null, but declared nullable: a non-nullable string is implicitly [Required], and the empty category of the
    // first screen then put "The Category field is required." on it. Each step checks what it needs itself.
    private string _category = "";
    [BindProperty(SupportsGet = true)] public string? Category { get => _category; set => _category = value?.Trim() ?? ""; }
    [BindProperty] public string? Location { get; set; }
    [BindProperty(SupportsGet = true)] public string? SubCategory { get; set; }
    // "Something else": the plain form, where the requester types their own title.
    [BindProperty(SupportsGet = true)] public bool Other { get; set; }
    [BindProperty] public string? Priority { get; set; }
    // "Report it again": the requester's own closed ticket this one follows on from. It is linked to it once raised.
    [BindProperty(SupportsGet = true)] public int? Again { get; set; }
    public TicketRecord? Previous { get; private set; }

    private ServiceItem? _item;

    public IActionResult OnGet()
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == id);
        Location = CurrentUser?.Location;
        Priority = DefaultPriority();

        if (FindPrevious(id.Value) is { } previous)
        {
            // Back onto the button it was raised from when that is still offered, otherwise the plain form with the old
            // title to edit.
            Category = previous.Category;
            Location = previous.Location ?? Location;
            if (store.FindServiceItem(previous.Category, previous.SubCategory) is { ShowInPortal: true } item) SubCategory = item.Name;
            else { Other = true; Title = previous.Title; }
        }

        Resolve();
        // What the button suggests: the item's starting priority.
        if (Mode == Step.Confirm && _item is { DefaultPriority.Length: > 0 }) Priority = ValidPriority(_item.DefaultPriority) ?? Priority;
        if (Previous is not null) Priority = ValidPriority(Previous.Priority) ?? Priority;
        if (Mode is Step.Categories or Step.Choices) Title = "";
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

        // A catalogue item names the ticket; without one, the requester typed the title themselves.
        ServiceItem? item = null;
        if (!string.IsNullOrWhiteSpace(SubCategory))
        {
            item = store.FindServiceItem(Category, SubCategory) is { ShowInPortal: true } found ? found : null;
            if (item is null) ModelState.AddModelError("", "That option is no longer available - please choose again.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Title)) ModelState.AddModelError("", "Tell us what the problem is.");
            else if (Title.Trim().Length > HelpdeskStore.MaxTicketTitleLength) ModelState.AddModelError("", $"Keep the problem to {HelpdeskStore.MaxTicketTitleLength} characters - put the rest under Tell us more.");
            if (!store.Categories.Contains(Category, StringComparer.OrdinalIgnoreCase) || !InScope(Category!)) ModelState.AddModelError("", "Select a category.");
        }
        if ((Description ?? "").Trim().Length > HelpdeskStore.MaxTicketTextLength) ModelState.AddModelError("", $"Keep the details under {HelpdeskStore.MaxTicketTextLength} characters.");
        if (string.IsNullOrWhiteSpace(Location) ? store.Locations.Count > 0 : !store.Locations.Contains(Location, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("", "Choose where the problem is.");
        var priority = ValidPriority(Priority);
        if (priority is null) ModelState.AddModelError("", "Choose how urgent it is.");
        if (!ModelState.IsValid)
        {
            Resolve();
            return Page();
        }

        var category = item?.Category ?? Category ?? "";
        var createdAt = DateTime.UtcNow;
        var sla = store.SlaFor(priority!, category);
        var dueDate = store.CalculateDueDate(sla, createdAt, category);
        var title = item?.Name ?? Title!.Trim();
        var number = store.AddTicket(new TicketRecord(
            0, title, (Description ?? "").Trim(), id.Value, [], null,
            priority!, store.StartingStatusFor(category), category, createdAt,
            null, sla, dueDate, false, false, null, string.IsNullOrWhiteSpace(Location) ? null : Location.Trim())
        { Type = TicketTypes.Incident, SubCategory = item?.Name ?? "" });
        // Linked both ways, so whoever picks it up sees the history of the first time round.
        if (previous is not null) store.LinkRelatedTickets(number, previous.Number);
        var problems = PortalFiles.Attach(store, number, chosen);
        // Told after the files are on, so whoever opens it from the ping finds them. Only here: tickets that arrive another
        // way (a technician logging one, an import, an onboarding's tasks) are not news to the team.
        store.AddNotification(HelpdeskStore.StaffAudience, null, HelpdeskStore.NotificationKinds.NewTicket, number);
        // The same event, for the Teams channel if the school has one set up (Settings → Teams channel).
        store.QueueTeamsNewTicket(number, People.QuickStartModel.GuessAddress(Request));

        TempData["Message"] = $"Ticket {store.Ref(number)} submitted - a technician will be in touch.{(problems.Length > 0 ? $" {problems}" : "")}";
        return RedirectToPage("/Portal/Ticket", new { number });
    }

    // Works out which step to show from what has been chosen so far. Anything that no longer resolves - a button hidden
    // since the page was opened - falls back a step rather than showing an error page.
    private void Resolve()
    {
        _item = !string.IsNullOrWhiteSpace(SubCategory) && store.FindServiceItem(Category, SubCategory) is { ShowInPortal: true } found ? found : null;
        if (_item is not null)
        {
            Mode = Step.Confirm;
            Category = _item.Category;
            ChosenLabel = _item.Name;
            ChosenHelper = _item.HelperLine;
            return;
        }
        SubCategory = null;
        // A chosen category says the desk; otherwise the desk asked for, if it's one staff can report to.
        ChosenDesk = !AsksForDesk ? null
            : store.Categories.Contains(Category, StringComparer.OrdinalIgnoreCase) && PortalDesks.FirstOrDefault(x => x.Id == store.DeskForCategory(Category).Id) is { } byCategory ? byCategory
            : store.FindDesk(Desk) is { } asked && PortalDesks.Any(x => x.Id == asked.Id) ? asked
            : null;
        Desk = ChosenDesk?.Name;
        // Who they need comes first, unless they've gone straight to the plain form (which then lists every desk's).
        if (AsksForDesk && ChosenDesk is null && !Other)
        {
            Mode = Step.Desks;
            Category = "";
            return;
        }
        var available = PortalCategories;
        // No buttons at all (nothing in the catalogue, or none shown) leaves the plain form as the only way in.
        if (Other || available.Count == 0)
        {
            Mode = Step.Describe;
            Other = true;
            if (!Categories.Contains(Category, StringComparer.OrdinalIgnoreCase)) Category = Categories.FirstOrDefault() ?? "";
            return;
        }
        var match = available.FirstOrDefault(x => string.Equals(x.Category, Category, StringComparison.OrdinalIgnoreCase));
        if (match.Category is not null) { Mode = Step.Choices; Category = match.Category; }
        else { Mode = Step.Categories; Category = ""; }
    }

    private string? ValidPriority(string? value) => store.Priorities.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));

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
