using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class JobModel(HelpdeskStore store) : PageModel
{
    public TicketRecord? Ticket { get; private set; }
    public UserRecord? Requester { get; private set; }
    public TechnicianRecord? Technician { get; private set; }
    public AssetRecord? Asset { get; private set; }
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(int number)
    {
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (Ticket is null) return NotFound();
        Requester = store.Users.FirstOrDefault(x => x.Id == Ticket.RequesterId);
        Technician = store.Technicians.FirstOrDefault(x => x.Id == Ticket.TechnicianId);
        Asset = store.Assets.FirstOrDefault(x => x.Id == Ticket.AssetId);
        return Page();
    }

    public IActionResult OnPostUpdate(int number, string status, string priority, string category, Guid? assetId, Guid technicianId)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        if (technicianId == Guid.Empty || !store.Technicians.Any(x => x.Id == technicianId))
        {
            Message = "Select a valid technician.";
            return RedirectToPage(new { number });
        }
        if (assetId.HasValue && !store.Assets.Any(x => x.Id == assetId.Value))
        {
            Message = "Select a valid asset.";
            return RedirectToPage(new { number });
        }
        DateTime? closedAt = status == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null;
        store.UpdateTicket(ticket with { Status = status, Priority = priority, Category = category, AssetId = assetId, TechnicianId = technicianId, ClosedAt = closedAt });
        Message = "Job updated.";
        return RedirectToPage(new { number });
    }
}
