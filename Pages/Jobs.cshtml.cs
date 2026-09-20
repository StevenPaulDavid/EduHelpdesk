using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class JobsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyDictionary<string, string> StatusDescriptions => store.StatusDescriptions;
    public string StatusDescriptionsJson => System.Text.Json.JsonSerializer.Serialize(StatusDescriptions).Replace("</", "<\\/");
    [TempData] public string? Message { get; set; }

    public IActionResult OnPostBulkUpdate(int[] selectedNumbers, string operation, string? status, Guid? technicianId)
    {
        if (selectedNumbers.Length == 0)
        {
            Message = "Select at least one job.";
            return RedirectToPage();
        }

        if (operation is not ("status" or "technician"))
        {
            Message = "Choose whether to update status or technician.";
            return RedirectToPage();
        }

        if (operation == "status" && (status is null || !store.Statuses.Contains(status, StringComparer.OrdinalIgnoreCase)))
        {
            Message = "Select a valid status.";
            return RedirectToPage();
        }

        if (operation == "technician" && technicianId.HasValue && !store.Technicians.Any(x => x.Id == technicianId.Value))
        {
            Message = "Select a valid technician.";
            return RedirectToPage();
        }

        var selected = selectedNumbers.ToHashSet();
        var updated = 0;
        foreach (var ticket in store.Tickets.Where(x => selected.Contains(x.Number)))
        {
            var updatedTicket = operation == "status"
                ? ticket with
                {
                    Status = status!,
                    ClosedAt = status == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null
                }
                : ticket with { TechnicianId = technicianId };
            if (store.UpdateTicket(updatedTicket))
                updated++;
        }

        Message = $"{updated} job{(updated == 1 ? "" : "s")} updated.";
        return RedirectToPage();
    }
}
