using System.Text;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class JobsModel(HelpdeskStore store) : PageModel
{
    public static readonly int[] PageSizes = [25, 50, 100, 200];
    private const string MeCookie = "helpdesk_me";

    // Short query-string names. "area", "action" and "page" are reserved by routing, so they are avoided.
    [BindProperty(SupportsGet = true, Name = "view")] public string View { get; set; } = "open";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public List<string> Status { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "priority")] public List<string> Priority { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "category")] public List<string> Category { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "tech")] public string? Technician { get; set; }
    [BindProperty(SupportsGet = true, Name = "team")] public string? Team { get; set; }
    [BindProperty(SupportsGet = true, Name = "requester")] public string? Requester { get; set; }
    [BindProperty(SupportsGet = true, Name = "dept")] public string? Department { get; set; }
    [BindProperty(SupportsGet = true, Name = "asset")] public string? Asset { get; set; }
    [BindProperty(SupportsGet = true, Name = "sort")] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "dir")] public string? Dir { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int Size { get; set; } = 50;

    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<string> Teams => store.TechnicianTeams;
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyDictionary<string, string> StatusDescriptions => store.StatusDescriptions;
    public string StatusDescriptionsJson => System.Text.Json.JsonSerializer.Serialize(StatusDescriptions).Replace("</", "<\\/");
    public int DueSoonHours => store.TicketDueSoonHours;
    [TempData] public string? Message { get; set; }

    public IReadOnlyList<TicketRecord> Rows { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> UserNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, string> TechnicianNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, AssetRecord> AssetsById { get; private set; } = new Dictionary<Guid, AssetRecord>();
    public IReadOnlyDictionary<string, int> ViewCounts { get; private set; } = new Dictionary<string, int>();
    public int TotalTickets { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public DateTime Now { get; } = DateTime.UtcNow;
    public Guid? CurrentTechnicianId { get; private set; }
    public TechnicianRecord? CurrentTechnician => CurrentTechnicianId is { } id ? Technicians.FirstOrDefault(x => x.Id == id) : null;
    public bool Descending => Dir == "desc";
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || Status.Count > 0 || Priority.Count > 0 || Category.Count > 0 || !string.IsNullOrWhiteSpace(Technician)
        || !string.IsNullOrWhiteSpace(Team) || !string.IsNullOrWhiteSpace(Requester) || !string.IsNullOrWhiteSpace(Department) || !string.IsNullOrWhiteSpace(Asset);

    public void OnGet()
    {
        Prepare();
        var context = Context();
        var all = store.Tickets;
        var matches = BuildQuery().Run(all, context);
        TotalTickets = all.Count;
        ViewCounts = TicketListQuery.ViewCounts(all, context);
        MatchCount = matches.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)Size));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Rows = matches.Skip((PageNumber - 1) * Size).Take(Size).ToList();
        UserNames = context.Users.ToDictionary(x => x.Id, x => x.Name);
        TechnicianNames = context.Technicians.ToDictionary(x => x.Id, x => x.Name);
        AssetsById = context.Assets.ToDictionary(x => x.Id);
    }

    public IActionResult OnPostBulk(string? operation, int[]? ids, bool selectAll, string? newStatus, string? newPriority, string? newCategory, string? newTeam, string? newTechnician, string? commentText, bool commentInternal, string? closingMessage, int? mergeTarget)
    {
        Prepare();
        // "Select all matching" re-applies the current filters here, so it covers every page, not just the one on screen.
        var targets = selectAll ? BuildQuery().Run(store.Tickets, Context()).Select(x => x.Number).ToList() : (ids ?? []).Distinct().ToList();
        if (targets.Count == 0)
        {
            Message = "Select at least one ticket.";
            return Back();
        }

        if (operation == "export")
        {
            var chosen = targets.ToHashSet();
            var sorted = BuildQuery().Run(store.Tickets.Where(x => chosen.Contains(x.Number)), Context());
            var csv = TicketCsv.Build(sorted, store.Users, store.Technicians, store.Assets, store.Slas, store.TicketAttributeDefinitions, store.TicketAttributeValues);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv; charset=utf-8", TicketCsv.FileName(DateTime.Now));
        }

        Guid? technicianId = Guid.TryParse(newTechnician, out var parsedTechnician) ? parsedTechnician : null;
        HelpdeskStore.TicketBulkChange? change = operation switch
        {
            "status" => new("status", newStatus),
            "priority" => new("priority", newPriority),
            "category" => new("category", newCategory),
            "team" => new("team", string.IsNullOrWhiteSpace(newTeam) ? TicketListQuery.None : newTeam),
            "technician" => new("technician", TechnicianId: technicianId),
            "comment" => new("comment", Text: commentText, Internal: commentInternal),
            "close" => new("close", Text: closingMessage),
            "merge" => new("merge", TargetNumber: mergeTarget),
            _ => null
        };
        if (change is null)
        {
            Message = "Choose what to change.";
            return Back();
        }

        var result = store.BulkUpdateTickets(targets, change);
        Message = result.Error ?? Describe(operation!, result, mergeTarget);
        return Back();
    }

    private static string Describe(string operation, HelpdeskStore.TicketBulkResult result, int? mergeTarget)
    {
        static string Tickets(int count) => $"{count} ticket{(count == 1 ? "" : "s")}";
        if (result.Updated == 0 && result.Unchanged == 0 && result.Skipped == 0) return "None of the selected tickets could be found.";
        var text = new StringBuilder();
        if (result.Updated > 0)
            text.Append(operation switch
            {
                "merge" => $"{Tickets(result.Updated)} merged into #{mergeTarget}.",
                "comment" => $"Comment added to {Tickets(result.Updated)}.",
                "close" => $"{Tickets(result.Updated)} closed.",
                _ => $"{Tickets(result.Updated)} updated."
            });
        var unchangedText = operation switch
        {
            "merge" => $"#{mergeTarget} was left as it is.",
            "close" => $"{Tickets(result.Unchanged)} already closed.",
            _ => $"{Tickets(result.Unchanged)} already had that value."
        };
        if (result.Updated == 0 && result.Unchanged > 0)
            text.Append(operation switch
            {
                "merge" => $"Nothing to merge: only #{mergeTarget} was selected.",
                "close" => result.Unchanged == 1 ? "That ticket is already closed." : $"All {result.Unchanged} tickets are already closed.",
                _ => result.Unchanged == 1 ? "No change made: that ticket already had that value." : $"No changes made: all {result.Unchanged} tickets already had that value."
            });
        else if (result.Unchanged > 0) text.Append(' ').Append(unchangedText);
        if (result.Skipped > 0) text.Append($" {Tickets(result.Skipped)} skipped. {result.SkippedReason}");
        return text.ToString().Trim();
    }

    // Remembers, in this browser only, which technician is using it, for the My tickets queue.
    public IActionResult OnPostWhoAmI(string? technicianId)
    {
        Prepare();
        if (Guid.TryParse(technicianId, out var id) && store.Technicians.Any(x => x.Id == id))
            Response.Cookies.Append(MeCookie, id.ToString(), new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
        else
            Response.Cookies.Delete(MeCookie);
        return Back();
    }

    private TicketContext Context() => new(store.Users, store.Technicians, store.Assets, store.Priorities, store.Statuses, Now, store.TicketDueSoonHours, CurrentTechnicianId);

    private TicketListQuery BuildQuery() => new()
    {
        View = View, Search = Search, Status = Status, Priority = Priority, Category = Category, Technician = Technician, Team = Team,
        Requester = Guid.TryParse(Requester, out var requesterId) ? requesterId : null,
        Department = Department,
        Asset = Guid.TryParse(Asset, out var assetId) ? assetId : null,
        Sort = Sort!, Descending = Descending
    };

    // Ignores unknown views, sort columns and page sizes rather than failing, and works out who "me" is.
    private void Prepare()
    {
        View = TicketListQuery.Views.Contains(View?.Trim().ToLowerInvariant()) ? View!.Trim().ToLowerInvariant() : "open";
        var (defaultSort, defaultDescending) = TicketListQuery.DefaultSort(View);
        if (!TicketListQuery.SortColumns.Contains(Sort)) { Sort = defaultSort; Dir = defaultDescending ? "desc" : "asc"; }
        else Dir = string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        if (!PageSizes.Contains(Size)) Size = 50;
        if (PageNumber < 1) PageNumber = 1;
        Status = Clean(Status); Priority = Clean(Priority); Category = Clean(Category);
        CurrentTechnicianId = Guid.TryParse(Request.Cookies[MeCookie], out var me) && store.Technicians.Any(x => x.Id == me) ? me : null;
    }

    private static List<string> Clean(List<string>? values) => (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // The current view, filters, sort and page as hidden form fields, so a bulk action re-applies them and returns to the same place.
    public IEnumerable<(string Name, string Value)> StateFields()
    {
        yield return ("view", View);
        if (!string.IsNullOrWhiteSpace(Search)) yield return ("q", Search);
        foreach (var value in Status) yield return ("status", value);
        foreach (var value in Priority) yield return ("priority", value);
        foreach (var value in Category) yield return ("category", value);
        if (!string.IsNullOrWhiteSpace(Technician)) yield return ("tech", Technician);
        if (!string.IsNullOrWhiteSpace(Team)) yield return ("team", Team);
        if (!string.IsNullOrWhiteSpace(Requester)) yield return ("requester", Requester);
        if (!string.IsNullOrWhiteSpace(Department)) yield return ("dept", Department);
        if (!string.IsNullOrWhiteSpace(Asset)) yield return ("asset", Asset);
        yield return ("sort", Sort!);
        yield return ("dir", Dir!);
        yield return ("p", PageNumber.ToString());
        yield return ("size", Size.ToString());
    }

    private IActionResult Back() => Redirect(Url.Page("/Jobs", RouteFor(PageNumber, Sort!, Dir!, View)) ?? "/Jobs");

    // Builds the query string for a link, leaving out anything that is at its default so URLs stay short.
    private Dictionary<string, object?> RouteFor(int page, string sort, string dir, string view)
    {
        var (defaultSort, defaultDescending) = TicketListQuery.DefaultSort(view);
        var isDefaultSort = sort == defaultSort && (dir == "desc") == defaultDescending;
        var route = new Dictionary<string, object?>
        {
            ["view"] = view == "open" ? null : view,
            ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
            ["status"] = Status.Count == 0 ? null : Status.ToArray(),
            ["priority"] = Priority.Count == 0 ? null : Priority.ToArray(),
            ["category"] = Category.Count == 0 ? null : Category.ToArray(),
            ["tech"] = Blank(Technician), ["team"] = Blank(Team), ["requester"] = Blank(Requester), ["dept"] = Blank(Department), ["asset"] = Blank(Asset),
            ["sort"] = isDefaultSort ? null : sort,
            ["dir"] = isDefaultSort ? null : dir,
            ["p"] = page > 1 ? page : null,
            ["size"] = Size == 50 ? null : Size
        };
        return route;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // A new column starts newest-first for numbers and dates that mean "recent", and A to Z or soonest-first for the rest; the same column again flips it.
    public string? SortUrl(string column)
    {
        var direction = Sort == column ? (Descending ? "asc" : "desc") : (column is "number" or "created" or "updated" ? "desc" : "asc");
        return Url.Page("/Jobs", RouteFor(1, column, direction, View));
    }
    public string? PageUrl(int page) => Url.Page("/Jobs", RouteFor(page, Sort!, Dir!, View));
    // A queue tab keeps the search and filters but starts on the first page and that queue's own sort order.
    public string? ViewUrl(string view) => Url.Page("/Jobs", RouteFor(1, TicketListQuery.DefaultSort(view).Sort, TicketListQuery.DefaultSort(view).Descending ? "desc" : "asc", view));
    public string? ClearUrl() => Url.Page("/Jobs", new { view = View == "open" ? null : View });
    public string SortMark(string column) => Sort != column ? "" : Descending ? " ▼" : " ▲";

    public static string ViewLabel(string view) => view switch
    {
        "mine" => "My tickets",
        "unassigned" => "Unassigned",
        "overdue" => "Overdue / due soon",
        "all" => "All tickets",
        _ => "Open"
    };
}
