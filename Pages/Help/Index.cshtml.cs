using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace EduHelpdesk.Pages.Help;

// The help centre: search, Getting started for what this person does, then every article by area. Everyone sees every
// article; one about a part their role can't open is marked rather than hidden (HelpCentre.CanUse).
public class IndexModel(HelpdeskStore store, HelpCentre help) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Query { get; set; }
    public IReadOnlyList<HelpCentre.Article> All { get; private set; } = [];
    public IReadOnlyList<HelpCentre.Hit> Hits { get; private set; } = [];
    public IReadOnlyList<HelpCentre.Article> StartHere { get; private set; } = [];
    public bool CanEdit => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public bool CanUse(HelpCentre.Article article) => HelpCentre.CanUse(article, store, User);
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        All = [.. help.Articles.Where(x => !HelpCentre.SwitchedOff(x, store)), .. store.SchoolHelpAsArticles()];
        if (!string.IsNullOrWhiteSpace(Query)) Hits = HelpCentre.Search(All, Query);
        StartHere = help.Articles.Where(x => x.Kind == "start" && CanUse(x)).ToList();
    }

    public IEnumerable<IGrouping<string, HelpCentre.Article>> ByArea() =>
        All.Where(x => x.Kind != "start").GroupBy(x => x.Area).OrderBy(g => Array.IndexOf(HelpCentre.Areas, g.Key));

    // The one-off welcome note in the layout: once dismissed, never shown to this account again.
    public IActionResult OnPostDismissWelcome(string? returnUrl)
    {
        if (Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var account)) store.SetPreference(account, HelpdeskStore.HelpWelcomePreference, "dismissed");
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/"));
    }
}
