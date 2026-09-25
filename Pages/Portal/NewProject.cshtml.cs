using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// A line manager or SLT member asking IT to source something. Only requesters ticked "Can raise projects" get here; the
// store checks the same thing again, so a hand-made post from anyone else is refused too.
public class NewProjectModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public UserRecord? CurrentUser { get; private set; }
    public IReadOnlyList<string> RequirementOptions => store.PurchasingRequirements;
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public DateOnly? DueDate { get; set; }
    [BindProperty] public string ItemsWanted { get; set; } = "";
    [BindProperty] public List<string> Requirements { get; set; } = [];
    [BindProperty] public string? Other { get; set; }
    [BindProperty] public int Priority { get; set; } = ProjectPriorities.Default;

    public IActionResult OnGet()
    {
        if (Signin() is { } redirect) return redirect;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (Signin() is { } redirect) return redirect;
        var (ok, message, number) = store.RaiseProject(CurrentUser!.Id, Title, DueDate, ItemsWanted, Requirements, Other, Priority);
        if (!ok)
        {
            // The store's message says it in plain words; the framework's own "field is required" lines would repeat it.
            ModelState.Clear();
            ModelState.AddModelError("", message);
            return Page();
        }
        TempData["Message"] = $"{message} You'll see updates here as it moves along.";
        return RedirectToPage("/Portal/Project", new { number });
    }

    private IActionResult? Signin()
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        CurrentUser = store.Users.FirstOrDefault(x => x.Id == id);
        return CurrentUser is { CanRaiseProjects: true } ? null : RedirectToPage("/Portal/Index");
    }
}
