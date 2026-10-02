using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

public class TicketTemplatesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<TicketTemplate> Templates => store.TicketTemplates;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    public IReadOnlyList<TicketAttributeDefinition> Attributes => store.TicketAttributeDefinitions;

    // Adds a template, or saves changes to one when an id is given.
    public IActionResult OnPostSave(Guid? id, string? name, string? type, string? title, string? description, string? category, string? priority, Guid? slaId, Dictionary<Guid, string>? attributes, string? helperLine, bool showInPortal)
    {
        TempData["Message"] = id is { } existing
            ? store.UpdateTicketTemplate(existing, name, type, title, description, category, priority, slaId, attributes, helperLine, showInPortal)
            : store.AddTicketTemplate(name, type, title, description, category, priority, slaId, attributes, helperLine, showInPortal);
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        TempData["Message"] = store.DeleteTicketTemplate(id);
        return RedirectToPage();
    }
}
