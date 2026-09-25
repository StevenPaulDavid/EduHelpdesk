using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// A line manager or SLT member asking IT to source something. Only requesters ticked "Can raise projects" or "Project
// lead" get here; the store checks the same thing again, so a hand-made post from anyone else is refused too.
// For the lead the form is the whole job: their priority stands, and they can pick the technician there and then.
public class NewProjectModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    public bool IsLead => CurrentUser?.IsProjectLead == true;
    public IReadOnlyList<string> RequirementOptions => store.PurchasingRequirements;
    public IReadOnlyList<ProjectWorkload> Workloads { get; private set; } = [];
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public DateOnly? DueDate { get; set; }
    [BindProperty] public string ItemsWanted { get; set; } = "";
    [BindProperty] public List<string> Requirements { get; set; } = [];
    [BindProperty] public string? Other { get; set; }
    [BindProperty] public int Priority { get; set; } = ProjectPriorities.Default;
    [BindProperty] public string? TechnicianId { get; set; }

    public IActionResult OnGet()
    {
        if (Signin() is { } redirect) return redirect;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (Signin() is { } redirect) return redirect;
        // Blank means "assign later". Anything else must be a real id - a mangled form never silently drops the choice.
        Guid? technician = null;
        var error = (string?)null;
        if (!string.IsNullOrWhiteSpace(TechnicianId))
        {
            if (!IsLead) error = "Only the project lead can choose the technician.";
            else if (Guid.TryParse(TechnicianId, out var id)) technician = id;
            else error = "That technician couldn't be found.";
        }
        var (ok, message, number) = error is null
            ? store.RaiseProject(CurrentUser!.Id, Title, DueDate, ItemsWanted, Requirements, Other, Priority, technician)
            : (false, error, 0);
        if (!ok)
        {
            // The store's message says it in plain words; the framework's own "field is required" lines would repeat it.
            ModelState.Clear();
            ModelState.AddModelError("", message);
            return Page();
        }
        TempData["Message"] = IsLead ? message : $"{message} You'll see updates here as it moves along.";
        return RedirectToPage("/Portal/Project", new { number });
    }

    private IActionResult? Signin()
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == id);
        if (CurrentUser is not { MayRaiseProjects: true }) return RedirectToPage("/Portal/Index");
        if (IsLead) Workloads = store.ProjectWorkloads();
        return null;
    }
}
