using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class NewTicketModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Priorities => store.Priorities;
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
        if (!ModelState.IsValid) return Page();

        var number = store.AddTicket(new TicketRecord(
            0,
            Ticket.Title.Trim(),
            Ticket.Description.Trim(),
            Ticket.RequesterId,
            Ticket.AssetId,
            Ticket.TechnicianId,
            Ticket.Priority,
            store.Statuses.FirstOrDefault() ?? "Open",
            Ticket.Category,
            DateTime.UtcNow,
            null));
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
        public string Priority { get; set; } = "Normal";
        public string Category { get; set; } = "Hardware";
    }
}
