using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Access;

// The DfE access control register as a list: who has access to which system or area, and everything wrong with it
// (AccessRules) - leavers still holding access, privileged access nobody approved, MFA off, reviews overdue. Live access
// by default; removed access is kept and can be shown. Opens at Access control: Access.
public class IndexModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "show")] public string? Show { get; set; }
    [BindProperty(SupportsGet = true, Name = "system")] public Guid? SystemId { get; set; }
    [BindProperty(SupportsGet = true, Name = "kind")] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "type")] public string? PersonType { get; set; }
    [TempData] public string? Message { get; set; }

    public static readonly string[] Shows = ["issues", "leaver", "approval", "mfa", "review", "privileged", "removed", "all"];
    public static string ShowLabel(string? show) => show switch
    {
        "issues" => "Anything to fix",
        "leaver" => "Leavers still with access",
        "approval" => "Privileged, not approved",
        "mfa" => "MFA not enabled",
        "review" => "Review overdue",
        "privileged" => "Privileged (administrator) access",
        "removed" => "Removed access only",
        "all" => "Live and removed",
        _ => show ?? ""
    };
    private static string? IssueKind(string? show) => show switch { "leaver" => "Leaver", "approval" => "Approval", "mfa" => "Mfa", "review" => "Review", _ => null };

    public bool CanView => store.UserCan(User, Modules.Access, ModulePermission.View);
    public bool CanAdd => store.UserCan(User, Modules.Access, ModulePermission.New);
    public bool CanEdit => store.UserCan(User, Modules.Access, ModulePermission.Edit);
    public bool CanOpenPeople => store.UserCan(User, Modules.Requesters, ModulePermission.View);

    public DateOnly Today { get; } = AssetInsights.Today;
    public int ReviewDays => store.AccessReviewDays;
    public sealed record Row(AccessGrant Grant, UserRecord? Person, AccessResource? Resource, IReadOnlyList<AccessRules.Issue> Issues);
    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public IReadOnlyList<AccessResource> Resources { get; private set; } = [];
    public IReadOnlyList<string> PersonTypes => store.PersonTypes;
    public ColumnSet Columns { get; private set; } = null!;

    // The figures across the top, for the whole register.
    public int LiveCount { get; private set; }
    public int PeopleWithAccess { get; private set; }
    public int ToFix { get; private set; }
    public int TotalCount { get; private set; }
    public AccessReview? LastReview { get; private set; }
    public DateOnly? NextReviewDue { get; private set; }

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || PanelFilterCount > 0;
    public int PanelFilterCount => new[] { Show, Kind, PersonType }.Count(x => !string.IsNullOrWhiteSpace(x)) + (SystemId.HasValue ? 1 : 0);

    public void OnGet()
    {
        Columns = ListColumns.For(store, User, "access");
        Load();
    }

    // The register as filtered, for an auditor. It names people, so it needs View, as a grant does.
    public IActionResult OnGetExport()
    {
        if (!CanView) return Forbid();
        Load();
        static string Day(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "";
        var csv = Csv.Table(
            ["Name", "Email", "Type", "Left", "System or area", "Kind", "Category", "Access level", "Account / key / fob", "Privileged", "MFA",
             "Granted on", "Granted by", "Approved by", "Approved on", "Last reviewed on", "Last reviewed by", "Removed on", "Removed by", "Reason for removal", "Issues", "Notes"],
            Rows,
            x => [x.Person?.Name ?? "", x.Person?.Email ?? "", x.Person is null ? "" : AccessRules.PersonType(x.Person), x.Person is null ? "" : Day(AccessRules.LeaveDate(x.Person)),
                  x.Resource?.Name ?? "", x.Resource is null ? "" : AccessKinds.Label(x.Resource.Kind), x.Resource?.Category ?? "", x.Grant.AccessLevel, x.Grant.Identifier,
                  x.Grant.Privileged ? "Yes" : "No", x.Grant.Mfa, Day(x.Grant.GrantedOn), x.Grant.GrantedBy, x.Grant.ApprovedBy, Day(x.Grant.ApprovedOn),
                  Day(x.Grant.LastReviewedOn), x.Grant.LastReviewedBy, Day(x.Grant.RevokedOn), x.Grant.RevokedBy, x.Grant.RevokeReason,
                  string.Join("; ", x.Issues.Select(i => i.Text)), x.Grant.Notes]);
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName("access-control-register", DateTime.Now));
    }

    private void Load()
    {
        if (!Shows.Contains(Show)) Show = null;
        Resources = store.AccessResources;
        var people = store.Users.ToDictionary(x => x.Id);
        var resources = Resources.ToDictionary(x => x.Id);
        var all = store.AccessGrants.Select(g =>
        {
            var person = people.GetValueOrDefault(g.PersonId);
            var resource = resources.GetValueOrDefault(g.ResourceId);
            return new Row(g, person, resource, AccessRules.Issues(g, person, resource, Today, ReviewDays));
        }).ToList();

        var live = all.Where(x => x.Grant.IsActive(Today)).ToList();
        TotalCount = all.Count;
        LiveCount = live.Count;
        PeopleWithAccess = live.Select(x => x.Grant.PersonId).Distinct().Count();
        ToFix = live.Count(x => x.Issues.Count > 0);
        LastReview = store.AccessReviews.FirstOrDefault();
        NextReviewDue = AccessRules.NextReviewDue(store.AccessReviews, ReviewDays);

        IEnumerable<Row> query = Show switch
        {
            "removed" => all.Where(x => !x.Grant.IsActive(Today)),
            "all" => all,
            _ => live
        };
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(x => terms.All(t => new[] { x.Person?.Name, x.Person?.Email, x.Resource?.Name, x.Grant.AccessLevel, x.Grant.Identifier, x.Grant.Notes }
                .Any(v => (v ?? "").Contains(t, StringComparison.OrdinalIgnoreCase))));
        }
        if (SystemId is { } system) query = query.Where(x => x.Grant.ResourceId == system);
        if (!string.IsNullOrWhiteSpace(Kind)) query = query.Where(x => x.Resource?.Kind == AccessKinds.Find(Kind));
        if (!string.IsNullOrWhiteSpace(PersonType)) query = query.Where(x => x.Person is not null && string.Equals(AccessRules.PersonType(x.Person), PersonType, StringComparison.OrdinalIgnoreCase));
        if (Show == "issues") query = query.Where(x => x.Issues.Count > 0);
        else if (Show == "privileged") query = query.Where(x => x.Grant.Privileged);
        else if (IssueKind(Show) is { } kind) query = query.Where(x => x.Issues.Any(i => i.Kind == kind));
        // Problems first, then by person and system.
        Rows = query.OrderBy(x => x.Issues.Count == 0 ? 1 : 0).ThenBy(x => x.Person?.Name, NaturalComparer.Instance).ThenBy(x => x.Resource?.Name, NaturalComparer.Instance).ToList();
    }

    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        string? Without(string key) { var route = Route(); route[key] = null; return Url.Page("/Access/Index", route); }
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q")));
        if (!string.IsNullOrWhiteSpace(Show)) chips.Add(new(ShowLabel(Show), Without("show")));
        if (SystemId is { } system) chips.Add(new($"System: {Resources.FirstOrDefault(x => x.Id == system)?.Name ?? "Unknown"}", Without("system")));
        if (!string.IsNullOrWhiteSpace(Kind)) chips.Add(new(AccessKinds.Label(AccessKinds.Find(Kind)), Without("kind")));
        if (!string.IsNullOrWhiteSpace(PersonType)) chips.Add(new($"Type: {PersonType}", Without("type")));
        return chips;
    }

    public Dictionary<string, object?> Route() => new()
    {
        ["q"] = Blank(Search), ["show"] = Blank(Show), ["system"] = SystemId, ["kind"] = Blank(Kind), ["type"] = Blank(PersonType)
    };
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
