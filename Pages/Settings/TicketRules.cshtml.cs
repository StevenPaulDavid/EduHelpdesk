using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Ticket queues & closing. Needs Settings: Edit (Program.cs).
public class TicketRulesModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public IFormFile? PrintTemplate { get; set; }
    public int TicketDueSoonHours => store.TicketDueSoonHours;
    public int ReopenWindowDays => store.ReopenWindowDays;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> RequireCloseMessagePriorities => store.RequireCloseMessagePriorities;
    public IReadOnlyList<string> RequireCloseMessageCategories => store.RequireCloseMessageCategories;
    public bool HasPrintTemplate => store.HasPrintTemplate;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostSaveTicketDueSoon(int hours)
    {
        Message = store.SetTicketDueSoonHours(hours);
        return RedirectToPage();
    }

    public IActionResult OnPostSaveReopenWindow(int days)
    {
        Message = store.SetReopenWindowDays(days);
        return RedirectToPage();
    }

    public IActionResult OnPostSaveCloseRequirements(string[]? priorities, string[]? categories)
    {
        Message = store.SetCloseMessageRequirements(priorities, categories);
        return RedirectToPage();
    }

    public IActionResult OnPostUploadPrintTemplate()
    {
        if (PrintTemplate is null || PrintTemplate.Length == 0 || !Path.GetExtension(PrintTemplate.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            Message = "Choose a .docx Word document.";
            return RedirectToPage();
        }
        using var stream = PrintTemplate.OpenReadStream();
        store.SavePrintTemplate(stream);
        Message = "Print template uploaded.";
        return RedirectToPage();
    }
}
