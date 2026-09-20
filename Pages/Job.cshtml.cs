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
    public string? TeamName => Ticket?.TeamName;
    public IReadOnlyList<AssetRecord> LinkedAssets { get; private set; } = [];
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<string> TechnicianTeams => store.TechnicianTeams;
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyDictionary<string, string> StatusDescriptions => store.StatusDescriptions;
    public string StatusDescriptionsJson => System.Text.Json.JsonSerializer.Serialize(StatusDescriptions).Replace("</", "<\\/");
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => store.Categories;
    public bool RequiresCloseMessage => Ticket is not null && store.RequiresCloseMessage(Ticket);
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributes => Ticket is null ? [] : store.GetTicketAttributes(Ticket.Category);
    public IReadOnlyDictionary<Guid, string> TicketAttributeValues => Ticket is null ? new Dictionary<Guid, string>() : store.GetTicketAttributeValues(Ticket.Number);
    public IReadOnlyList<(PartRecord Part, int Quantity)> TicketParts => Ticket is null ? [] : store.GetTicketParts(Ticket.Number);
    public IReadOnlyList<PartRecord> Parts => store.Parts;
    public IReadOnlyList<TicketRecord> MergeCandidates => Ticket is null ? [] : store.Tickets.Where(x => x.Number != Ticket.Number).OrderByDescending(x => x.Number).ToList();
    public string TemplateHtml { get; private set; } = string.Empty;
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(int number)
    {
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (Ticket is null) return NotFound();
        Requester = store.Users.FirstOrDefault(x => x.Id == Ticket.RequesterId);
        Technician = store.Technicians.FirstOrDefault(x => x.Id == Ticket.TechnicianId);
        LinkedAssets = store.Assets.Where(x => Ticket.AssetIds.Contains(x.Id)).ToList();
        TemplateHtml = store.RenderPrintTemplate(Ticket, Requester, Technician, LinkedAssets);
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

            var allowed = field switch
            {
                "status" => store.Statuses,
                "priority" => store.Priorities,
                _ => store.Categories
            };
            if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                Message = "Select a valid value.";
                return RedirectToPage(new { number });
            }

            ticket = field switch
            {
                "status" => ticket with { Status = value, ClosedAt = value == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null },
                "priority" => ticket.SlaOverridden ? ticket with { Priority = value } : ticket with { Priority = value, SlaId = store.SlaForPriority(value), DueDate = ticket.DueDateOverridden ? ticket.DueDate : store.CalculateDueDate(store.SlaForPriority(value), ticket.CreatedAt) },
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
            ticket = ticket with { TechnicianId = technicianId, TeamName = null };
        }
        else if (field == "team")
        {
            var team = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (team is not null && !store.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase)) { Message = "Select a valid team."; return RedirectToPage(new { number }); }
            ticket = ticket with { TeamName = team, TechnicianId = null };
        }
        else if (field == "sla")
        {
            Guid? slaId = string.IsNullOrWhiteSpace(value) ? null : Guid.TryParse(value, out var parsedSlaId) ? parsedSlaId : null;
            if (slaId.HasValue && !store.Slas.Any(x => x.Id == slaId.Value))
            {
                Message = "Select a valid SLA.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { SlaId = slaId, SlaOverridden = slaId.HasValue, DueDate = ticket.DueDateOverridden ? ticket.DueDate : store.CalculateDueDate(slaId, ticket.CreatedAt), DueDateOverridden = ticket.DueDateOverridden };
        }

        else if (field == "dueDate")
        {
            DateTime? dueDate = string.IsNullOrWhiteSpace(value) ? null : DateTime.TryParse(value, out var parsedDueDate) ? parsedDueDate.ToUniversalTime() : null;
            if (!string.IsNullOrWhiteSpace(value) && !dueDate.HasValue)
            {
                Message = "Enter a valid due date.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { DueDate = dueDate, DueDateOverridden = dueDate.HasValue };
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

    public IActionResult OnPostUpdateAssets(int number, Guid[]? assetIds)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        var validAssetIds = (assetIds ?? []).Where(id => store.Assets.Any(x => x.Id == id)).Distinct().ToList();
        store.UpdateTicket(ticket with { AssetIds = validAssetIds });
        Message = "Linked assets updated.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostAssignPart(int number, Guid partId, int quantity)
    {
        if (partId == Guid.Empty || quantity <= 0)
        {
            Message = "Select a part and a quantity to assign.";
            return RedirectToPage(new { number });
        }
        Message = store.SetTicketPartQuantity(number, partId, quantity) ?? "Part assigned.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdatePartQuantity(int number, Guid partId, int quantity)
    {
        Message = store.SetTicketPartQuantity(number, partId, quantity) ?? (quantity <= 0 ? "Part removed." : "Part quantity updated.");
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdateAttributes(int number, Dictionary<Guid, string>? customAttributes)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        store.UpdateTicketAttributeValues(number, ticket.Category, customAttributes);
        Message = "Custom attributes updated.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdateContent(int number, string? title, string? description)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        if (string.IsNullOrWhiteSpace(title))
        {
            Message = "Enter a ticket title.";
            return RedirectToPage(new { number });
        }

        store.UpdateTicket(ticket with
        {
            Title = title.Trim(),
            Description = (description ?? string.Empty).Trim()
        });
        Message = "Ticket details updated.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostAddComment(int number, string? comment, bool closeTicket = false, bool reopenTicket = false)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            Message = "Enter a comment before saving.";
            return RedirectToPage(new { number });
        }

        if (!store.AddTicketComment(number, comment))
        {
            Message = "Ticket was not found.";
            return RedirectToPage(new { number });
        }

        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();

        if (closeTicket && ticket.Status != "Closed")
        {
            store.UpdateTicket(ticket with { Status = "Closed", ClosedAt = ticket.ClosedAt ?? DateTime.UtcNow });
            Message = "Comment added and ticket closed.";
        }
        else if (reopenTicket && ticket.Status == "Closed")
        {
            var reopenStatus = store.Statuses.FirstOrDefault(x => !string.Equals(x, "Closed", StringComparison.OrdinalIgnoreCase)) ?? ticket.Status;
            store.UpdateTicket(ticket with { Status = reopenStatus, ClosedAt = null });
            Message = "Comment added and ticket reopened.";
        }
        else
        {
            Message = "Comment added.";
        }

        return RedirectToPage(new { number });
    }

    public IActionResult OnPostClose(int number, string? closingMessage)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();

        if (ticket.Status == "Closed")
        {
            Message = "Job is already closed.";
            return RedirectToPage(new { number });
        }

        if (store.RequiresCloseMessage(ticket) && string.IsNullOrWhiteSpace(closingMessage))
        {
            Message = $"A closing message is required for {ticket.Priority} priority / {ticket.Category} tickets before they can be closed.";
            return RedirectToPage(new { number });
        }

        if (!string.IsNullOrWhiteSpace(closingMessage))
            store.AddTicketComment(number, closingMessage);

        var current = store.Tickets.FirstOrDefault(x => x.Number == number) ?? ticket;
        store.UpdateTicket(current with
        {
            Status = "Closed",
            ClosedAt = current.ClosedAt ?? DateTime.UtcNow
        });
        Message = "Job closed.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostDelete(int number)
    {
        var message = store.DeleteTicket(number);
        if (message is not null)
        {
            Message = message;
            return RedirectToPage(new { number });
        }
        Message = $"Ticket #{number} deleted.";
        return RedirectToPage("/Jobs");
    }

    public IActionResult OnPostMerge(int number, int targetNumber)
    {
        var message = store.MergeTicket(number, targetNumber);
        if (message is not null)
        {
            Message = message;
            return RedirectToPage(new { number });
        }
        Message = $"Ticket #{number} was merged into ticket #{targetNumber}.";
        return RedirectToPage(new { number = targetNumber });
    }
}
