using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The daily sheet a supervisor prints and hands out (stage 3 of desks): the open jobs for some desks, or one team, by
// room, with a box to tick and room to write. Only tickets this person may see (HelpdeskStore.Desks).
// With a day (planner stage 5) it is that day's plan instead: the jobs planned for it - and on today's, the ones whose
// planned day went by without them being done - for one person, or each person on a page of their own.
public class DailySheetModel(HelpdeskStore store) : PageModel
{
    // Desk names; none means the desks the account is on (or every desk it can see).
    [BindProperty(SupportsGet = true, Name = "desk")] public List<string> Desk { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "team")] public string? Team { get; set; }
    // The planned day (yyyy-MM-dd), "today", or nothing for every open job.
    [BindProperty(SupportsGet = true, Name = "day")] public string? DayText { get; set; }
    // A technician's id, or "none" for jobs nobody has yet.
    [BindProperty(SupportsGet = true, Name = "tech")] public string? Technician { get; set; }
    // "person": a page for each technician.
    [BindProperty(SupportsGet = true, Name = "split")] public string? Split { get; set; }

    public const string Nobody = "none";
    public sealed record Section(string? Person, IReadOnlyList<(string Room, IReadOnlyList<TicketRecord> Tickets)> Rooms, int Count);

    public IReadOnlyList<Desk> ShownDesks { get; private set; } = [];
    public IReadOnlyList<Section> Sections { get; private set; } = [];
    public int Count { get; private set; }
    public IReadOnlyDictionary<Guid, string> Names { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyList<TechnicianRecord> People { get; private set; } = [];
    public DateTime Now { get; } = DateTime.UtcNow;
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public DateOnly? Day { get; private set; }
    public bool ByPerson => Split == "person";
    private Func<string?, Desk> _deskOf = _ => null!;
    public string Ref(TicketRecord ticket) => HelpdeskStore.RefFor(_deskOf(ticket.Category), ticket);
    public string? PersonName => Technician == Nobody ? "Unassigned" : Guid.TryParse(Technician, out var id) ? Names.GetValueOrDefault(id) : null;
    public string Title => Day is null ? "Daily job sheet" : "Planned jobs";
    public string Subtitle => string.Join(" · ", new[]
    {
        ShownDesks.Count == 0 ? null : string.Join(", ", ShownDesks.Select(x => x.Name)) + (ShownDesks.Count == 1 ? " desk" : " desks"),
        string.IsNullOrWhiteSpace(Team) ? null : $"{Team} team",
        PersonName,
        (Day ?? Today).ToString("dddd d MMMM yyyy")
    }.Where(x => x is not null));

    public void OnGet()
    {
        _deskOf = store.DeskLookup();
        var visible = store.DesksVisibleTo(User);
        var asked = visible.Where(d => Desk.Any(x => string.Equals(x, d.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        // Nothing asked for: the desk switcher's choice (HelpdeskStore.DeskSwitch), named in the subtitle even when it is every desk.
        var working = store.WorkingDesks(User);
        ShownDesks = asked.Count > 0 ? asked : working.Count > 0 ? working.ToList() : store.HasSeveralDesks ? visible : [];
        var deskIds = ShownDesks.Select(x => x.Id).ToHashSet();
        Names = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
        Day = string.Equals(DayText?.Trim(), "today", StringComparison.OrdinalIgnoreCase) ? Today
            : DateOnly.TryParseExact(DayText?.Trim(), "yyyy-MM-dd", out var day) ? day : null;
        Split = string.Equals(Split?.Trim(), "person", StringComparison.OrdinalIgnoreCase) ? "person" : null;
        if (Technician is not null && Technician != Nobody && !(Guid.TryParse(Technician, out var techId) && Names.ContainsKey(techId))) Technician = null;

        var tickets = store.TicketsFor(User)
            .Where(t => !TicketInsights.IsClosed(t))
            .Where(t => deskIds.Count == 0 || deskIds.Contains(_deskOf(t.Category).Id))
            .Where(t => string.IsNullOrWhiteSpace(Team) || string.Equals(t.TeamName, Team.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(t => Day is not { } on || TicketInsights.OnSheetFor(t, on, Today))
            .Where(t => Technician is null || (Technician == Nobody ? t.TechnicianId is null : t.TechnicianId?.ToString() == Technician))
            .ToList();
        Count = tickets.Count;
        People = store.Technicians.Where(x => x.IsActive && (deskIds.Count == 0 || x.DeskIds.Any(deskIds.Contains))).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (!ByPerson) { Sections = [new(null, ByRoom(tickets), tickets.Count)]; return; }
        // A page for each person with something on it, by name, and the jobs nobody has yet at the end.
        Sections = tickets.GroupBy(t => t.TechnicianId)
            .OrderBy(g => g.Key is null).ThenBy(g => g.Key is { } id ? Names.GetValueOrDefault(id, "") : "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new Section(g.Key is { } id ? Names.GetValueOrDefault(id, "Unknown") : "Unassigned", ByRoom(g.ToList()), g.Count()))
            .ToList();
    }

    // Rooms in name order, as someone walks the site; jobs with no room at the end. Within a room, the most urgent and
    // soonest due first.
    private static IReadOnlyList<(string Room, IReadOnlyList<TicketRecord> Tickets)> ByRoom(List<TicketRecord> tickets) => tickets
        .GroupBy(t => string.IsNullOrWhiteSpace(t.Location) ? "" : t.Location.Trim(), StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key.Length == 0).ThenBy(g => g.Key, NaturalComparer.Instance)
        .Select(g => (g.Key.Length == 0 ? "No room given" : g.Key, (IReadOnlyList<TicketRecord>)g.OrderBy(PriorityRank).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.Number).ToList()))
        .ToList();

    private static int PriorityRank(TicketRecord ticket) => ticket.Priority.ToLowerInvariant() switch { "urgent" => 0, "high" => 1, "normal" => 2, "low" => 3, _ => 4 };
}
