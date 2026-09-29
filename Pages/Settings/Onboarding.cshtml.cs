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
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<OnboardingDocument> Documents => store.OnboardingDocuments;
    public string ItInfo => store.OnboardingItInfo;

    // ---- The welcome pack ----

    public IActionResult OnPostItInfo(string? text)
    {
        Message = store.SetOnboardingItInfo(text).Message;
        return RedirectToPage(null, null, "welcome-pack");
    }

    public IActionResult OnPostAddDocument(string? name, IFormFile? file)
    {
        if (file is null || file.Length == 0) Message = "Choose a PDF to upload.";
        else
        {
            using var stream = file.OpenReadStream();
            Message = store.AddOnboardingDocument(name, file.FileName, stream, file.Length).Message;
        }
        return RedirectToPage(null, null, "welcome-pack");
    }

    public IActionResult OnPostRenameDocument(Guid documentId, string? name)
    {
        Message = store.RenameOnboardingDocument(documentId, name).Message;
        return RedirectToPage(null, null, "welcome-pack");
    }

    public IActionResult OnPostDeleteDocument(Guid documentId)
    {
        Message = store.DeleteOnboardingDocument(documentId).Message;
        return RedirectToPage(null, null, "welcome-pack");
    }

    public IActionResult OnPostTemplateDocuments(Guid id, List<Guid>? documentIds) => Done(id, store.SetOnboardingTemplateDocuments(id, documentIds));

    // A PDF to check before it goes out. Served as a download: nothing uploaded is opened in the page.
    public IActionResult OnGetDocument(Guid documentId)
    {
        if (store.FindOnboardingDocument(documentId) is not { } found) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(found.Path, "application/pdf", found.Document.FileName);
    }
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
    public IActionResult OnPostAddTask(Guid id, string? title, string? stage, int offsetDays, string? owner, string? action) =>
        Done(id, store.AddOnboardingTemplateTask(id, title, stage, offsetDays, owner, action));
    public IActionResult OnPostUpdateTask(Guid id, Guid taskId, string? title, string? stage, int offsetDays, string? owner, string? action) =>
        Done(id, store.UpdateOnboardingTemplateTask(id, taskId, title, stage, offsetDays, owner, action));
    public IActionResult OnPostDeleteTask(Guid id, Guid taskId) => Done(id, store.DeleteOnboardingTemplateTask(id, taskId));

    // Back to the template that was changed, rather than the top of a long page.
    private IActionResult Done(Guid id, (bool Ok, string Message) result)
    {
        Message = result.Message;
        return RedirectToPage(null, null, $"template-{id}");
    }

    // The "Does" dropdown on a task row: the chosen value (OnboardingActions.Encode) and the asset types to offer.
    public sealed record ActionField(string Value, IReadOnlyList<string> AssetTypes);

    // "5 days before", "on the day", "3 days after" - how the offset reads next to the number box.
    public static string DescribeOffset(int days) => days switch
    {
        0 => "on their first day",
        < 0 => $"{-days} day{(days == -1 ? "" : "s")} before they start",
        _ => $"{days} day{(days == 1 ? "" : "s")} after they start"
    };
}
