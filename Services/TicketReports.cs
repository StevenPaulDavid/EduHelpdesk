using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// How well tickets were dealt with, in numbers. Everything here is a plain calculation over the tickets it is given.
public static class TicketReports
{
    // When a ticket was closed. A closed ticket with no recorded closing time counts as closed when it was last touched.
    public static DateTime? ClosedTime(TicketRecord ticket) =>
        ticket.ClosedAt ?? (TicketInsights.IsClosed(ticket) ? ticket.LastModifiedAt : null);

    public sealed record SlaRow(string Name, int Tickets, int OnTime, int Late, int OpenOverdue, int OpenWithinTime, int NoDueDate, int Closed, double? AverageResolutionHours, double? MedianResolutionHours)
    {
        // Tickets whose deadline has been decided: met, missed, or already past due and still open.
        public int Judged => OnTime + Late + OpenOverdue;
        public int Breached => Late + OpenOverdue;
        // Share of judged tickets that met their deadline. Null when nothing has been judged yet.
        public double? OnTimeRate => Judged == 0 ? null : OnTime * 100.0 / Judged;
    }

    // A ticket meets its deadline when it was closed on or before its due date. An open ticket past its due date has missed it already.
    public static SlaRow Sla(string name, IReadOnlyCollection<TicketRecord> tickets, DateTime now)
    {
        int onTime = 0, late = 0, openOverdue = 0, openWithin = 0, noDue = 0;
        var resolution = new List<double>();
        foreach (var ticket in tickets)
        {
            var closed = ClosedTime(ticket);
            if (closed is { } closedAt) resolution.Add(Math.Max(0, (closedAt - ticket.CreatedAt).TotalHours));
            if (ticket.DueDate is not { } due) { noDue++; continue; }
            if (closed is { } closedTime) { if (closedTime <= due) onTime++; else late++; }
            else if (TicketInsights.IsOverdue(ticket, now)) openOverdue++;
            else openWithin++;
        }
        resolution.Sort();
        double? median = resolution.Count == 0 ? null : resolution.Count % 2 == 1 ? resolution[resolution.Count / 2] : (resolution[resolution.Count / 2 - 1] + resolution[resolution.Count / 2]) / 2;
        return new SlaRow(name, tickets.Count, onTime, late, openOverdue, openWithin, noDue, resolution.Count, resolution.Count == 0 ? null : resolution.Average(), median);
    }

    // One row per group, worst on-time rate first, so the categories or priorities that miss most are at the top.
    public static IReadOnlyList<SlaRow> SlaBy(IEnumerable<TicketRecord> tickets, Func<TicketRecord, string> key, DateTime now) =>
        tickets.GroupBy(t => { var k = key(t); return string.IsNullOrWhiteSpace(k) ? "(none)" : k; }, StringComparer.OrdinalIgnoreCase)
            .Select(g => Sla(g.Key, g.ToList(), now))
            .OrderBy(r => r.OnTimeRate ?? 101).ThenByDescending(r => r.Breached).ThenByDescending(r => r.Tickets).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public sealed record WorkloadRow(Guid? TechnicianId, string Name, string Team, int Open, int OpenIncidents, int OpenRequests, int Overdue, DateTime? OldestOpen, int ClosedInPeriod, double? AverageResolutionHours);

