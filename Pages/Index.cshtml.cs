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
    [BindProperty] public IFormFile? UserCsv { get; set; }
    [BindProperty] public IFormFile? TechnicianCsv { get; set; }
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostCreateTicket()
    {
        if (!ModelState.IsValid) return Page();
        var number = store.AddTicket(new TicketRecord(0, Ticket.Title.Trim(), Ticket.Description.Trim(), Ticket.RequesterId, Ticket.AssetId, Ticket.TechnicianId, Ticket.Priority, "Open", Ticket.Category, DateTime.UtcNow, null));
        Message = $"Job #{number} created and assigned.";
        return RedirectToPage();
    }

    public IActionResult OnPostCreateAsset()
    {
        if (!ModelState.IsValid) return Page();
        store.AddAsset(new AssetRecord(Guid.NewGuid(), Asset.AssetTag.Trim(), Asset.Type.Trim(), Asset.Model.Trim(), Asset.SerialNumber?.Trim() ?? "", Asset.Location?.Trim() ?? "", Asset.AssignedUserId));
        Message = $"Asset {Asset.AssetTag} added to the register.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportUsersAsync()
    {
        if (UserCsv is null || UserCsv.Length == 0) { Message = "Choose a users CSV file first."; return RedirectToPage(); }
        var records = new List<UserRecord>();
        using var reader = new StreamReader(UserCsv.OpenReadStream());
        await reader.ReadLineAsync();
        while (await reader.ReadLineAsync() is { } line)
        {
            var cells = line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
            if (cells.Length >= 4 && !string.IsNullOrWhiteSpace(cells[0]) && Mail(cells[1])) records.Add(new(Guid.NewGuid(), cells[0], cells[1], cells[2], cells[3]));
        }
        Message = $"{store.ImportUsers(records)} users imported.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportTechniciansAsync()
    {
        if (TechnicianCsv is null || TechnicianCsv.Length == 0) { Message = "Choose a technicians CSV file first."; return RedirectToPage(); }
        var records = new List<TechnicianRecord>();
        using var reader = new StreamReader(TechnicianCsv.OpenReadStream());
        await reader.ReadLineAsync();
        while (await reader.ReadLineAsync() is { } line)
        {
            var cells = line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
            if (cells.Length >= 3 && !string.IsNullOrWhiteSpace(cells[0]) && Mail(cells[1])) records.Add(new(Guid.NewGuid(), cells[0], cells[1], cells[2]));
        }
        Message = $"{store.ImportTechnicians(records)} technicians imported.";
        return RedirectToPage();
    }

    private static bool Mail(string value) => value.Contains('@', StringComparison.Ordinal) && value.Contains('.', StringComparison.Ordinal);
    public sealed class TicketInput { [BindProperty, System.ComponentModel.DataAnnotations.Required] public string Title { get; set; } = ""; [BindProperty, System.ComponentModel.DataAnnotations.Required] public string Description { get; set; } = ""; public Guid RequesterId { get; set; } public Guid? AssetId { get; set; } public Guid TechnicianId { get; set; } public string Priority { get; set; } = "Normal"; public string Category { get; set; } = "Hardware"; }
    public sealed class AssetInput { [System.ComponentModel.DataAnnotations.Required] public string AssetTag { get; set; } = ""; [System.ComponentModel.DataAnnotations.Required] public string Type { get; set; } = ""; [System.ComponentModel.DataAnnotations.Required] public string Model { get; set; } = ""; public string? SerialNumber { get; set; } public string? Location { get; set; } public Guid? AssignedUserId { get; set; } }
}
