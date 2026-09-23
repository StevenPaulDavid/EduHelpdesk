using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;

public class AuditModel(HelpdeskStore store) : PageModel
{
    public const int PageSize = 50;
    public static readonly IReadOnlyList<string> Areas = ["Tickets", "Assets", "Users", "Technicians", "Suppliers", "Parts", "Lists", "SLAs", "Ticket templates", "Custom attributes", "Settings", "System"];

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
            .ToHashSet();
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
            _ => null
        };
    }

    private static bool Has(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static DateTime LocalMidnightToUtc(DateTime date) => DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();
}
