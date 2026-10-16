using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

public class StatusesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyDictionary<string, string> Descriptions => store.StatusDescriptions;
    public bool Pauses(string status) => store.PausesSla(status);
    // A status can be kept to one desk ("Awaiting contractor" for Site); the choice only shows with more than one desk.
    public bool HasSeveralDesks => store.HasSeveralDesks;
    public IReadOnlyList<Models.Desk> Desks => store.Desks;
    public Models.Desk? DeskFor(string status) => store.DeskForStatus(status);
    [TempData] public string? Message { get; set; }

    public IActionResult OnPostSetPause(string status, bool pauses)
    {
        Message = store.SetStatusPausesSla(status, pauses);
        return RedirectToPage();
    }

    public IActionResult OnPostAdd(string value, Guid? deskId)
    {
        Message = store.AddTicketOption("Status", value, deskId);
        return RedirectToPage();
    }

    public IActionResult OnPostDesk(string status, Guid? deskId)
    {
        Message = store.SetStatusDesk(status, deskId).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostRename(string currentValue, string value)
    {
        Message = store.UpdateTicketOption("Status", currentValue, value);
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(string value)
    {
        Message = store.DeleteTicketOption("Status", value);
        return RedirectToPage();
    }

    public IActionResult OnPostSaveDescription(string status, string? description)
    {
        Message = store.SetStatusDescription(status, description);
        return RedirectToPage();
    }
}
