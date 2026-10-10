using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Access;

// The termly access review the cyber security standard asks for, done "with your business professionals or the finance
// team": every live grant (or one system's) on one page, each kept or marked for removal, recorded in one step with
// who it was done with. Past reviews are the evidence. Reading needs Access; recording a review needs Edit.
public class ReviewModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "system")] public Guid? SystemId { get; set; }
    [TempData] public string? Message { get; set; }
    public string? Error { get; private set; }

    public bool CanReview => store.UserCan(User, Modules.Access, ModulePermission.Edit);
    public bool CanSetInterval => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public DateOnly Today { get; } = AssetInsights.Today;
    public int ReviewDays => store.AccessReviewDays;
    public IReadOnlyList<AccessReview> Reviews => store.AccessReviews;
    public DateOnly? NextDue => AccessRules.NextReviewDue(Reviews, ReviewDays);
    public IReadOnlyList<AccessResource> Resources { get; private set; } = [];
    public IReadOnlyList<IndexModel.Row> Rows { get; private set; } = [];
    public IReadOnlyList<string> RevokeReasons => store.RevokeReasons;

    // What was posted, kept when the review is refused so nothing ticked is lost.
    public string? ReviewedWith { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlySet<Guid> Marked { get; private set; } = new HashSet<Guid>();

    public void OnGet() => Load();

    public IActionResult OnPost(DateOnly? reviewedOn, string? reviewedWith, string? notes, string? reason, Guid[]? shown, Guid[]? remove)
    {
        if (!CanReview) return Forbid();
        var removeIds = (remove ?? []).ToHashSet();
        var confirmIds = (shown ?? []).Where(x => !removeIds.Contains(x)).ToHashSet();
        var scope = SystemId is { } system ? store.FindAccessResource(system)?.Name ?? "" : "All systems and areas";
        var (ok, message) = store.CompleteAccessReview(reviewedOn, reviewedWith, scope, notes, confirmIds, removeIds, reason);
        if (!ok)
        {
            Error = message;
            ReviewedWith = reviewedWith;
            Notes = notes;
            Marked = removeIds;
            Load();
            return Page();
        }
        Message = message;
        return RedirectToPage(new { system = SystemId });
    }

    public IActionResult OnPostInterval(int days)
    {
        if (!CanSetInterval) return Forbid();
        Message = store.SetAccessReviewDays(days);
        return RedirectToPage(new { system = SystemId });
    }

    private void Load()
    {
        Resources = store.AccessResources;
        var people = store.Users.ToDictionary(x => x.Id);
        var resources = Resources.ToDictionary(x => x.Id);
        Rows = store.AccessGrants.Where(x => x.IsActive(Today) && (SystemId is null || x.ResourceId == SystemId))
            .Select(g => new IndexModel.Row(g, people.GetValueOrDefault(g.PersonId), resources.GetValueOrDefault(g.ResourceId),
                AccessRules.Issues(g, people.GetValueOrDefault(g.PersonId), resources.GetValueOrDefault(g.ResourceId), Today, ReviewDays)))
            .OrderBy(x => x.Resource?.Name, NaturalComparer.Instance).ThenBy(x => x.Person?.Name, NaturalComparer.Instance).ToList();
    }
}
