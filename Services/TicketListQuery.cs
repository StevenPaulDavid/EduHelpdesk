using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Paused: the SLA clock is stopped (a status such as On Hold), so it is neither overdue nor due soon until it restarts.
public enum DueState { None, Soon, Overdue, Paused }

// What the ticket list needs to know besides the tickets themselves.
public sealed record TicketContext(
    IReadOnlyList<UserRecord> Users,
    IReadOnlyList<TechnicianRecord> Technicians,
    IReadOnlyList<AssetRecord> Assets,
    IReadOnlyList<string> Priorities,
    IReadOnlyList<string> Statuses,
    DateTime Now,
    int DueSoonHours,
    Guid? CurrentTechnicianId);

public static class TicketInsights
{
    public const string ClosedStatus = "Closed";

    public static bool IsClosed(TicketRecord ticket) => string.Equals(ticket.Status, ClosedStatus, StringComparison.OrdinalIgnoreCase);

    // Closed tickets and tickets with no due date are never flagged.
    public static DueState DueStateOf(TicketRecord ticket, DateTime now, int soonHours)
    {
        if (IsClosed(ticket) || ticket.DueDate is not { } due) return DueState.None;
        if (ticket.IsSlaPaused) return DueState.Paused;
        if (due < now) return DueState.Overdue;
        return due <= now.AddHours(soonHours) ? DueState.Soon : DueState.None;
    }

    // Open, past its due date, and not paused - the one test every overdue count uses.
    public static bool IsOverdue(TicketRecord ticket, DateTime now) =>
        !IsClosed(ticket) && !ticket.IsSlaPaused && ticket.DueDate is { } due && due < now;

    public static string DueText(TicketRecord ticket, DateTime now)
    {
        if (ticket.DueDate is not { } due) return string.Empty;
        if (!IsClosed(ticket) && ticket.IsSlaPaused) return "SLA paused";
        var gap = due - now;
        var span = gap < TimeSpan.Zero ? -gap : gap;
        var length = span.TotalDays >= 2 ? $"{(int)span.TotalDays} days"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} hour{((int)span.TotalHours == 1 ? "" : "s")}"
            : $"{Math.Max(1, (int)span.TotalMinutes)} min";
        return gap < TimeSpan.Zero ? $"Overdue by {length}" : $"Due in {length}";
    }
}

// The search, filters, queue and sort order of the ticket list. The list page, the bulk actions and the CSV export all use it,
// so "the tickets matching the current filters" means the same thing everywhere.
public sealed class TicketListQuery
{
    // Filter values that mean "blank" or "nobody".
    public const string None = "(none)";
    // open: not closed, mine: open and assigned to the current technician, unassigned: open with no technician,
    // overdue: open and past due or due soon, all: everything including closed.
    public static readonly string[] Views = ["open", "mine", "unassigned", "overdue", "all"];
    public static readonly string[] SortColumns = ["number", "title", "requester", "asset", "technician", "priority", "status", "due", "created", "updated"];

    public string View { get; set; } = "open";
    public string? Search { get; set; }
    public List<string> Status { get; set; } = [];
    public List<string> Priority { get; set; } = [];
    public List<string> Category { get; set; } = [];
    // Incident or Request.
    public List<string> Type { get; set; } = [];
    // "none" for unassigned, "me" for the current technician, or a technician id.
    public string? Technician { get; set; }
    // A team name, or "(none)" for tickets with no team.
    public string? Team { get; set; }
    public Guid? Requester { get; set; }
    public string? Department { get; set; }
    // "(none)" for tickets with no location.
    public string? Location { get; set; }
    public Guid? Asset { get; set; }
    public string Sort { get; set; } = "number";
    public bool Descending { get; set; } = true;

    // Newest first, except that the overdue queue starts with what is most overdue.
    public static (string Sort, bool Descending) DefaultSort(string view) => view == "overdue" ? ("due", false) : ("number", true);

    public static bool InView(TicketRecord ticket, string view, TicketContext context) => view switch
    {
        "all" => true,
        "mine" => !TicketInsights.IsClosed(ticket) && context.CurrentTechnicianId is { } me && ticket.TechnicianId == me,
        "unassigned" => !TicketInsights.IsClosed(ticket) && ticket.TechnicianId is null,
        "overdue" => TicketInsights.DueStateOf(ticket, context.Now, context.DueSoonHours) is DueState.Overdue or DueState.Soon,
        _ => !TicketInsights.IsClosed(ticket)
    };

    // How many tickets each queue holds, ignoring any filters, for the tabs above the list.
    public static Dictionary<string, int> ViewCounts(IReadOnlyCollection<TicketRecord> tickets, TicketContext context) =>
        Views.ToDictionary(view => view, view => tickets.Count(t => InView(t, view, context)));

