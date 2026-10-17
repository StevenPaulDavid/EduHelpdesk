using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class NewTicketModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<string> TechnicianTeams => store.TechnicianTeams;
    // Disposed assets can't be linked to a new ticket - they have left the estate.
    public IReadOnlyList<AssetRecord> Assets => store.Assets.Where(x => !HelpdeskStore.IsDisposed(x)).ToList();
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    // With several desks (HelpdeskStore.Desks) the form starts on the logger's first desk, and the desk box narrows the
    // categories, teams and technicians to that desk's. The category is what decides the desk; the box only filters.
    public bool HasSeveralDesks => store.HasSeveralDesks;
    public IReadOnlyList<Desk> Desks => store.Desks;
    public Guid DeskOfCategory(string category) => store.DeskForCategory(category).Id;
    public Guid? DeskOfTeam(string team) => store.DeskForTeam(team)?.Id;
    public Guid StartingDesk => store.DeskForCategory(Ticket.Category).Id;
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributes => store.TicketAttributeDefinitions;
    // The template this form was started from, and its default answers for custom attributes.
    public IReadOnlyList<TicketTemplate> Templates => store.TicketTemplates;
    public TicketTemplate? AppliedTemplate { get; private set; }
    public IReadOnlyDictionary<Guid, string> TemplateAttributeValues { get; private set; } = new Dictionary<Guid, string>();
    [BindProperty] public TicketInput Ticket { get; set; } = new();
    [TempData] public string? Message { get; set; }

    public void OnGet(Guid? template)
    {
        if (template is { } templateId && store.GetTicketTemplate(templateId) is { } chosen)
        {
            AppliedTemplate = chosen;
            Ticket.Title = chosen.Title;
            Ticket.Description = chosen.Description;
            Ticket.Type = chosen.Type;
            Ticket.Category = chosen.Category;
            Ticket.Priority = chosen.Priority;
            Ticket.SlaId = chosen.SlaId is { } sla && store.Slas.Any(x => x.Id == sla) ? sla : null;
            TemplateAttributeValues = chosen.AttributeValues;
        }
        if (!store.Categories.Contains(Ticket.Category, StringComparer.OrdinalIgnoreCase))
            Ticket.Category = (store.DesksOf(User).FirstOrDefault() is { } mine ? store.CategoriesFor(mine.Id).FirstOrDefault() : null)
                ?? store.Categories.FirstOrDefault() ?? Ticket.Category;
        if (!store.Priorities.Contains(Ticket.Priority, StringComparer.OrdinalIgnoreCase))
            Ticket.Priority = store.Priorities.FirstOrDefault() ?? Ticket.Priority;
    }

    public IActionResult OnPostCreateTicket()
    {
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(Ticket.Title)) ModelState.AddModelError("Ticket.Title", "Enter a ticket title.");
        if (Ticket.RequesterId == Guid.Empty) ModelState.AddModelError("Ticket.RequesterId", "Select a requester.");
        if (!store.Categories.Contains(Ticket.Category, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.Category", "Select a valid category.");
        if (!store.Priorities.Contains(Ticket.Priority, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.Priority", "Select a valid priority.");
        if (!TicketTypes.All.Contains(Ticket.Type, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.Type", "Select a valid type.");
        if (Ticket.SlaId.HasValue && !store.Slas.Any(x => x.Id == Ticket.SlaId)) ModelState.AddModelError("Ticket.SlaId", "Select a valid SLA.");
        if (Ticket.TechnicianId.HasValue && !store.Technicians.Any(x => x.Id == Ticket.TechnicianId)) ModelState.AddModelError("Ticket.TechnicianId", "Select a valid technician.");
        if (!string.IsNullOrWhiteSpace(Ticket.TeamName) && !store.TechnicianTeams.Contains(Ticket.TeamName, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.TeamName", "Select a valid team.");
        if (Ticket.TechnicianId.HasValue && !string.IsNullOrWhiteSpace(Ticket.TeamName) && store.Technicians.FirstOrDefault(x => x.Id == Ticket.TechnicianId) is { } technician && !HelpdeskStore.TechnicianInTeam(technician, Ticket.TeamName)) ModelState.AddModelError("Ticket.TechnicianId", $"Select a technician from the {Ticket.TeamName} team.");
        if (store.Categories.Contains(Ticket.Category, StringComparer.OrdinalIgnoreCase) && store.DeskForCategory(Ticket.Category) is { } desk)
        {
            if (!string.IsNullOrWhiteSpace(Ticket.TeamName) && store.DeskForTeam(Ticket.TeamName) is { } teamDesk && teamDesk.Id != desk.Id)
                ModelState.AddModelError("Ticket.TeamName", $"The {Ticket.TeamName} team is on the {teamDesk.Name} desk, but {Ticket.Category} is a {desk.Name} category.");
            if (Ticket.TechnicianId is { } chosen && store.Technicians.FirstOrDefault(x => x.Id == chosen) is { } person && !HelpdeskStore.TechnicianOnDesk(person, desk.Id))
                ModelState.AddModelError("Ticket.TechnicianId", $"{person.Name} isn't on the {desk.Name} desk.");
        }
        if (Ticket.DueDate.HasValue && Ticket.DueDate < DateTime.UtcNow.Date) ModelState.AddModelError("Ticket.DueDate", "Due date cannot be in the past.");
        if (!string.IsNullOrWhiteSpace(Ticket.Location) && !store.Locations.Contains(Ticket.Location, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.Location", "Select a valid location.");
        if (!ModelState.IsValid) return Page();

        var createdAt = DateTime.UtcNow;
        var selectedSla = Ticket.SlaId ?? store.SlaFor(Ticket.Priority, Ticket.Category);
        var dueDate = Ticket.DueDate ?? store.CalculateDueDate(selectedSla, createdAt, Ticket.Category);
        var assetIds = (Ticket.AssetIds ?? []).Where(id => store.Assets.Any(a => a.Id == id)).Distinct().ToList();
        var number = store.AddTicket(new TicketRecord(
            0,
            Ticket.Title.Trim(),
            (Ticket.Description ?? string.Empty).Trim(),
            Ticket.RequesterId,
            assetIds,
            Ticket.TechnicianId,
            Ticket.Priority,
            store.StartingStatusFor(Ticket.Category),
            Ticket.Category,
            DateTime.UtcNow,
            null, selectedSla, dueDate, Ticket.DueDate.HasValue, Ticket.SlaId.HasValue, Ticket.TeamName, string.IsNullOrWhiteSpace(Ticket.Location) ? null : Ticket.Location.Trim()) { Type = TicketTypes.Normalize(Ticket.Type) });
        if (!store.UpdateTicketAttributeValues(number, Ticket.Category, Ticket.CustomAttributes)) { }
        Message = $"Ticket {store.Ref(number)} created.";
        return RedirectToPage("/Job", new { number });
    }

    public sealed class TicketInput
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public Guid RequesterId { get; set; }
        public List<Guid> AssetIds { get; set; } = [];
        public Guid? TechnicianId { get; set; }
        public string? TeamName { get; set; }
        public string Priority { get; set; } = "Normal";
        public string Category { get; set; } = "Hardware";
        public string? Location { get; set; }
        public string Type { get; set; } = TicketTypes.Incident;
        public Guid? SlaId { get; set; }
        public DateTime? DueDate { get; set; }
        public Dictionary<Guid, string>? CustomAttributes { get; set; }
    }
}
