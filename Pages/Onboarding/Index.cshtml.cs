using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Onboarding;

// The Onboarding menu: every new starter's onboarding, in tabs like the ticket queues, with the same filter panel,
// sort, page size and pager as the other lists.
// - active: still to finish;
// - upcoming: still to finish and not started yet;
// - overdue: still to finish, with a task past its due date;
// - it: still to finish, with IT tasks left - what the technicians' Onboarding queue shows;
// - finished: every task done, or cancelled;
// - all: everything.
public class IndexModel(HelpdeskStore store) : PageModel
{
    public const string None = "none";
    public static readonly int[] PageSizes = [25, 50, 100];
    public static readonly string[] Views = ["active", "upcoming", "overdue", "it", "finished", "all"];
    public static readonly string[] Startings = ["week", "fortnight", "month", "started"];
    public static readonly string[] Sorts = ["start", "latest", "overdue", "newest"];

    public sealed record Row(OnboardingRecord Record, TicketRecord? Ticket, UserRecord? Starter)
    {
        public bool IsFinished => Record.IsCancelled || Record.AllDone;
    }

    // "page" is reserved by routing, so paging uses p, as on the other lists.
    [BindProperty(SupportsGet = true, Name = "view")] public string View { get; set; } = "active";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "template")] public string? Template { get; set; }
    [BindProperty(SupportsGet = true, Name = "dept")] public string? Department { get; set; }
    [BindProperty(SupportsGet = true, Name = "tech")] public string? Technician { get; set; }
    [BindProperty(SupportsGet = true, Name = "starting")] public string? Starting { get; set; }
    [BindProperty(SupportsGet = true, Name = "sort")] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int Size { get; set; } = 25;

    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public IReadOnlyDictionary<string, int> ViewCounts { get; private set; } = new Dictionary<string, int>();
    public IReadOnlyList<string> Templates { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<TechnicianRecord> Technicians { get; private set; } = [];
    public int TotalOnboardings { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public bool CanStart => store.UserCan(User, Modules.Onboarding, ModulePermission.New);
    [TempData] public string? Message { get; set; }

    // Finished onboardings read back most recent first; everything else is worked soonest first.
    public string DefaultSort => View == "finished" ? "latest" : "start";
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Template) || !string.IsNullOrWhiteSpace(Department)
        || !string.IsNullOrWhiteSpace(Technician) || !string.IsNullOrWhiteSpace(Starting);

    public void OnGet()
    {
        if (!Views.Contains(View)) View = "active";
        if (!Sorts.Contains(Sort ?? "")) Sort = DefaultSort;
        if (!Startings.Contains(Starting ?? "")) Starting = null;
        if (!PageSizes.Contains(Size)) Size = 25;

        var tickets = store.Tickets.ToDictionary(x => x.Number);
        var users = store.Users.ToDictionary(x => x.Id);
        var all = store.Onboardings.Select(x => new Row(x, tickets.GetValueOrDefault(x.TicketNumber), users.GetValueOrDefault(x.StarterId))).ToList();
        TotalOnboardings = all.Count;
        ViewCounts = Views.ToDictionary(view => view, view => all.Count(row => InView(row, view)));

        // The choices come from the onboardings themselves, so a renamed template or a department since removed from
        // the option list can still be picked.
        Templates = all.Select(x => x.Record.TemplateName).Concat(store.OnboardingTemplates.Select(x => x.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Departments = all.Select(x => x.Starter?.Department ?? "").Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var involved = all.SelectMany(x => x.Record.Tasks.Select(t => t.TechnicianId).Append(x.Ticket?.TechnicianId)).OfType<Guid>().ToHashSet();
        Technicians = store.Technicians.Where(x => x.IsActive || involved.Contains(x.Id)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

        IEnumerable<Row> query = all.Where(row => InView(row, View) && Matches(row));
        if (Template == None) query = query.Where(x => string.IsNullOrWhiteSpace(x.Record.TemplateName));
        else if (!string.IsNullOrWhiteSpace(Template)) query = query.Where(x => string.Equals(x.Record.TemplateName, Template, StringComparison.OrdinalIgnoreCase));
        if (Department == None) query = query.Where(x => string.IsNullOrWhiteSpace(x.Starter?.Department));
        else if (!string.IsNullOrWhiteSpace(Department)) query = query.Where(x => string.Equals(x.Starter?.Department, Department, StringComparison.OrdinalIgnoreCase));
        // A technician is on an onboarding when they lead its ticket or an IT task is given to them.
        if (Technician == None) query = query.Where(x => x.Ticket?.TechnicianId is null);
        else if (Guid.TryParse(Technician, out var techId)) query = query.Where(x => x.Ticket?.TechnicianId == techId || x.Record.Tasks.Any(t => t.TechnicianId == techId));
        query = Starting switch
        {
            "week" => query.Where(x => x.Record.StartDate >= Today && x.Record.StartDate <= Today.AddDays(7)),
            "fortnight" => query.Where(x => x.Record.StartDate >= Today && x.Record.StartDate <= Today.AddDays(14)),
            "month" => query.Where(x => x.Record.StartDate >= Today && x.Record.StartDate <= Today.AddDays(30)),
            "started" => query.Where(x => x.Record.StartDate <= Today),
            _ => query
        };

        IOrderedEnumerable<Row> ordered = Sort switch
        {
            "latest" => query.OrderByDescending(x => x.Record.StartDate),
            "overdue" => query.OrderByDescending(x => x.Record.OverdueCount(Today)).ThenBy(x => x.Record.StartDate),
            "newest" => query.OrderByDescending(x => x.Record.CreatedAt),
            _ => query.OrderBy(x => x.Record.StartDate)
        };
        var matches = ordered.ThenBy(x => x.Record.TicketNumber).ToList();
        MatchCount = matches.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)Size));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Rows = matches.Skip((PageNumber - 1) * Size).Take(Size).ToList();
    }

    private bool InView(Row row, string view) => view switch
    {
        "finished" => row.IsFinished,
        "upcoming" => !row.IsFinished && row.Record.StartDate > Today,
        "overdue" => !row.IsFinished && row.Record.OverdueCount(Today) > 0,
        "it" => !row.IsFinished && row.Record.ItTasks.Any(x => !x.IsDone),
        "all" => true,
        _ => !row.IsFinished
    };

    // Every word somewhere in the name, email, job title, department, template or ticket number.
    private bool Matches(Row row)
    {
        var words = (Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = string.Join(" ", row.Starter?.Name, row.Starter?.Email, row.Record.JobTitle, row.Starter?.Department, row.Record.TemplateName, $"#{row.Record.TicketNumber}");
        return words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static string ViewLabel(string view) => view switch
    {
        "upcoming" => "Not started yet",
        "overdue" => "Overdue tasks",
        "it" => "IT tasks left",
        "finished" => "Finished",
        "all" => "All",
        _ => "In progress"
    };

    public static string StartingLabel(string starting) => starting switch
    {
        "week" => "In the next 7 days",
        "fortnight" => "In the next 14 days",
        "month" => "In the next 30 days",
        _ => "Already started"
    };

    public static string SortLabel(string sort) => sort switch
    {
        "latest" => "Start date, latest first",
        "overdue" => "Most overdue tasks",
        "newest" => "Newest first",
        _ => "Start date, soonest first"
    };

    // Switching tab keeps the filters, as on the ticket list, but goes back to page one and the tab's own order.
    public string? ViewUrl(string view) => ListUrl(view, null, 1);
    public string ClearUrl() => Url.Page("/Onboarding/Index", new { view = View == "active" ? null : View })!;
    public string PageUrl(int page) => ListUrl(View, Sort, page)!;

    private string? ListUrl(string view, string? sort, int page) => Url.Page("/Onboarding/Index", new Dictionary<string, object?>
    {
        ["view"] = view == "active" ? null : view,
        ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["template"] = string.IsNullOrWhiteSpace(Template) ? null : Template,
        ["dept"] = string.IsNullOrWhiteSpace(Department) ? null : Department,
        ["tech"] = string.IsNullOrWhiteSpace(Technician) ? null : Technician,
        ["starting"] = Starting,
        ["sort"] = sort is null || sort == DefaultSort ? null : sort,
        ["size"] = Size == 25 ? null : Size,
        ["p"] = page > 1 ? page : null
    });

    public static string Starts(DateOnly start, DateOnly today) => (start.DayNumber - today.DayNumber) switch
    {
        0 => "Starts today",
        1 => "Starts tomorrow",
        > 1 and var days => $"Starts in {days} days",
        -1 => "Started yesterday",
        var days => $"Started {-days} days ago"
    };
}
