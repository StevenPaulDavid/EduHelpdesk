using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

public class StatusesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyDictionary<string, string> Descriptions => store.StatusDescriptions;
    [TempData] public string? Message { get; set; }

    public IActionResult OnPostAdd(string value)
    {
        Message = store.AddTicketOption("Status", value);
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
