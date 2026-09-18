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
    public string TemplateHtml { get; private set; } = string.Empty;
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(int number)
    {
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (Ticket is null) return NotFound();
        Requester = store.Users.FirstOrDefault(x => x.Id == Ticket.RequesterId);
        Technician = store.Technicians.FirstOrDefault(x => x.Id == Ticket.TechnicianId);
        Asset = store.Assets.FirstOrDefault(x => x.Id == Ticket.AssetId);
        TemplateHtml = store.RenderPrintTemplate(Ticket, Requester, Technician, Asset);
        return Page();
    }

    public IActionResult OnPostUpdate(int number, string field, string? value)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        if (field is "status" or "priority" or "category")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Message = "Select a value.";
                return RedirectToPage(new { number });
            }
            ticket = field switch
            {
                "status" => ticket with { Status = value, ClosedAt = value == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null },
                "priority" => ticket with { Priority = value },
                _ => ticket with { Category = value }
            };
        }
        else if (field == "technician")
        {
            Guid? technicianId = string.IsNullOrWhiteSpace(value) ? null : Guid.TryParse(value, out var parsedTechnicianId) ? parsedTechnicianId : null;
            if (technicianId.HasValue && !store.Technicians.Any(x => x.Id == technicianId.Value))
            {
                Message = "Select a valid technician.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { TechnicianId = technicianId };
        }
        else if (field == "asset")
        {
            Guid? assetId = string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
            if (assetId.HasValue && !store.Assets.Any(x => x.Id == assetId.Value))
            {
                Message = "Select a valid asset.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { AssetId = assetId };
        }
        else
        {
            Message = "Unknown job field.";
            return RedirectToPage(new { number });
        }
        store.UpdateTicket(ticket);
        Message = "Job updated.";
        return RedirectToPage(new { number });
    }
}
