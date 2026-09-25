using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// The requester's own projects. Someone whose "Can raise projects" tick has since been taken away still sees what they
// raised before; they just can't raise another.
public class ProjectsModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    public IReadOnlyList<ProjectRecord> Projects { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> TechnicianNames { get; private set; } = new Dictionary<Guid, string>();

    public IActionResult OnGet()
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == id);
        Projects = store.Projects.Where(x => x.RequesterId == id).ToList();
        TechnicianNames = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
        return CurrentUser is { CanRaiseProjects: false } && Projects.Count == 0 ? RedirectToPage("/Portal/Index") : Page();
    }
}
