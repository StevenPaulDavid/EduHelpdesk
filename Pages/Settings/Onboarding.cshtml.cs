using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Onboarding: the checklist templates a new starter's onboarding is started from - one per kind of staff.
// Gated like the rest of /Settings (Settings: Edit) by the folder convention in Program.cs.
public class OnboardingModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<OnboardingTemplate> Templates => store.OnboardingTemplates;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostAdd(string? name, Guid? copyFrom)
    {
        var (ok, message, id) = store.AddOnboardingTemplate(name, copyFrom);
        Message = message;
        return ok ? RedirectToPage(null, null, $"template-{id}") : RedirectToPage();
    }

    public IActionResult OnPostRename(Guid id, string? name) => Done(id, store.RenameOnboardingTemplate(id, name));
    public IActionResult OnPostDelete(Guid id)
    {
        Message = store.DeleteOnboardingTemplate(id).Message;
        return RedirectToPage();
    }
    public IActionResult OnPostAddTask(Guid id, string? title, string? stage, int offsetDays, string? owner) =>
        Done(id, store.AddOnboardingTemplateTask(id, title, stage, offsetDays, owner));
    public IActionResult OnPostUpdateTask(Guid id, Guid taskId, string? title, string? stage, int offsetDays, string? owner) =>
        Done(id, store.UpdateOnboardingTemplateTask(id, taskId, title, stage, offsetDays, owner));
    public IActionResult OnPostDeleteTask(Guid id, Guid taskId) => Done(id, store.DeleteOnboardingTemplateTask(id, taskId));

    // Back to the template that was changed, rather than the top of a long page.
    private IActionResult Done(Guid id, (bool Ok, string Message) result)
    {
        Message = result.Message;
        return RedirectToPage(null, null, $"template-{id}");
    }

    // "5 days before", "on the day", "3 days after" - how the offset reads next to the number box.
    public static string DescribeOffset(int days) => days switch
    {
        0 => "on their first day",
        < 0 => $"{-days} day{(days == -1 ? "" : "s")} before they start",
        _ => $"{days} day{(days == 1 ? "" : "s")} after they start"
    };
}
