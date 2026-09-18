using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class IndexModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    public int OpenTickets => Tickets.Count(x => x.Status is not "Closed");
    [BindProperty] public TicketInput Ticket { get; set; } = new();
    [BindProperty] public AssetInput Asset { get; set; } = new();
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostCreateTicket()
    {
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(Ticket.Title)) ModelState.AddModelError("Ticket.Title", "Enter a job title.");
        if (string.IsNullOrWhiteSpace(Ticket.Description)) ModelState.AddModelError("Ticket.Description", "Enter a description.");
        if (Ticket.RequesterId == Guid.Empty) ModelState.AddModelError("Ticket.RequesterId", "Select a requester.");
        if (Ticket.TechnicianId == Guid.Empty) ModelState.AddModelError("Ticket.TechnicianId", "Select a technician.");
        if (!ModelState.IsValid) return Page();
        var number = store.AddTicket(new TicketRecord(0, Ticket.Title.Trim(), Ticket.Description.Trim(), Ticket.RequesterId, Ticket.AssetId, Ticket.TechnicianId, Ticket.Priority, "Open", Ticket.Category, DateTime.UtcNow, null));
        Message = $"Job #{number} created and assigned.";
        return RedirectToPage();
    }

    public IActionResult OnPostCreateAsset()
    {
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(Asset.AssetTag)) ModelState.AddModelError("Asset.AssetTag", "Enter an asset tag.");
        if (string.IsNullOrWhiteSpace(Asset.Type)) ModelState.AddModelError("Asset.Type", "Enter an asset type.");
        if (string.IsNullOrWhiteSpace(Asset.Model)) ModelState.AddModelError("Asset.Model", "Enter an asset model.");
        if (!ModelState.IsValid) return Page();
        store.AddAsset(new AssetRecord(Guid.NewGuid(), Asset.AssetTag.Trim(), Asset.Type.Trim(), Asset.Model.Trim(), Asset.SerialNumber?.Trim() ?? "", Asset.Location?.Trim() ?? "", Asset.AssignedUserId));
        Message = $"Asset {Asset.AssetTag} added to the register.";
        return RedirectToPage();
    }

    public sealed class TicketInput { [BindProperty, System.ComponentModel.DataAnnotations.Required] public string Title { get; set; } = ""; [BindProperty, System.ComponentModel.DataAnnotations.Required] public string Description { get; set; } = ""; public Guid RequesterId { get; set; } public Guid? AssetId { get; set; } public Guid TechnicianId { get; set; } public string Priority { get; set; } = "Normal"; public string Category { get; set; } = "Hardware"; }
    public sealed class AssetInput { [System.ComponentModel.DataAnnotations.Required] public string AssetTag { get; set; } = ""; [System.ComponentModel.DataAnnotations.Required] public string Type { get; set; } = ""; [System.ComponentModel.DataAnnotations.Required] public string Model { get; set; } = ""; public string? SerialNumber { get; set; } public string? Location { get; set; } public Guid? AssignedUserId { get; set; } }
}
