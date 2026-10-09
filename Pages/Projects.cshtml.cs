using System.Security.Claims;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class ProjectsModel(HelpdeskStore store) : PageModel
{
    public static readonly int[] PageSizes = [25, 50, 100];
    // The queues across the top. "due" is proposals needed within a week or already late - the thing a lead checks first.
    public static readonly string[] Views = ["active", "unassigned", "mine", "due", "closed", "all"];
    public static string ViewLabel(string view) => view switch
    {
        "unassigned" => "Awaiting assignment",
        "mine" => "Mine",
        "due" => "Due soon or late",
        "closed" => "Closed",
        "all" => "All",
        _ => "Active"
    };
    public static readonly string[] Sorts = ["due", "priority", "newest", "updated"];
    public static string SortLabel(string sort) => sort switch
    {
        "priority" => "Priority",
        "newest" => "Newest first",
        "updated" => "Recently updated",
        _ => "Needed by"
    };

    // "page" is reserved by routing, so paging uses p, as on the ticket list.
    [BindProperty(SupportsGet = true, Name = "view")] public string View { get; set; } = "active";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "tech")] public string? Technician { get; set; }
    [BindProperty(SupportsGet = true, Name = "priority")] public List<int> Priority { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "status")] public List<string> Status { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "sort")] public string Sort { get; set; } = "due";
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int Size { get; set; } = 25;

    public IReadOnlyList<ProjectRecord> Rows { get; private set; } = [];
    public IReadOnlyDictionary<string, int> ViewCounts { get; private set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<Guid, UserRecord> Requesters { get; private set; } = new Dictionary<Guid, UserRecord>();
    public IReadOnlyDictionary<Guid, string> TechnicianNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyList<TechnicianRecord> Technicians { get; private set; } = [];
    public int TotalProjects { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public Guid? SignedInTechnicianId { get; private set; }
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Technician) || Priority.Count > 0 || Status.Count > 0;

    public void OnGet()
    {
        if (!Views.Contains(View)) View = "active";
        if (!Sorts.Contains(Sort)) Sort = "due";
        if (!PageSizes.Contains(Size)) Size = 25;
        SignedInTechnicianId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var me) && store.Technicians.Any(x => x.Id == me) ? me : null;

        var all = store.Projects;
        TotalProjects = all.Count;
        Requesters = store.Users.ToDictionary(x => x.Id);
        TechnicianNames = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
        // Anyone who holds a project can be filtered on, including someone whose role has since lost Projects access.
        Technicians = store.Technicians.Where(x => store.CanWorkProjects(x) || all.Any(p => p.TechnicianId == x.Id))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ViewCounts = Views.ToDictionary(x => x, x => all.Count(p => InView(p, x)));

        IEnumerable<ProjectRecord> query = all.Where(x => InView(x, View));
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(x => terms.All(t => Matches(x, t)));
        }
        if (Technician == "none") query = query.Where(x => x.TechnicianId is null);
        else if (Guid.TryParse(Technician, out var techId)) query = query.Where(x => x.TechnicianId == techId);
        if (Priority.Count > 0) query = query.Where(x => Priority.Contains(x.EffectivePriority));
        if (Status.Count > 0) query = query.Where(x => Status.Contains(x.Status, StringComparer.OrdinalIgnoreCase));

        query = Sort switch
        {
            "priority" => query.OrderBy(x => x.EffectivePriority).ThenBy(x => x.DueDate),
            "newest" => query.OrderByDescending(x => x.Number),
            "updated" => query.OrderByDescending(x => x.LastModifiedAt),
            // Closed projects stay in due-date order too, so a lead reading back over the term sees them as they fell.
            _ => query.OrderBy(x => x.DueDate).ThenBy(x => x.EffectivePriority)
        };
        var matches = query.ToList();
        MatchCount = matches.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)Size));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Rows = matches.Skip((PageNumber - 1) * Size).Take(Size).ToList();
    }

    private bool InView(ProjectRecord project, string view) => view switch
    {
        "unassigned" => project.IsActive && project.TechnicianId is null,
        "mine" => project.IsActive && SignedInTechnicianId is { } me && project.TechnicianId == me,
        "due" => project.IsOverdue(Today) || project.IsDueSoon(Today),
        "closed" => !project.IsActive,
        "all" => true,
        _ => project.IsActive
    };

    private bool Matches(ProjectRecord project, string term) =>
        Has(project.Reference, term) || Has(project.Title, term) || Has(project.ItemsWanted, term) || Has(project.Number.ToString(), term)
        || (Requesters.GetValueOrDefault(project.RequesterId) is { } requester && (Has(requester.Name, term) || Has(requester.Department, term)))
        || (project.TechnicianId is { } tech && Has(TechnicianNames.GetValueOrDefault(tech, ""), term));

    private static bool Has(string? text, string term) => (text ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

    public string ViewUrl(string view) => Url.Page("/Projects", new { view = view == "active" ? null : view })!;
    public string ClearUrl() => ViewUrl(View);
    public string PageUrl(int page) => Url.Page("/Projects", Route(page))!;

    // Arrays, not lists: an array becomes one query-string entry per value, which is what the filter binding reads back.
    private Dictionary<string, object?> Route(int page) => new()
    {
        ["view"] = View == "active" ? null : View,
        ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["tech"] = string.IsNullOrWhiteSpace(Technician) ? null : Technician,
        ["priority"] = Priority.Count == 0 ? null : Priority.ToArray(),
        ["status"] = Status.Count == 0 ? null : Status.ToArray(),
        ["sort"] = Sort == "due" ? null : Sort,
        ["size"] = Size == 25 ? null : Size,
        ["p"] = page > 1 ? page : null
    };

    // Filters in the Filters panel that are switched on - the number on its button. Sort and page size aren't filters.
    public int PanelFilterCount => Priority.Count + Status.Count + (string.IsNullOrWhiteSpace(Technician) ? 0 : 1);

    // Every filter in force as a chip that takes just that one off.
    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        string Without(string key, object? value) { var route = Route(1); route[key] = value; return Url.Page("/Projects", route)!; }
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q", null)));
        if (!string.IsNullOrWhiteSpace(Technician))
            chips.Add(new($"Technician: {(Technician == "none" ? "Unassigned" : Guid.TryParse(Technician, out var id) ? TechnicianNames.GetValueOrDefault(id, "Unknown") : Technician)}", Without("tech", null)));
        foreach (var level in Priority)
            chips.Add(new($"Priority: {ProjectPriorities.Label(level)}", Without("priority", Priority.Where(x => x != level).ToArray() is { Length: > 0 } rest ? rest : null)));
        foreach (var status in Status)
            chips.Add(new($"Status: {status}", Without("status", Status.Where(x => !string.Equals(x, status, StringComparison.OrdinalIgnoreCase)).ToArray() is { Length: > 0 } rest ? rest : null)));
        return chips;
    }

    // The list's money columns use the same VAT setting as the project page (Settings → Spending bands).
    public bool IncVat => store.ProjectPageIncludesVat;
    public string VatLabel => IncVat ? "inc. VAT" : "ex. VAT";
    public decimal ChosenTerm(ProjectRecord project) => IncVat ? project.ChosenTotals.TermIncVat : project.ChosenTotals.TermExVat;
}
