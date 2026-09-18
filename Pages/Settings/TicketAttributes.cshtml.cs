using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class TicketAttributesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<TicketAttributeDefinition> Attributes => store.TicketAttributeDefinitions;
    public IActionResult OnPost(string name, string? category, string fieldType, string? choices) { TempData["Message"] = store.AddTicketAttributeDefinition(name, category, fieldType, choices); return RedirectToPage(); }
    public IActionResult OnPostDelete(Guid id) { TempData["Message"] = store.DeleteTicketAttributeDefinition(id); return RedirectToPage(); }
}
