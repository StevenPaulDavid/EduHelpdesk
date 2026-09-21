using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class SlasModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    public IActionResult OnPost(string name, int duration, string durationUnit, string? description, string[]? priorities, string[]? categories)
    {
        TempData["Message"] = store.AddSla(name, duration, durationUnit, description, priorities, categories);
        return RedirectToPage();
    }
    public IActionResult OnPostDelete(Guid id) { TempData["Message"] = store.DeleteSla(id); return RedirectToPage(); }
}
