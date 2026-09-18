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
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributes => store.GetTicketAttributes(Ticket.Category);
    [BindProperty] public TicketInput Ticket { get; set; } = new();
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostCreateTicket()
    {
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(Ticket.Title)) ModelState.AddModelError("Ticket.Title", "Enter a ticket title.");
        if (Ticket.RequesterId == Guid.Empty) ModelState.AddModelError("Ticket.RequesterId", "Select a requester.");
        if (!store.Categories.Contains(Ticket.Category, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.Category", "Select a valid category.");
        if (!store.Priorities.Contains(Ticket.Priority, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.Priority", "Select a valid priority.");
        if (Ticket.SlaId.HasValue && !store.Slas.Any(x => x.Id == Ticket.SlaId)) ModelState.AddModelError("Ticket.SlaId", "Select a valid SLA.");
        if (Ticket.TechnicianId.HasValue && !store.Technicians.Any(x => x.Id == Ticket.TechnicianId)) ModelState.AddModelError("Ticket.TechnicianId", "Select a valid technician.");
        if (!string.IsNullOrWhiteSpace(Ticket.TeamName) && !store.TechnicianTeams.Contains(Ticket.TeamName, StringComparer.OrdinalIgnoreCase)) ModelState.AddModelError("Ticket.TeamName", "Select a valid team.");
        if (Ticket.TechnicianId.HasValue && !string.IsNullOrWhiteSpace(Ticket.TeamName)) ModelState.AddModelError("Ticket.TeamName", "Choose an individual technician or a team, not both.");
        if (Ticket.DueDate.HasValue && Ticket.DueDate < DateTime.UtcNow.Date) ModelState.AddModelError("Ticket.DueDate", "Due date cannot be in the past.");
        if (!ModelState.IsValid) return Page();

        var createdAt = DateTime.UtcNow;
        var selectedSla = Ticket.SlaId ?? store.SlaForPriority(Ticket.Priority);
        var dueDate = Ticket.DueDate ?? store.CalculateDueDate(selectedSla, createdAt);
        var number = store.AddTicket(new TicketRecord(
            0,
            Ticket.Title.Trim(),
            (Ticket.Description ?? string.Empty).Trim(),
            Ticket.RequesterId,
            Ticket.AssetId,
            Ticket.TechnicianId,
            Ticket.Priority,
            store.Statuses.FirstOrDefault() ?? "Open",
            Ticket.Category,
            DateTime.UtcNow,
            null, selectedSla, dueDate, Ticket.DueDate.HasValue, Ticket.SlaId.HasValue, Ticket.TeamName));
        if (!store.UpdateTicketAttributeValues(number, Ticket.Category, Ticket.CustomAttributes)) { }
        Message = $"Ticket #{number} created.";
        return RedirectToPage("/Job", new { number });
    }

    public sealed class TicketInput
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public Guid RequesterId { get; set; }
        public Guid? AssetId { get; set; }
        public Guid? TechnicianId { get; set; }
        public string? TeamName { get; set; }
        public string Priority { get; set; } = "Normal";
        public string Category { get; set; } = "Hardware";
        public Guid? SlaId { get; set; }
        public DateTime? DueDate { get; set; }
        public Dictionary<Guid, string>? CustomAttributes { get; set; }
    }
}
