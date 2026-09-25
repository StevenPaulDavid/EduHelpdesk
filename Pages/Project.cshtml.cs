using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// One project, helpdesk side. The page needs Projects: View to open (Program.cs); every change is checked here, because
// tidying up, assigning and deleting are three different permissions on the same page.
public class ProjectModel(HelpdeskStore store) : PageModel
{
    public ProjectRecord? Project { get; private set; }
    public UserRecord? Requester { get; private set; }
    public TechnicianRecord? Technician { get; private set; }
    public IReadOnlyList<ProjectWorkload> Workloads { get; private set; } = [];
    public IReadOnlyList<string> RequirementOptions { get; private set; } = [];
    public bool CanEdit => store.UserCan(User, Modules.Projects, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Projects, ModulePermission.Delete);
    public bool CanAssign => store.UserHasFlag(User, Modules.Flags.AssignProjects);
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(int number)
    {
        Project = store.FindProject(number);
        if (Project is null) return NotFound();
        Requester = store.Users.FirstOrDefault(x => x.Id == Project.RequesterId);
        Technician = Project.TechnicianId is { } techId ? store.Technicians.FirstOrDefault(x => x.Id == techId) : null;
        // Everything already ticked on the project stays offered, even if it has since been taken off the Settings list.
        RequirementOptions = store.PurchasingRequirements
            .Concat(Project.PurchasingRequirements.Where(x => !store.PurchasingRequirements.Contains(x, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        if (CanAssign) Workloads = store.ProjectWorkloads();
        return Page();
    }

    public IActionResult OnPostDetails(int number, string? title, DateOnly? dueDate, string? itemsWanted, List<string>? requirements, string? other)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.UpdateProjectDetails(number, title, dueDate, itemsWanted, requirements, other));
    }

    public IActionResult OnPostAssign(int number, string? technicianId, int priority)
    {
        if (!CanAssign) return Forbid();
        // Blank is a deliberate "nobody yet"; anything else has to be a real id, so a mangled form can't quietly unassign.
        Guid? technician = null;
        if (!string.IsNullOrWhiteSpace(technicianId))
        {
            if (!Guid.TryParse(technicianId, out var id)) return Done(number, (false, "That technician couldn't be found."));
            technician = id;
        }
        return Done(number, store.AssignProject(number, technician, priority));
    }

    public IActionResult OnPostStatus(int number, string? status)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.SetProjectStatus(number, status));
    }

    public IActionResult OnPostClose(int number, string? outcome, string? note)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.CloseProject(number, outcome, note));
    }

    public IActionResult OnPostNote(int number, string? text, bool isInternal)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.AddProjectNote(number, text, isInternal));
    }

    public IActionResult OnPostDelete(int number)
    {
        if (!CanDelete) return Forbid();
        var (ok, message) = store.DeleteProject(number);
        Message = message;
        return ok ? RedirectToPage("/Projects") : RedirectToPage(new { number });
    }

    private IActionResult Done(int number, (bool Ok, string Message) result)
    {
        Message = result.Message;
        return RedirectToPage(new { number });
    }
}
