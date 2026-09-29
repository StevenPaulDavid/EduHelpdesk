using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Onboarding;

// Starting an onboarding: who is coming, when, and which checklist. Needs Onboarding: New (Program.cs).
public class NewModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? JobTitle { get; set; }
    [BindProperty] public string? Department { get; set; }
    [BindProperty] public string? Location { get; set; }
    [BindProperty] public DateOnly? StartDate { get; set; }
    [BindProperty] public Guid? LineManagerId { get; set; }
    [BindProperty] public Guid? TemplateId { get; set; }
    [BindProperty] public Guid? TechnicianId { get; set; }

    public IReadOnlyList<OnboardingTemplate> Templates { get; private set; } = [];
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<UserRecord> People { get; private set; } = [];
    public IReadOnlyList<TechnicianRecord> Technicians { get; private set; } = [];

    public void OnGet()
    {
        Load();
        // The only template, or the teaching one, is the likeliest choice.
        TemplateId ??= (Templates.FirstOrDefault(x => x.Name.Contains("Teach", StringComparison.OrdinalIgnoreCase)) ?? Templates.FirstOrDefault())?.Id;
    }

    public IActionResult OnPost()
    {
        var details = new HelpdeskStore.OnboardingDetails(Name, Email, JobTitle, Department, Location, StartDate, LineManagerId);
        var (ok, message, number) = store.StartOnboarding(details, TemplateId, TechnicianId);
        if (!ok)
        {
            ModelState.AddModelError("", message);
            Load();
            return Page();
        }
        TempData["Message"] = message;
        return RedirectToPage("/Onboarding/Details", new { number });
    }

    private void Load()
    {
        Templates = store.OnboardingTemplates;
        People = store.Users.Where(x => x.IsActive && x.AnonymisedAt is null).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Technicians = store.Technicians.Where(x => x.IsActive).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