    public List<TicketRecord> Run(IEnumerable<TicketRecord> tickets, TicketContext context)
    {
        var users = context.Users.ToDictionary(x => x.Id);
        var technicians = context.Technicians.ToDictionary(x => x.Id);
        var assets = context.Assets.ToDictionary(x => x.Id);
        IEnumerable<TicketRecord> query = tickets.Where(t => InView(t, View, context));

        if (Status.Count > 0) query = query.Where(t => Status.Any(s => Same(t.Status, s)));
        if (Priority.Count > 0) query = query.Where(t => Priority.Any(s => Same(t.Priority, s)));
        if (Category.Count > 0) query = query.Where(t => Category.Any(s => Same(t.Category, s)));
        if (Type.Count > 0) query = query.Where(t => Type.Any(s => Same(t.Type, s)));
        if (!string.IsNullOrWhiteSpace(Technician))
        {
            if (string.Equals(Technician, "none", StringComparison.OrdinalIgnoreCase)) query = query.Where(t => t.TechnicianId is null);
            else if (string.Equals(Technician, "me", StringComparison.OrdinalIgnoreCase)) query = query.Where(t => context.CurrentTechnicianId is { } me && t.TechnicianId == me);
            else if (Guid.TryParse(Technician, out var technicianId)) query = query.Where(t => t.TechnicianId == technicianId);
        }
        if (!string.IsNullOrWhiteSpace(Team))
            query = query.Where(t => Team == None ? string.IsNullOrWhiteSpace(t.TeamName) : Same(t.TeamName, Team));
        if (Requester is { } requesterId) query = query.Where(t => t.RequesterId == requesterId);
        if (!string.IsNullOrWhiteSpace(Department))
            query = query.Where(t => users.TryGetValue(t.RequesterId, out var user)
                && (Department == None ? string.IsNullOrWhiteSpace(user.Department) : Same(user.Department, Department)));
        if (!string.IsNullOrWhiteSpace(Location))
            query = query.Where(t => Location == None ? string.IsNullOrWhiteSpace(t.Location) : Same(t.Location, Location));
        if (Asset is { } assetId) query = query.Where(t => t.AssetIds.Contains(assetId));
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.TrimStart('#')).Where(x => x.Length > 0).ToList();
            if (terms.Count > 0)
                query = query.Where(t =>
                {
                    var text = SearchText(t, users, technicians, assets);
                    return terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
                });
        }
        return Order(query.ToList(), users, technicians, assets, context);
    }

    private static bool Same(string? value, string filter) => string.Equals(value?.Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string SearchText(TicketRecord ticket, Dictionary<Guid, UserRecord> users, Dictionary<Guid, TechnicianRecord> technicians, Dictionary<Guid, AssetRecord> assets)
    {
        var parts = new List<string> { ticket.Number.ToString(), ticket.Title, ticket.Description, ticket.Category, ticket.Status, ticket.Priority, ticket.Type, ticket.TeamName ?? "", ticket.Location ?? "" };
        if (users.TryGetValue(ticket.RequesterId, out var user)) { parts.Add(user.Name); parts.Add(user.Department); parts.Add(user.Email); }
        if (ticket.TechnicianId is { } techId && technicians.TryGetValue(techId, out var technician)) parts.Add(technician.Name);
        foreach (var id in ticket.AssetIds)
            if (assets.TryGetValue(id, out var asset)) { parts.Add(asset.AssetTag); parts.Add(asset.Make); parts.Add(asset.Model); parts.Add(asset.SerialNumber); }
        parts.AddRange(ticket.Comments.Select(c => c.Text));
        return string.Join('\n', parts);
    }

    private List<TicketRecord> Order(List<TicketRecord> list, Dictionary<Guid, UserRecord> users, Dictionary<Guid, TechnicianRecord> technicians, Dictionary<Guid, AssetRecord> assets, TicketContext context)
    {
        var natural = NaturalComparer.Instance;
        string Requester(TicketRecord t) => users.TryGetValue(t.RequesterId, out var u) ? u.Name : string.Empty;
        string Technician(TicketRecord t) => t.TechnicianId is { } id && technicians.TryGetValue(id, out var x) ? x.Name : string.Empty;
        string FirstAsset(TicketRecord t) => t.AssetIds.Select(id => assets.TryGetValue(id, out var a) ? a.AssetTag : null).FirstOrDefault(x => x is not null) ?? string.Empty;
        // Most urgent first when ascending: Urgent, High, Normal, Low, then anything the user has added, in list order.
        var known = new[] { "Urgent", "High", "Normal", "Low" };
        int PriorityRank(TicketRecord t)
        {
            var index = Array.FindIndex(known, x => string.Equals(x, t.Priority, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
            var listed = context.Priorities.ToList().FindIndex(x => string.Equals(x, t.Priority, StringComparison.OrdinalIgnoreCase));
            return known.Length + Math.Max(listed, 0);
        }
        int StatusRank(TicketRecord t) => Math.Max(0, context.Statuses.ToList().FindIndex(x => string.Equals(x, t.Status, StringComparison.OrdinalIgnoreCase)));

        IOrderedEnumerable<TicketRecord> By(Func<TicketRecord, string?> key) => Descending ? list.OrderByDescending(key, natural) : list.OrderBy(key, natural);
        IOrderedEnumerable<TicketRecord> ByValue<T>(Func<TicketRecord, T> key) => Descending ? list.OrderByDescending(key) : list.OrderBy(key);
        // Tickets with no value for the column always go last, whichever way it is sorted.
        IOrderedEnumerable<TicketRecord> ByDate(Func<TicketRecord, DateTime?> key) => Descending
            ? list.OrderBy(t => key(t) is null).ThenByDescending(key)
            : list.OrderBy(t => key(t) is null).ThenBy(key);
        IOrderedEnumerable<TicketRecord> ByName(Func<TicketRecord, string> key) => Descending
            ? list.OrderBy(t => key(t).Length == 0).ThenByDescending(key, natural)
            : list.OrderBy(t => key(t).Length == 0).ThenBy(key, natural);

        var ordered = Sort switch
        {
            "title" => By(t => t.Title),
            "requester" => ByName(Requester),
            "asset" => ByName(FirstAsset),
            "technician" => ByName(Technician),
            "priority" => ByValue(PriorityRank),
            "status" => ByValue(StatusRank),
            "due" => ByDate(t => t.DueDate),
            "created" => ByValue(t => t.CreatedAt),
            "updated" => ByValue(t => t.LastModifiedAt),
            _ => ByValue(t => t.Number)
        };
        return ordered.ThenByDescending(t => t.Number).ToList();
    }
}
