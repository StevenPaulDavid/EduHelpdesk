using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;

public class AuditModel(HelpdeskStore store) : PageModel
{
    // Virtual so the print view can take the whole filtered log rather than the page on screen; the CSV export flips
    // the same switch locally (see OnGetExport).
    public virtual int PageSize => _exportingEverything ? int.MaxValue : 50;
    private bool _exportingEverything;
    public static readonly IReadOnlyList<string> Areas = ["Tickets", "Projects", "Onboarding", "Assets", "Users", "Technicians", "Suppliers", "Parts", "Lists", "SLAs", "Ticket templates", "Service catalogue", "Custom attributes", "Settings", "Sign-in", "Database", "System"];

    // "area" and "action" are reserved routing names, so the query string uses section and act.
    [BindProperty(SupportsGet = true, Name = "section")] public string? Section { get; set; }
    [BindProperty(SupportsGet = true, Name = "act")] public string? ActionFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "who")] public string? ActorFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<AuditEntry> Entries { get; private set; } = [];
    public IReadOnlyList<string> Actions { get; private set; } = [];
    // Everyone who appears in the log, including people whose account has since been deleted - the log keeps the name.
    public IReadOnlyList<string> Actors { get; private set; } = [];
    public int TotalEvents { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Section) || !string.IsNullOrWhiteSpace(ActionFilter) || !string.IsNullOrWhiteSpace(ActorFilter) || !string.IsNullOrWhiteSpace(Search) || From.HasValue || To.HasValue;

    private HashSet<string> _existing = [];

    public void OnGet()
    {
        var all = store.GetAuditEntries();
        TotalEvents = all.Count;
        Actions = all.Select(x => x.Action).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        Actors = all.Where(x => x.By is not null).Select(x => x.By!.Value.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        IEnumerable<AuditEntry> query = all;
        if (!string.IsNullOrWhiteSpace(Section))
            query = query.Where(x => string.Equals(x.Area, Section, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(ActionFilter))
            query = query.Where(x => string.Equals(x.Action, ActionFilter, StringComparison.OrdinalIgnoreCase));
        // Filters on the recorded name rather than the account id, so it still finds the work of someone since deleted.
        if (!string.IsNullOrWhiteSpace(ActorFilter))
            query = query.Where(x => x.By is { } by && string.Equals(by.Name, ActorFilter, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(x => terms.All(t => Has(x.Entity, t) || Has(x.Action, t) || Has(x.Details, t) || Has(x.Area, t) || Has(x.By?.Name ?? "", t)));
        }
        if (From is { } from)
        {
            var start = LocalMidnightToUtc(from);
            query = query.Where(x => x.At >= start);
        }
        if (To is { } to)
        {
            var end = LocalMidnightToUtc(to.AddDays(1));
            query = query.Where(x => x.At < end);
        }

        var matches = query.ToList();
        MatchCount = matches.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Entries = matches.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();

        _existing = store.Tickets.Select(x => $"Ticket|{x.Number}")
            .Concat(store.Assets.Select(x => $"Asset|{x.Id}"))
            .Concat(store.Users.Select(x => $"User|{x.Id}"))
            .Concat(store.Technicians.Select(x => $"Technician|{x.Id}"))
            .Concat(store.Suppliers.Select(x => $"Supplier|{x.Id}"))
            .Concat(store.Parts.Select(x => $"Part|{x.Id}"))
            .Concat(store.Projects.Select(x => $"Project|{x.Number}"))
            .ToHashSet();
    }

    // Exports every row matching the current filters, not the page on screen - the same principle as "select all
    // matching" on the list pages. PageSize is lifted for the duration so the paging step keeps the whole set.
    public IActionResult OnGetExport()
    {
        _exportingEverything = true;
        OnGet();
        var csv = Csv.Table(
            ["When (local)", "Who", "Area", "Item", "Action", "Details"],
            Entries,
            x => [x.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), x.By?.Name ?? "", x.Area, x.Entity, x.Action, x.Details]);
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName("audit-log", DateTime.Now));
    }

    // Only records that still exist can be linked to.
    public string? LinkFor(AuditEntry entry)
    {
        if (entry.EntityType is null || entry.EntityKey is null || !_existing.Contains($"{entry.EntityType}|{entry.EntityKey}")) return null;
        return entry.EntityType switch
        {
            "Ticket" => Url.Page("/Job", new { number = entry.EntityKey }),
            "Asset" => Url.Page("/Asset", new { id = entry.EntityKey }),
            "User" => Url.Page("/User", new { id = entry.EntityKey }),
            "Technician" => Url.Page("/People/Technician", new { id = entry.EntityKey }),
            "Supplier" => Url.Page("/Supplier", new { id = entry.EntityKey }),
            "Part" => Url.Page("/Parts/Edit", new { id = entry.EntityKey }),
            "Project" => Url.Page("/Project", new { number = entry.EntityKey }),
            _ => null
        };
    }

    // Filters in the Filters panel that are switched on - the number on its button.
    public int PanelFilterCount => new[] { Section, ActionFilter, ActorFilter }.Count(x => !string.IsNullOrWhiteSpace(x)) + (From.HasValue ? 1 : 0) + (To.HasValue ? 1 : 0);

    private Dictionary<string, object?> Route() => new()
    {
        ["section"] = Blank(Section), ["act"] = Blank(ActionFilter), ["who"] = Blank(ActorFilter), ["q"] = Blank(Search),
        ["from"] = From?.ToString("yyyy-MM-dd"), ["to"] = To?.ToString("yyyy-MM-dd")
    };
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // Every filter in force as a chip that takes just that one off.
    public IReadOnlyList<Pages.ActiveFilter> ActiveFilters()
    {
        var chips = new List<Pages.ActiveFilter>();
        string? Without(string key) { var route = Route(); route[key] = null; return Url.Page("/Settings/Audit", route); }
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q")));
        if (!string.IsNullOrWhiteSpace(Section)) chips.Add(new($"Area: {Section}", Without("section")));
        if (!string.IsNullOrWhiteSpace(ActionFilter)) chips.Add(new($"Action: {ActionFilter}", Without("act")));
        if (!string.IsNullOrWhiteSpace(ActorFilter)) chips.Add(new($"Who: {ActorFilter}", Without("who")));
        if (From is { } from) chips.Add(new($"From {from:d MMM yyyy}", Without("from")));
        if (To is { } to) chips.Add(new($"To {to:d MMM yyyy}", Without("to")));
        return chips;
    }

    private static bool Has(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static DateTime LocalMidnightToUtc(DateTime date) => DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();
}
