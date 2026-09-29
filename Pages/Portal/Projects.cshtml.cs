using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// A requester's own projects. The project lead also gets the whole school's, starting with the ones waiting for them to
// assign. Someone whose tick has since been taken away still sees what they raised before; they just can't raise another.
public class ProjectsModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public static readonly string[] LeadViews = ["awaiting", "open", "mine", "closed"];
    public static string ViewLabel(string view) => view switch
    {
        "awaiting" => "Awaiting assignment",
        "open" => "All open",
        "closed" => "Closed",
        _ => "Raised by me"
    };

    [BindProperty(SupportsGet = true, Name = "view")] public string View { get; set; } = "";
    public UserRecord? CurrentUser { get; private set; }
    public bool IsLead => CurrentUser?.IsProjectLead == true && CurrentUser.IsActive;
    public IReadOnlyList<ProjectRecord> Projects { get; private set; } = [];
    public IReadOnlyDictionary<string, int> ViewCounts { get; private set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<Guid, string> TechnicianNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, string> RequesterNames { get; private set; } = new Dictionary<Guid, string>();

    public IActionResult OnGet()
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == id);
        TechnicianNames = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
        RequesterNames = store.Users.ToDictionary(x => x.Id, x => x.Name);
        var all = store.Projects;
        if (IsLead)
        {
            ViewCounts = LeadViews.ToDictionary(x => x, x => all.Count(p => InView(p, x, id.Value)));
            // "View projects" lands on what needs the lead first, or on everything open when nothing is waiting.
            if (!LeadViews.Contains(View)) View = ViewCounts["awaiting"] > 0 ? "awaiting" : "open";
            // Oldest first on the queues the lead works through; newest first for the ones already dealt with.
            Projects = View == "closed"
                ? all.Where(p => InView(p, View, id.Value)).ToList()
                : all.Where(p => InView(p, View, id.Value)).OrderBy(p => p.DueDate).ThenBy(p => p.EffectivePriority).ToList();
            return Page();
        }
        View = "mine";
        Projects = all.Where(x => x.RequesterId == id).ToList();
        return CurrentUser is { MayRaiseProjects: false } && Projects.Count == 0 ? RedirectToPage("/Portal/Index") : Page();
    }

    private static bool InView(ProjectRecord project, string view, Guid me) => view switch
    {
        "awaiting" => project.IsActive && project.TechnicianId is null,
        "open" => project.IsActive,
        "closed" => !project.IsActive,
        _ => project.RequesterId == me
    };
}
