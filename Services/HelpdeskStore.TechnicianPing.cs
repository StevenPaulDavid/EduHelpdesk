using System.Text.Json;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The technician ping (Settings → EduNotify): when a ticket is open at one of the chosen priorities with nobody assigned -
// raised that way, or moved up to it later, from the portal, the ticket page or the bulk actions - a notice goes through
// EduNotify to the technicians' PCs. It comes down again once someone is assigned, the ticket is closed or deleted, or its
// priority drops out of the chosen ones. It says only the ticket number and priority, never who raised it or what about.
// EduNotifyClient makes the calls every few seconds; this works out what to send and what to take down.
//
// Seen is the tickets already dealt with: pinged, or let through quietly because they were already waiting when the
// ping was switched on, arrived in a Spiceworks import, or came in while sending was off. It is saved, so a restart
// neither pings everything again nor misses a ticket raised just before it.
public sealed partial class HelpdeskStore
{
    public sealed record TechnicianPingSettingsView(bool Enabled, IReadOnlyList<string> Priorities, string TemplateId, string TemplateName, IReadOnlyList<string> Groups)
    {
        // Everything chosen that a ping needs.
        public bool Complete => Priorities.Count > 0 && TemplateId.Length > 0 && Groups.Count > 0;
        public bool Active => Enabled && Complete;
    }

    // A ping showing on technicians' PCs, kept until it is recalled.
    public sealed record TechnicianPing(int TicketNumber, long MessageId, string Priority, string Title, List<string> Groups, DateTime SentAt, int Online, int Targeted);

    // One pass's work: the pings to send now, and the tickets whose ping should come down.
    public sealed record TechnicianPingPlan(IReadOnlyList<(int TicketNumber, EduNotify.Send Send)> Send, IReadOnlyList<int> Recall);

    public TechnicianPingSettingsView TechnicianPingSettings
    {
        get { lock (_sync) return PingSettings(); }
    }

    public IReadOnlyList<TechnicianPing> TechnicianPings
    {
        get { lock (_sync) return _data.TechnicianPings.OrderByDescending(x => x.SentAt).ToList(); }
    }

    // Called with the lock held.
    private TechnicianPingSettingsView PingSettings() => new(_data.TechnicianPingEnabled, _data.TechnicianPingPriorities.ToList(),
        _data.TechnicianPingTemplateId, _data.TechnicianPingTemplateName, _data.TechnicianPingGroups.ToList());

