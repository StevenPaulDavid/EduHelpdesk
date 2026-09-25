using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// A project seen from the staff portal: where it has got to and who has it, with the shared notes. The requester sees
// their own; the project lead sees any, and assigns it from here. Internal notes, quotes and prices stay on the
// helpdesk side for both.
public class ProjectModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public ProjectRecord? Project { get; private set; }
    public string? TechnicianName { get; private set; }
    public string? RequesterName { get; private set; }
    public bool IsLead { get; private set; }
    public bool IsOwnProject { get; private set; }
    public IReadOnlyList<ProjectWorkload> Workloads { get; private set; } = [];
    public IReadOnlyList<ProjectNote> Notes => Project is null ? [] : Project.Notes.Where(x => !x.IsInternal).OrderBy(x => x.CreatedAt).ToList();
    [BindProperty] public string Note { get; set; } = "";
    [TempData] public string? Message { get; set; }

    // NotFound for both "doesn't exist" and "not yours", as on portal tickets.
    public IActionResult OnGet(int number)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        Project = store.PortalProject(id.Value, number);
        if (Project is null) return NotFound();
        IsLead = store.IsActiveProjectLead(id.Value);
        IsOwnProject = Project.RequesterId == id;
        TechnicianName = Project.TechnicianId is { } tech ? store.Technicians.FirstOrDefault(x => x.Id == tech)?.Name : null;
        RequesterName = store.Users.FirstOrDefault(x => x.Id == Project.RequesterId)?.Name;
        if (IsLead && Project.IsActive) Workloads = store.ProjectWorkloads();
        return Page();
    }

    public IActionResult OnPostNote(int number)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        if (store.PortalProject(id.Value, number) is null) return NotFound();
        // Always a shared note - a portal user can't write one the IT team would then hide from the requester.
        var (_, message) = store.AddProjectNote(number, Note, isInternal: false);
        Message = message == "Note added." ? "Sent." : message;
        return RedirectToPage(new { number });
    }

    // The lead's decision: the priority that counts, and who does the work. Checked against the stored tick on every
    // post, not against what the page showed when it loaded.
    public IActionResult OnPostAssign(int number, string? technicianId, int priority)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        if (store.PortalProject(id.Value, number) is null) return NotFound();
        if (!store.IsActiveProjectLead(id.Value))
        {
            Message = "Only the project lead can assign projects.";
            return RedirectToPage(new { number });
        }
        Guid? technician = null;
        if (!string.IsNullOrWhiteSpace(technicianId))
        {
            if (!Guid.TryParse(technicianId, out var techId))
            {
                Message = "That technician couldn't be found.";
                return RedirectToPage(new { number });
            }
            technician = techId;
        }
        Message = store.AssignProject(number, technician, priority).Message;
        return RedirectToPage(new { number });
    }
}
