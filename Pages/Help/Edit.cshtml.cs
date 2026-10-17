using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Help;

// Adding or changing one of the school's own help articles (Settings: Edit, Program.cs). With a preview, since the
// formatting is Markdown.
public class EditModel(HelpdeskStore store) : PageModel
{
    public Guid? Id { get; private set; }
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Summary { get; set; }
    [BindProperty] public string? Body { get; set; }
    public string? Error { get; private set; }
    public HelpMarkdown.Result? Preview { get; private set; }

    public IActionResult OnGet(Guid? id)
    {
        if (id is { } given)
        {
            if (store.GetSchoolHelpArticle(given) is not { } article) return NotFound();
            Id = given;
            (Title, Summary, Body) = (article.Title, article.Summary, article.Body);
        }
        return Page();
    }

    public IActionResult OnPost(Guid? id, string? preview)
    {
        Id = id;
        if (preview is not null)
        {
            Preview = HelpLinks.Render(Url, Body ?? "");
            return Page();
        }
        var (ok, message, savedId) = store.SaveSchoolHelpArticle(id, Title, Summary, Body);
        if (!ok) { Error = message; return Page(); }
        TempData["Message"] = message;
        return RedirectToPage("/Help/Article", new { slug = HelpdeskStore.SchoolSlug(savedId!.Value) });
    }
}