    // Open tickets are counted as they stand now; closed ones are those closed since the start of the period.
    public static IReadOnlyList<WorkloadRow> Workload(IReadOnlyCollection<TicketRecord> tickets, IReadOnlyList<TechnicianRecord> technicians, DateTime now, DateTime since)
    {
        var byTechnician = tickets.GroupBy(t => t.TechnicianId).ToDictionary(g => g.Key ?? Guid.Empty, g => g.ToList());
        WorkloadRow Row(Guid? id, string name, string team)
        {
            var mine = byTechnician.GetValueOrDefault(id ?? Guid.Empty) ?? [];
            var open = mine.Where(t => !TicketInsights.IsClosed(t)).ToList();
            var closed = mine.Where(t => ClosedTime(t) is { } c && c >= since).ToList();
            var resolution = closed.Select(t => Math.Max(0, (ClosedTime(t)!.Value - t.CreatedAt).TotalHours)).ToList();
            return new WorkloadRow(id, name, team, open.Count, open.Count(t => t.Type == TicketTypes.Incident), open.Count(t => t.Type == TicketTypes.Request),
                open.Count(t => TicketInsights.IsOverdue(t, now)), open.Count == 0 ? null : open.Min(t => t.CreatedAt), closed.Count, resolution.Count == 0 ? null : resolution.Average());
        }
        var rows = technicians.Select(t => Row(t.Id, t.Name, t.Team)).ToList();
        rows.Add(Row(null, "Unassigned", ""));
        return rows.OrderByDescending(r => r.TechnicianId is null ? 0 : 1).ThenByDescending(r => r.Open).ThenByDescending(r => r.ClosedInPeriod).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public sealed record TeamRow(string Team, int Technicians, int Open, int Overdue, int ClosedInPeriod);

    // A team's open tickets are those held by its technicians plus the ones sent to the team but not yet picked up.
    public static IReadOnlyList<TeamRow> WorkloadByTeam(IReadOnlyCollection<TicketRecord> tickets, IReadOnlyList<TechnicianRecord> technicians, DateTime now, DateTime since)
    {
        var teamOf = technicians.ToDictionary(t => t.Id, t => t.Team ?? string.Empty);
        string TeamFor(TicketRecord t) => t.TechnicianId is { } id && teamOf.TryGetValue(id, out var team) && team.Length > 0 ? team : (t.TeamName ?? string.Empty);
        var teams = technicians.Select(t => t.Team).Concat(tickets.Select(t => t.TeamName)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return teams.Select(team =>
            {
                var mine = tickets.Where(t => string.Equals(TeamFor(t), team, StringComparison.OrdinalIgnoreCase)).ToList();
                var open = mine.Where(t => !TicketInsights.IsClosed(t)).ToList();
                return new TeamRow(team!, technicians.Count(t => string.Equals(t.Team, team, StringComparison.OrdinalIgnoreCase)), open.Count,
                    open.Count(t => TicketInsights.IsOverdue(t, now)), mine.Count(t => ClosedTime(t) is { } c && c >= since));
            })
            .OrderByDescending(r => r.Open).ThenBy(r => r.Team, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public sealed record AssetRow(AssetRecord Asset, int Tickets, int Incidents, int Open, DateTime LastTicket);
    public sealed record RequesterRow(UserRecord User, int Tickets, int Open, DateTime LastTicket);
    public sealed record RepeatFaultRow(AssetRecord Asset, string Category, int Tickets, DateTime First, DateTime Last);
    public sealed record CategoryRow(string Category, int Tickets, double SharePercent, int Previous);

    public static IReadOnlyList<AssetRow> AssetsWithMostTickets(IEnumerable<TicketRecord> tickets, IReadOnlyList<AssetRecord> assets, int take)
    {
        var byId = assets.ToDictionary(a => a.Id);
        return tickets.SelectMany(t => t.AssetIds.Distinct().Where(byId.ContainsKey).Select(id => (Id: id, Ticket: t)))
            .GroupBy(x => x.Id)
            .Select(g => new AssetRow(byId[g.Key], g.Count(), g.Count(x => x.Ticket.Type == TicketTypes.Incident), g.Count(x => !TicketInsights.IsClosed(x.Ticket)), g.Max(x => x.Ticket.CreatedAt)))
            .OrderByDescending(r => r.Tickets).ThenByDescending(r => r.LastTicket).ThenBy(r => r.Asset.AssetTag, NaturalComparer.Instance)
            .Take(take).ToList();
    }

    // The same device with the same kind of fault more than once: a sign the first fix did not hold, or the device is failing.
    public static IReadOnlyList<RepeatFaultRow> RepeatFaults(IEnumerable<TicketRecord> tickets, IReadOnlyList<AssetRecord> assets, int take)
    {
        var byId = assets.ToDictionary(a => a.Id);
        return tickets.SelectMany(t => t.AssetIds.Distinct().Where(byId.ContainsKey).Select(id => (Id: id, Ticket: t)))
            .GroupBy(x => (x.Id, Category: x.Ticket.Category.Trim().ToLowerInvariant()))
            .Where(g => g.Count() >= 2)
            .Select(g => new RepeatFaultRow(byId[g.Key.Id], g.First().Ticket.Category, g.Count(), g.Min(x => x.Ticket.CreatedAt), g.Max(x => x.Ticket.CreatedAt)))
            .OrderByDescending(r => r.Tickets).ThenByDescending(r => r.Last).ThenBy(r => r.Asset.AssetTag, NaturalComparer.Instance)
            .Take(take).ToList();
    }

    public static IReadOnlyList<RequesterRow> RequestersWithMostTickets(IEnumerable<TicketRecord> tickets, IReadOnlyList<UserRecord> users, int take)
    {
        var byId = users.ToDictionary(u => u.Id);
        return tickets.Where(t => byId.ContainsKey(t.RequesterId)).GroupBy(t => t.RequesterId)
            .Select(g => new RequesterRow(byId[g.Key], g.Count(), g.Count(t => !TicketInsights.IsClosed(t)), g.Max(t => t.CreatedAt)))
            .OrderByDescending(r => r.Tickets).ThenByDescending(r => r.LastTicket).ThenBy(r => r.User.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take).ToList();
    }

    // Tickets per category in the period, and how many there were in the period of the same length just before it.
    public static IReadOnlyList<CategoryRow> Categories(IReadOnlyCollection<TicketRecord> current, IReadOnlyCollection<TicketRecord> previous)
    {
        var before = previous.GroupBy(t => t.Category, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return current.GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "(none)" : t.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategoryRow(g.Key, g.Count(), current.Count == 0 ? 0 : g.Count() * 100.0 / current.Count, before.GetValueOrDefault(g.Key)))
            .OrderByDescending(r => r.Tickets).ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // "3.5 hours", "2 days": whichever reads more naturally.
    public static string Duration(double? hours)
    {
        if (hours is not { } h) return "—";
        if (h < 1) return $"{Math.Max(1, Math.Round(h * 60)):0} min";
        if (h < 48) return $"{h:0.#} hour{(Math.Round(h, 1) == 1 ? "" : "s")}";
        return $"{h / 24:0.#} days";
    }
}