    // Template and groups have to be ones EduNotify last said the key can use (Test the connection fetches them); with no
    // list fetched yet, only what is already saved can be kept. Switching on needs a priority, a template and a group.
    public (bool Ok, string Message) SetTechnicianPingSettings(bool enabled, IEnumerable<string>? priorities, string? templateId, IEnumerable<string>? groups)
    {
        lock (_sync)
        {
            var before = PingSettings();
            var catalogue = EduNotifyCatalogue;
            var chosenPriorities = (priorities ?? [])
                .Select(x => _data.Priorities.FirstOrDefault(p => string.Equals(p, x?.Trim(), StringComparison.OrdinalIgnoreCase)))
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var id = (templateId ?? "").Trim();
            var name = "";
            if (id.Length > 0)
            {
                if (catalogue?.Sendable.FirstOrDefault(x => x.Id == id) is { } template) name = template.Name;
                else if (id == before.TemplateId) name = before.TemplateName;
                else return (false, "That template isn't one the key may send. Use Test the connection to fetch the list again.");
            }

            var chosenGroups = new List<string>();
            foreach (var group in (groups ?? []).Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var known = catalogue?.Groups.FirstOrDefault(x => string.Equals(x.Name, group, StringComparison.OrdinalIgnoreCase))?.Name
                    ?? before.Groups.FirstOrDefault(x => string.Equals(x, group, StringComparison.OrdinalIgnoreCase));
                if (known is null) return (false, $"EduNotify has no group called \"{group}\". Use Test the connection to fetch the list again.");
                chosenGroups.Add(known);
            }

            var after = new TechnicianPingSettingsView(enabled, chosenPriorities, id, name, chosenGroups);
            if (enabled && !after.Complete)
                return (false, "To switch the ping on, tick at least one priority, choose a template and tick the technicians' group.");
            if (after.Enabled == before.Enabled && after.TemplateId == before.TemplateId && after.Priorities.SequenceEqual(before.Priorities)
                && after.Groups.SequenceEqual(before.Groups)) return (true, "Nothing changed.");

            _data.TechnicianPingEnabled = enabled;
            _data.TechnicianPingPriorities = chosenPriorities;
            _data.TechnicianPingTemplateId = id;
            _data.TechnicianPingTemplateName = name;
            _data.TechnicianPingGroups = chosenGroups;
            // Tickets already waiting at the chosen priorities are let through quietly: switching on (or ticking another
            // priority) shouldn't ping every one of them at once.
            if (after.Active)
                foreach (var ticket in _data.Tickets.Where(PingQualifies)) _data.TechnicianPingSeen.Add(ticket.Number);
            else _data.TechnicianPingSeen.Clear();

            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "EduNotify", "Technician ping " + (after.Active ? "on" : "off"),
                $"Priorities {(chosenPriorities.Count == 0 ? "none" : string.Join(", ", chosenPriorities))}; template {(name.Length == 0 ? "none" : name)}; groups {(chosenGroups.Count == 0 ? "none" : string.Join(", ", chosenGroups))}."));
            Save();
            return (true, after.Active ? "Saved. New tickets at the ticked priorities will ping the technicians' PCs." : "Saved. The technician ping is off.");
        }
    }

    // Called with the lock held. Whether a ticket should have a ping showing: open, at a chosen priority, nobody assigned.
    // A ticket given to a team but no technician is still waiting for someone.
    private bool PingQualifies(TicketRecord ticket) =>
        _data.TechnicianPingEnabled && _data.TechnicianPingTemplateId.Length > 0 && _data.TechnicianPingGroups.Count > 0
        && !TicketInsights.IsClosed(ticket) && ticket.TechnicianId is null
        && _data.TechnicianPingPriorities.Contains(ticket.Priority, StringComparer.OrdinalIgnoreCase);

    // What EduNotifyClient should do this pass. sending is whether EduNotify can be called now: while it can't, tickets
    // that qualify are only noted, so switching sending back on later doesn't ping a backlog of old tickets. A ping that
    // fails stays out of Seen and is tried again next pass.
    public TechnicianPingPlan PlanTechnicianPings(bool sending)
    {
        lock (_sync)
        {
            var tickets = _data.Tickets.ToDictionary(x => x.Number);
            var recall = _data.TechnicianPings
                .Where(ping => !tickets.TryGetValue(ping.TicketNumber, out var ticket) || !PingQualifies(ticket))
                .Select(x => x.TicketNumber).ToList();
            var send = new List<(int, EduNotify.Send)>();
            var seen = _data.TechnicianPingSeen;
            var seenBefore = seen.Count;
            var changed = false;
            if (!PingSettings().Active) seen.Clear();
            else
            {
                var qualifying = _data.Tickets.Where(PingQualifies).ToList();
                var numbers = qualifying.Select(x => x.Number).ToHashSet();
                if (!sending)
                {
                    changed = !seen.SetEquals(numbers);
                    if (changed) _data.TechnicianPingSeen = numbers;
                }
                else
                {
                    // A ticket that stops qualifying is forgotten, so being unassigned or moved up again pings again.
                    seen.RemoveWhere(x => !numbers.Contains(x));
                    var showing = _data.TechnicianPings.Select(x => x.TicketNumber).ToHashSet();
                    foreach (var ticket in qualifying.Where(x => !seen.Contains(x.Number) && !showing.Contains(x.Number)).OrderBy(x => x.Number))
                        send.Add((ticket.Number, PingFor(ticket)));
                }
            }
            if (changed || seen.Count != seenBefore) Save();
            return new(send, recall);
        }
    }

    // Called with the lock held. Generic on purpose: these show on PCs in shared offices. The template decides what of the
    // title and message EduNotify actually takes (it keeps only what the template lets a caller change).
    private EduNotify.Send PingFor(TicketRecord ticket)
    {
        var title = $"{ticket.Priority} ticket #{ticket.Number}";
        if (title.Length > EduNotify.MaxTitle) title = $"Ticket #{ticket.Number}";
        var link = TicketLink(ticket.Number, null);
        var body = $"Ticket #{ticket.Number} needs a technician. Open the helpdesk to pick it up." + (link is null ? "" : $" {link}");
        return new(_data.TechnicianPingTemplateId, false, _data.TechnicianPingGroups.ToList(), title, body, EduNotify.PingReferenceFor(ticket.Number));
    }

    public void RecordTechnicianPingSent(int ticketNumber, EduNotify.Send send, EduNotify.Sent sent)
    {
        lock (_sync)
        {
            var priority = _data.Tickets.FirstOrDefault(x => x.Number == ticketNumber)?.Priority ?? "";
            _data.TechnicianPings.RemoveAll(x => x.TicketNumber == ticketNumber);
            _data.TechnicianPings.Add(new(ticketNumber, sent.Id, priority, send.Title ?? "", send.Groups.ToList(), DateTime.UtcNow, sent.Online, sent.Targeted));
            _data.TechnicianPingSeen.Add(ticketNumber);
            AddStaffNoticeHistory(ticketNumber, "Technicians pinged through EduNotify",
                $"\"{send.Title}\" to {string.Join(", ", send.Groups)}, using the {_data.TechnicianPingTemplateName} template. Shown on {sent.Online} of {sent.Targeted} PCs straight away.");
            Save();
        }
    }

    // The ping is no longer showing. The history line says why, from how the ticket stands now.
    public void RecordTechnicianPingRecalled(int ticketNumber, bool alreadyGone)
    {
        lock (_sync)
        {
            var ping = _data.TechnicianPings.FirstOrDefault(x => x.TicketNumber == ticketNumber);
            if (ping is null) return;
            _data.TechnicianPings.Remove(ping);
            var ticket = _data.Tickets.FirstOrDefault(x => x.Number == ticketNumber);
            var why = ticket is null ? "" : TicketInsights.IsClosed(ticket) ? "when the ticket was closed"
                : ticket.TechnicianId is { } id ? $"when it was assigned to {_data.Technicians.FirstOrDefault(x => x.Id == id)?.Name ?? "a technician"}"
                : !_data.TechnicianPingPriorities.Contains(ticket.Priority, StringComparer.OrdinalIgnoreCase) ? $"when its priority changed to {ticket.Priority}"
                : "when the technician ping was switched off";
            AddStaffNoticeHistory(ticketNumber, "Technician ping recalled", $"Taken off technicians' PCs {why}{(alreadyGone ? " (it had already gone from EduNotify)" : "")}.");
            Save();
        }
    }

    // Called with the lock held, by the Spiceworks import and its undo: the tickets they bring in or put back are history,
    // not new work, so they never ping.
    private void SkipTechnicianPings(IEnumerable<int> ticketNumbers)
    {
        if (!PingSettings().Active) return;
        var touched = ticketNumbers.ToHashSet();
        foreach (var ticket in _data.Tickets.Where(x => touched.Contains(x.Number) && PingQualifies(x))) _data.TechnicianPingSeen.Add(ticket.Number);
    }

    // ---- Storage (with the rest of EduNotify's) ----

    private static void ReadTechnicianPing(SqliteConnection connection, StoreData data)
    {
        string? Value(string key) => ExecuteScalar(connection, $"SELECT Value FROM Metadata WHERE Key = '{key}';") as string;
        List<string> List(string key) => (Value(key) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        data.TechnicianPingEnabled = Value("EduNotifyPingEnabled") == "1";
        data.TechnicianPingPriorities = List("EduNotifyPingPriorities");
        data.TechnicianPingTemplateId = Value("EduNotifyPingTemplateId") ?? "";
        data.TechnicianPingTemplateName = Value("EduNotifyPingTemplateName") ?? "";
        data.TechnicianPingGroups = List("EduNotifyPingGroups");
        data.TechnicianPingSeen = List("EduNotifyPingSeen").Select(x => int.TryParse(x, out var n) ? n : 0).Where(x => x > 0).ToHashSet();
        data.TechnicianPings = [];
        if (Value("EduNotifyPings") is { Length: > 0 } pings)
        {
            // As with staff notices: an unreadable list is dropped rather than stopping the helpdesk starting.
            try { data.TechnicianPings = JsonSerializer.Deserialize<List<TechnicianPing>>(pings) ?? []; }
            catch (JsonException) { }
        }
    }

    private static void WriteTechnicianPing(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "EduNotifyPingEnabled", data.TechnicianPingEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "EduNotifyPingPriorities", string.Join('\n', data.TechnicianPingPriorities));
        SetMetadata(connection, transaction, "EduNotifyPingTemplateId", data.TechnicianPingTemplateId);
        SetMetadata(connection, transaction, "EduNotifyPingTemplateName", data.TechnicianPingTemplateName);
        SetMetadata(connection, transaction, "EduNotifyPingGroups", string.Join('\n', data.TechnicianPingGroups));
        SetMetadata(connection, transaction, "EduNotifyPingSeen", string.Join('\n', data.TechnicianPingSeen.Order()));
        SetMetadata(connection, transaction, "EduNotifyPings", data.TechnicianPings.Count == 0 ? "" : JsonSerializer.Serialize(data.TechnicianPings.OrderBy(x => x.TicketNumber)));
    }
}
