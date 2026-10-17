using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The daily sheet a supervisor prints and hands out (stage 3 of desks): the open jobs for some desks, or one team, by
// room, with a box to tick and room to write. Only tickets this person may see (HelpdeskStore.Desks).
public class DailySheetModel(HelpdeskStore store) : PageModel
{
    // Desk names; none means the desks the account is on (or every desk it can see).
    [BindProperty(SupportsGet = true, Name = "desk")] public List<string> Desk { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "team")] public string? Team { get; set; }

    public IReadOnlyList<Desk> ShownDesks { get; private set; } = [];
    public IReadOnlyList<(string Room, IReadOnlyList<TicketRecord> Tickets)> Rooms { get; private set; } = [];
    public int Count { get; private set; }
    public IReadOnlyDictionary<Guid, string> Names { get; private set; } = new Dictionary<Guid, string>();
    public DateTime Now { get; } = DateTime.UtcNow;
    private Func<string?, Desk> _deskOf = _ => null!;
    public string Ref(TicketRecord ticket) => HelpdeskStore.RefFor(_deskOf(ticket.Category), ticket);
    public string Subtitle => string.Join(" · ", new[]
    {
        ShownDesks.Count == 0 ? null : string.Join(", ", ShownDesks.Select(x => x.Name)) + (ShownDesks.Count == 1 ? " desk" : " desks"),
        string.IsNullOrWhiteSpace(Team) ? null : $"{Team} team",
        DateTime.Now.ToString("dddd d MMMM yyyy")
    }.Where(x => x is not null));

    public void OnGet()
    {
        _deskOf = store.DeskLookup();
        var visible = store.DesksVisibleTo(User);
        var asked = visible.Where(d => Desk.Any(x => string.Equals(x, d.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var mine = store.DesksOf(User).Where(d => visible.Any(v => v.Id == d.Id)).ToList();
        ShownDesks = asked.Count > 0 ? asked : mine.Count > 0 && mine.Count < visible.Count ? mine : store.HasSeveralDesks ? visible : [];
        var deskIds = ShownDesks.Select(x => x.Id).ToHashSet();
        var tickets = store.TicketsFor(User)
            .Where(t => !TicketInsights.IsClosed(t))
            .Where(t => deskIds.Count == 0 || deskIds.Contains(_deskOf(t.Category).Id))
            .Where(t => string.IsNullOrWhiteSpace(Team) || string.Equals(t.TeamName, Team.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Count = tickets.Count;
        // Rooms in name order, as someone walks the site; jobs with no room at the end. Within a room, the most urgent
        // and soonest due first.
        Rooms = tickets
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Location) ? "" : t.Location.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Length == 0).ThenBy(g => g.Key, NaturalComparer.Instance)
            .Select(g => (g.Key.Length == 0 ? "No room given" : g.Key, (IReadOnlyList<TicketRecord>)g.OrderBy(PriorityRank).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.Number).ToList()))
            .ToList();
        Names = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
    }

    private static int PriorityRank(TicketRecord ticket) => ticket.Priority.ToLowerInvariant() switch { "urgent" => 0, "high" => 1, "normal" => 2, "low" => 3, _ => 4 };
}
