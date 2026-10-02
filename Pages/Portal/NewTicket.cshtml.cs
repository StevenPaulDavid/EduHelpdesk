using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// Report a problem in the staff portal, in up to three short steps: big category tiles, then that category's buttons
// (service catalogue items and portal-ticked templates, which look the same to staff), then a confirm step asking only
// where it is, how urgent, and an optional description. "Something else" opens a plain form for anything not listed,
// so the buttons never stop anyone reporting a problem. Type, requester and team are set server-side.
public class NewTicketModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public enum Step { Categories, Choices, Confirm, Describe }

    public UserRecord? CurrentUser { get; private set; }
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<(string Category, int Count)> PortalCategories => store.PortalCategories();
    public IReadOnlyList<HelpdeskStore.PortalChoice> Choices => store.PortalChoices(Category);

    public Step Mode { get; private set; }
    // What was tapped, shown at the top of the confirm step.
    public string? ChosenLabel { get; private set; }
    public string? ChosenHelper { get; private set; }

    // Nullable on purpose: a non-nullable string property is treated as [Required], which added "The Description field
    // is required." to a box the form calls optional - and failed the ticket. The handler does its own checks.
    [BindProperty] public string? Title { get; set; } = "";
    [BindProperty] public string? Description { get; set; } = "";
    // Category, SubCategory (a catalogue item's name) and TemplateId are also the query string of the earlier steps.
    // Never null, but declared nullable: a non-nullable string is implicitly [Required], and the empty category of the
    // first screen then put "The Category field is required." on it. Each step checks what it needs itself.
    private string _category = "";
    [BindProperty(SupportsGet = true)] public string? Category { get => _category; set => _category = value?.Trim() ?? ""; }
    [BindProperty] public string? Location { get; set; }
    [BindProperty(SupportsGet = true)] public string? SubCategory { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? TemplateId { get; set; }
    // "Something else": the plain form, where the requester types their own title.
    [BindProperty(SupportsGet = true)] public bool Other { get; set; }
    [BindProperty] public string? Priority { get; set; }
    // "Report it again": the requester's own closed ticket this one follows on from. It is linked to it once raised.
    [BindProperty(SupportsGet = true)] public int? Again { get; set; }
    public TicketRecord? Previous { get; private set; }

    private ServiceItem? _item;
    private TicketTemplate? _template;

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
            else if (store.FindPortalTemplateByName(previous.Category, previous.SubCategory) is { } template) TemplateId = template.Id;
            else { Other = true; Title = previous.Title; }
        }

        Resolve();
        if (Mode == Step.Confirm)
        {
            // What the button suggests: a template its own priority and description, an item its starting priority.
            if (_template is not null)
            {
                Description = _template.Description;
                Priority = ValidPriority(_template.Priority) ?? Priority;
            }
            else if (_item is { DefaultPriority.Length: > 0 }) Priority = ValidPriority(_item.DefaultPriority) ?? Priority;
        }
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

        // A template or a catalogue item names the ticket; with neither, the requester typed the title themselves.
        TicketTemplate? template = null;
        ServiceItem? item = null;
        if (TemplateId is { } templateId)
        {
            template = store.FindPortalTemplate(templateId);
            if (template is null) ModelState.AddModelError("", "That option is no longer available - please choose again.");
        }
        else if (!string.IsNullOrWhiteSpace(SubCategory))
        {
            item = store.FindServiceItem(Category, SubCategory) is { ShowInPortal: true } found ? found : null;
            if (item is null) ModelState.AddModelError("", "That option is no longer available - please choose again.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Title)) ModelState.AddModelError("", "Tell us what the problem is.");
            else if (Title.Trim().Length > HelpdeskStore.MaxTicketTitleLength) ModelState.AddModelError("", $"Keep the problem to {HelpdeskStore.MaxTicketTitleLength} characters - put the rest under Tell us more.");
            if (!store.Categories.Contains(Category, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("", "Select a category.");
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

        var category = template?.Category ?? item?.Category ?? Category ?? "";
        var createdAt = DateTime.UtcNow;
        // A template's own SLA is kept as chosen (it stops following the priority), as on the technician's form.
        Guid? templateSla = template?.SlaId is { } slaId && store.Slas.Any(x => x.Id == slaId) ? slaId : null;
        var sla = templateSla ?? store.SlaFor(priority!, category);
        var dueDate = store.CalculateDueDate(sla, createdAt);
        var title = template is not null ? (string.IsNullOrWhiteSpace(template.Title) ? template.Name : template.Title) : item?.Name ?? Title!.Trim();
        var number = store.AddTicket(new TicketRecord(
            0, title, (Description ?? "").Trim(), id.Value, [], null,
            priority!, store.Statuses.FirstOrDefault() ?? "Open", category, createdAt,
            null, sla, dueDate, false, templateSla.HasValue, null, string.IsNullOrWhiteSpace(Location) ? null : Location.Trim())
        { Type = template is not null ? TicketTypes.Normalize(template.Type) : TicketTypes.Incident, SubCategory = template?.Name ?? item?.Name ?? "" });
        // Answers to custom attributes the template carries, which the staff member is never asked.
        if (template is { AttributeValues.Count: > 0 }) store.UpdateTicketAttributeValues(number, category, template.AttributeValues);
        // Linked both ways, so whoever picks it up sees the history of the first time round.
        if (previous is not null) store.LinkRelatedTickets(number, previous.Number);
        var problems = PortalFiles.Attach(store, number, chosen);

        TempData["Message"] = $"Ticket #{number} submitted - a technician will be in touch.{(problems.Length > 0 ? $" {problems}" : "")}";
        return RedirectToPage("/Portal/Ticket", new { number });
    }

    // Works out which step to show from what has been chosen so far. Anything that no longer resolves - a button hidden
    // since the page was opened - falls back a step rather than showing an error page.
    private void Resolve()
    {
        _template = TemplateId is { } templateId ? store.FindPortalTemplate(templateId) : null;
        _item = _template is null && !string.IsNullOrWhiteSpace(SubCategory) && store.FindServiceItem(Category, SubCategory) is { ShowInPortal: true } found ? found : null;
        if (_template is not null || _item is not null)
        {
            Mode = Step.Confirm;
            Category = _template?.Category ?? _item!.Category;
            ChosenLabel = _template?.Name ?? _item!.Name;
            ChosenHelper = _template?.HelperLine ?? _item!.HelperLine;
            return;
        }
        TemplateId = null;
        SubCategory = null;
        var available = PortalCategories;
        // No buttons at all (nothing in the catalogue, or none shown) leaves the plain form as the only way in.
        if (Other || available.Count == 0)
        {
            Mode = Step.Describe;
            Other = true;
            if (!store.Categories.Contains(Category, StringComparer.OrdinalIgnoreCase)) Category = store.Categories.FirstOrDefault() ?? "";
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
