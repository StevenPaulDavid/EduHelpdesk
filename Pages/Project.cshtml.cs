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
    public DateTime Now { get; } = DateTime.UtcNow;
    public IReadOnlyList<SupplierRecord> Suppliers { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> SupplierNames { get; private set; } = new Dictionary<Guid, string>();
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
        Suppliers = store.Suppliers;
        SupplierNames = Suppliers.ToDictionary(x => x.Id, x => x.Name);
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

    // Items, sub-items and the suppliers quoting for them. All are Projects: Edit - they are the technician's day-to-day
    // work on the project. Adding a brand-new supplier from here is included: asking a new firm for a quote is part of
    // the job, and the Supplier directory's own permissions still govern editing and deleting them afterwards.
    public IActionResult OnPostAddItem(int number, string? name, int quantity) => Edit(number, null, () => store.AddProjectItem(number, name, quantity));
    public IActionResult OnPostItemsFromRequest(int number) => Edit(number, null, () => store.AddItemsFromRequest(number));
    public IActionResult OnPostUpdateItem(int number, Guid itemId, string? name, int quantity) => Edit(number, itemId, () => store.UpdateProjectItem(number, itemId, name, quantity));
    public IActionResult OnPostDeleteItem(int number, Guid itemId) => Edit(number, null, () => store.DeleteProjectItem(number, itemId));
    public IActionResult OnPostMoveItem(int number, Guid itemId, int direction) => Edit(number, itemId, () => store.MoveProjectItem(number, itemId, direction));
    public IActionResult OnPostAddSubItem(int number, Guid itemId, string? name, int quantity) => Edit(number, itemId, () => store.AddSubItem(number, itemId, name, quantity));
    public IActionResult OnPostUpdateSubItem(int number, Guid itemId, Guid subItemId, string? name, int quantity) => Edit(number, itemId, () => store.UpdateSubItem(number, itemId, subItemId, name, quantity));
    public IActionResult OnPostDeleteSubItem(int number, Guid itemId, Guid subItemId) => Edit(number, itemId, () => store.DeleteSubItem(number, itemId, subItemId));
    public IActionResult OnPostAddSupplier(int number, Guid itemId, string? supplierId, string? newName, string? newEmail, bool everyItem) => Edit(number, itemId, () =>
        Guid.TryParse(supplierId, out var id) ? store.AddItemSupplier(number, itemId, id, everyItem)
        : !string.IsNullOrWhiteSpace(newName) ? store.QuickAddItemSupplier(number, itemId, newName, newEmail, everyItem)
        : (false, "Choose a supplier from the list, or type a new one's name."));
    public IActionResult OnPostQuoteStatus(int number, Guid itemId, Guid supplierId, string? status) => Edit(number, itemId, () => store.SetQuoteStatus(number, itemId, supplierId, status));
    public IActionResult OnPostValidUntil(int number, Guid itemId, Guid supplierId, DateOnly? validUntil) => Edit(number, itemId, () => store.SetQuoteValidUntil(number, itemId, supplierId, validUntil));
    public IActionResult OnPostRemoveSupplier(int number, Guid itemId, Guid supplierId) => Edit(number, itemId, () => store.RemoveItemSupplier(number, itemId, supplierId));

    // Back to the item that was just changed, rather than the top of a long page.
    private IActionResult Edit(int number, Guid? itemId, Func<(bool Ok, string Message)> change)
    {
        if (!CanEdit) return Forbid();
        Message = change().Message;
        return RedirectToPage(null, null, new { number }, itemId is { } id ? $"item-{id:N}" : "items");
    }

    private IActionResult Done(int number, (bool Ok, string Message) result)
    {
        Message = result.Message;
        return RedirectToPage(new { number });
    }
}
