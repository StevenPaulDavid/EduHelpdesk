using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Help;

// One help article: a built-in one (from wwwroot/help/articles) with the school's note under its title, or one of the
// school's own (HelpdeskStore.Help).
public class ArticleModel(HelpdeskStore store, HelpCentre help) : PageModel
{
    public HelpCentre.Article Article { get; private set; } = null!;
    public HelpMarkdown.Result Rendered { get; private set; } = null!;
    public HelpNote? Note { get; private set; }
    public HelpMarkdown.Result? RenderedNote { get; private set; }
    public IReadOnlyList<HelpCentre.Article> Related { get; private set; } = [];
    public bool CanUse { get; private set; }
    public bool CanEdit => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public SchoolHelpArticle? School { get; private set; }
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(string slug)
    {
        if (!Load(slug)) return NotFound();
        return Page();
    }

    private bool Load(string slug)
    {
        if (HelpdeskStore.SchoolId(slug) is { } id)
        {
            School = store.GetSchoolHelpArticle(id);
            if (School is null) return false;
            Article = store.SchoolHelpAsArticles().Single(x => x.Slug == HelpdeskStore.SchoolSlug(id));
        }
        else if (help.Find(slug) is { } article) Article = article;
        else return false;
        Rendered = HelpLinks.Render(Url, Article.Body);
        CanUse = HelpCentre.CanUse(Article, store, User);
        if (!Article.IsSchool)
        {
            Note = store.HelpNoteFor(Article.Slug);
            if (Note is not null) RenderedNote = HelpLinks.Render(Url, Note.Text);
            Related = help.Articles.Where(x => x.Area == Article.Area && x.Slug != Article.Slug).Take(8).ToList();
        }
        return true;
    }

    public IActionResult OnPostNote(string slug, string? text)
    {
        if (!CanEdit) return Forbid();
        if (help.Find(slug) is null) return NotFound();
        Message = store.SaveHelpNote(slug, text).Message;
        return RedirectToPage(new { slug });
    }

    public IActionResult OnPostDelete(string slug)
    {
        if (!CanEdit) return Forbid();
        if (HelpdeskStore.SchoolId(slug) is not { } id) return NotFound();
        Message = store.DeleteSchoolHelpArticle(id).Message;
        return RedirectToPage("/Help/Index");
    }
}
