using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// A requester's view of their own project: where it has got to and who has it, with the shared notes. Internal notes,
// quotes and prices stay on the helpdesk side.
public class ProjectModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public ProjectRecord? Project { get; private set; }
    public string? TechnicianName { get; private set; }
    public IReadOnlyList<ProjectNote> Notes => Project is null ? [] : Project.Notes.Where(x => !x.IsInternal).OrderBy(x => x.CreatedAt).ToList();
    [BindProperty] public string Note { get; set; } = "";
    [TempData] public string? Message { get; set; }

    // NotFound for both "doesn't exist" and "not yours", as on portal tickets.
    public IActionResult OnGet(int number)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        Project = store.Projects.FirstOrDefault(x => x.Number == number && x.RequesterId == id);
        if (Project is null) return NotFound();
        TechnicianName = Project.TechnicianId is { } tech ? store.Technicians.FirstOrDefault(x => x.Id == tech)?.Name : null;
        return Page();
    }

    public IActionResult OnPostNote(int number)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        if (!store.Projects.Any(x => x.Number == number && x.RequesterId == id)) return NotFound();
        // Always a shared note - a requester can't write one the IT team would then hide from them.
        var (_, message) = store.AddProjectNote(number, Note, isInternal: false);
        Message = message == "Note added." ? "Sent." : message;
        return RedirectToPage(new { number });
    }
}
