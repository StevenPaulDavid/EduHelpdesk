using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

public class ServiceCatalogueModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<ServiceItem> Items => store.ServiceItems;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Priorities => store.Priorities;

    // Adds an item, or saves changes to one when an id is given.
    public IActionResult OnPostSave(Guid? id, string? category, string? name, string? priority)
    {
        TempData["Message"] = id is { } existing
            ? store.UpdateServiceItem(existing, category, name, priority)
            : store.AddServiceItem(category, name, priority);
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        TempData["Message"] = store.DeleteServiceItem(id);
        return RedirectToPage();
    }
}
