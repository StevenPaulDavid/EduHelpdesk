using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// How a ticket moves between the helpdesk and the person who raised it:
// - the SLA clock stopping while a ticket waits (statuses that pause it, such as On Hold);
// - how long after closing a requester's reply still reopens it;
// - files shared between the two sides, and knowing when the other side has written.
public sealed partial class HelpdeskStore
{
    public const int DefaultReopenWindowDays = 14;
    public const int MaxReopenWindowDays = 365;
    // Per portal ticket or reply. Each file is still held to MaxAttachmentBytes.
    public const int MaxPortalAttachments = 3;
    private const string SlaPausedAction = "SLA paused";
    private const string SlaRestartedAction = "SLA restarted";

    // ---- The SLA clock ----

    public IReadOnlyList<string> SlaPauseStatuses { get { lock (_sync) return _data.SlaPauseStatuses.ToList(); } }
    public bool PausesSla(string? status) { lock (_sync) return PausesSlaCore(status); }
    private bool PausesSlaCore(string? status) =>
        !string.IsNullOrWhiteSpace(status) && !IsBuiltInStatus(status) && _data.SlaPauseStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    // Ticking or unticking a status applies straight away to the tickets already in it.
    public string SetStatusPausesSla(string status, bool pauses)
    {
        lock (_sync)
        {
            var name = _data.Statuses.FirstOrDefault(x => string.Equals(x, status?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return "Status was not found.";
            if (IsBuiltInStatus(name)) return "A closed ticket has no SLA clock to stop.";
            if (PausesSlaCore(name) == pauses) return pauses ? $"{name} already stops the SLA clock." : $"{name} already leaves the SLA clock running.";
            if (pauses) _data.SlaPauseStatuses.Add(name);
            else _data.SlaPauseStatuses.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            var changed = ReconcileSlaPauses(x => string.Equals(x.Status, name, StringComparison.OrdinalIgnoreCase));
            Save();
            var tickets = changed == 1 ? "1 ticket" : $"{changed} tickets";
            return pauses
                ? $"{name} now stops the SLA clock.{(changed > 0 ? $" {tickets} in it {(changed == 1 ? "was" : "were")} paused." : "")}"
                : $"{name} no longer stops the SLA clock.{(changed > 0 ? $" {tickets} in it started again, with {(changed == 1 ? "its" : "their")} due dates moved on." : "")}";
        }
    }

    // A due date from the SLA, moved on by every finished pause since `start`. What a priority, category or SLA change
    // recalculates with, so time already spent On Hold isn't lost when the due date is worked out again.
    public DateTime? CalculateDueDate(Guid? slaId, DateTime start, IReadOnlyList<SlaPause> pauses)
    {
        lock (_sync)
        {
            var due = CalculateDueDate(slaId, start);
            if (due is null || _data.Slas.FirstOrDefault(x => x.Id == slaId) is not { } sla) return due;
            foreach (var pause in pauses.Where(x => x.EndedAt is not null && x.StartedAt >= start).OrderBy(x => x.StartedAt))
            {
                // A pause only ever starts before the ticket is due (see WithSlaClock), so this just keeps a recalculation honest.
                if (pause.StartedAt >= due.Value) continue;
                due = SlaClock.Extend(due.Value, pause.StartedAt, pause.EndedAt!.Value, sla.DurationUnit, _data.SchoolDays, _data.Periods);
            }
            return due;
        }
    }

    // The ticket with its clock matching its status. Called under the lock wherever a status can change.
    // - Into a pausing status: the clock stops - unless the ticket is already overdue, since putting it On Hold then
    //   would simply hide a missed deadline.
    // - Out of one (including being closed): the clock starts again and an SLA due date moves on by the working time
    //   paused. A due date typed by hand is a promise to someone, so it is left alone.
    private TicketRecord WithSlaClock(TicketRecord ticket, DateTime now)
    {
        var shouldPause = !TicketInsights.IsClosed(ticket) && PausesSlaCore(ticket.Status);
        if (ticket.IsSlaPaused && !shouldPause)
        {
            var open = ticket.SlaPauses[^1];
            var pauses = ticket.SlaPauses.Take(ticket.SlaPauses.Count - 1).Append(open with { EndedAt = now }).ToList();
            var due = ticket.DueDate;
            if (due is { } current && !ticket.DueDateOverridden && _data.Slas.FirstOrDefault(x => x.Id == ticket.SlaId) is { } sla)
                due = SlaClock.Extend(current, open.StartedAt, now, sla.DurationUnit, _data.SchoolDays, _data.Periods);
            return ticket with { SlaPauses = pauses, DueDate = due };
        }
        if (!ticket.IsSlaPaused && shouldPause && ticket.DueDate is { } dueDate && dueDate > now)
            return ticket with { SlaPauses = [.. ticket.SlaPauses, new SlaPause(now, null)] };
        return ticket;
    }

    // Brings every matching ticket's clock into line with its status, recording what changed. Returns how many changed.
    private int ReconcileSlaPauses(Func<TicketRecord, bool> which)
    {
        var now = DateTime.UtcNow;
        var changed = 0;
        for (var i = 0; i < _data.Tickets.Count; i++)
        {
            var ticket = _data.Tickets[i];
            if (!which(ticket)) continue;
            var next = WithSlaClock(ticket, now);
            if (ReferenceEquals(next, ticket)) continue;
            var history = ticket.History.ToList();
            var from = history.Count;
            AddTicketActivities(history, ticket, next);
            StampActor(history, from);
            _data.Tickets[i] = next with { History = history };
            changed++;
        }
        return changed;
    }

    // The history line for the clock stopping or starting, or null if it did neither.
    private TicketActivity? SlaClockActivity(TicketRecord previous, TicketRecord updated)
    {
        var now = DateTime.UtcNow;
        if (!previous.IsSlaPaused && updated.IsSlaPaused)
            return new(SlaPausedAction, $"The SLA clock stopped while the ticket is {updated.Status}.", now);
        if (previous.IsSlaPaused && !updated.IsSlaPaused && updated.SlaPauses.Count > 0 && updated.SlaPauses[^1] is { EndedAt: { } ended } pause)
        {
            var moved = updated.DueDate != previous.DueDate && updated.DueDate is { } due
                ? $" The due date moved to {due.ToLocalTime():dd MMM yyyy, HH:mm}."
                : updated.DueDateOverridden ? " The due date set by hand was kept." : "";
            return new(SlaRestartedAction, TicketInsights.IsClosed(updated)
                ? $"Closed after {Span(ended - pause.StartedAt)} with the SLA clock stopped, so that time doesn't count against it.{moved}"
                : $"The SLA clock started again after {Span(ended - pause.StartedAt)}.{moved}", now);
        }
        // Moved into a pausing status but already overdue: say why the clock didn't stop.
        if (!updated.IsSlaPaused && previous.Status != updated.Status && PausesSlaCore(updated.Status) && !TicketInsights.IsClosed(updated) && updated.DueDate is { } overdue && overdue <= now)
            return new("SLA not paused", $"The ticket was already overdue, so {updated.Status} didn't stop the SLA clock.", now);
        return null;
    }

    private static string Span(TimeSpan span) =>
        span.TotalDays >= 2 ? $"{(int)span.TotalDays} days"
        : span.TotalHours >= 2 ? $"{(int)span.TotalHours} hours"
        : span.TotalMinutes >= 2 ? $"{(int)span.TotalMinutes} minutes"
        : "under two minutes";

    // ---- Replies from the portal ----

    public int ReopenWindowDays { get { lock (_sync) return _data.ReopenWindowDays; } }

    public string SetReopenWindowDays(int days)
    {
        lock (_sync)
        {
            if (days is < 0 or > MaxReopenWindowDays) return $"Enter a number of days between 0 and {MaxReopenWindowDays}.";
            _data.ReopenWindowDays = days;
            Save();
            return days == 0 ? "Replies no longer reopen closed tickets." : $"Replies reopen a closed ticket for {days} day{(days == 1 ? "" : "s")} after it closes.";
        }
    }

    // Open tickets always take a reply; a closed one only within the reopen window.
    public bool RequesterCanReply(TicketRecord ticket) { lock (_sync) return RequesterCanReplyCore(ticket, DateTime.UtcNow); }
    private bool RequesterCanReplyCore(TicketRecord ticket, DateTime now) =>
        !TicketInsights.IsClosed(ticket) || (TicketReports.ClosedTime(ticket) is { } closed && closed > now.AddDays(-_data.ReopenWindowDays));

    // When a closed ticket stops taking replies, for the portal to say so. Null for an open ticket.
    public DateTime? ReplyWindowEnds(TicketRecord ticket)
    {
        lock (_sync)
            return TicketInsights.IsClosed(ticket) && TicketReports.ClosedTime(ticket) is { } closed ? closed.AddDays(_data.ReopenWindowDays) : null;
    }

    // Something the requester hasn't seen yet: a message from IT, or IT changing the status (closing it, putting it On
    // Hold), since they last opened it in the portal. Their own replies and internal notes don't count.
    public static bool HasUpdateForRequester(TicketRecord ticket)
    {
        var seen = ticket.RequesterSeenAt ?? ticket.CreatedAt;
        return ticket.Comments.Any(x => !x.IsInternal && !x.FromRequester && x.CreatedAt > seen)
            || ticket.History.Any(x => x.Action is "Status changed" or "Ticket merged" && x.By?.Id is not null && x.CreatedAt > seen);
    }

    // The requester had the last word: their reply is waiting for IT. Shown on the Tickets list.
    public static bool AwaitingTechnician(TicketRecord ticket) =>
        !TicketInsights.IsClosed(ticket) && ticket.Comments.Where(x => !x.IsInternal).MaxBy(x => x.CreatedAt) is { FromRequester: true };

    // Recorded each time the requester opens the ticket. Written straight to its one column rather than through Save,
    // which would rewrite every table for a page view.
    public void MarkSeenByRequester(int number)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return;
            var now = DateTime.UtcNow;
            _data.Tickets[index] = _data.Tickets[index] with { RequesterSeenAt = now };
            try
            {
                using var connection = new SqliteConnection($"Data Source={_path}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Tickets SET RequesterSeenAt = $seen WHERE Number = $number;";
                command.Parameters.AddWithValue("$seen", Iso(now));
                command.Parameters.AddWithValue("$number", number);
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                // Only the "New reply" flag is lost; the next full save writes the in-memory value anyway.
                Console.Error.WriteLine($"EduHelpdesk: couldn't record that ticket #{number} was seen: {ex.Message}");
            }
        }
    }

    // A technician sharing a file with the requester, or taking it back. The requester's own uploads always stay visible.
    public string? SetAttachmentVisibleToRequester(int number, Guid id, bool visible)
    {
        lock (_sync)
        {
            var index = _data.TicketAttachments.FindIndex(x => x.Id == id && x.TicketNumber == number);
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0 || ticketIndex < 0) return "Attachment was not found.";
            var attachment = _data.TicketAttachments[index];
            if (attachment.FromRequester) return "The requester uploaded that file, so it stays visible to them.";
            if (attachment.VisibleToRequester == visible) return null;
            _data.TicketAttachments[index] = attachment with { VisibleToRequester = visible };
            var history = _data.Tickets[ticketIndex].History.ToList();
            history.Add(new(visible ? "Attachment shared" : "Attachment unshared",
                visible ? $"{attachment.FileName} is now shown to the requester in the staff portal." : $"{attachment.FileName} is no longer shown to the requester.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };
            Save();
            return null;
        }
    }

    public IReadOnlyList<TicketAttachment> GetRequesterAttachments(int number)
    {
        lock (_sync) return _data.TicketAttachments.Where(x => x.TicketNumber == number && x.VisibleToRequester).OrderBy(x => x.UploadedAt).ToList();
    }

    // ---- Storage ----

    private static void EnsureTicketProcessSchema(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS TicketSlaPauses (TicketNumber INTEGER NOT NULL, StartedAt TEXT NOT NULL, EndedAt TEXT NULL,
                    FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
                CREATE TABLE IF NOT EXISTS SlaPauseStatuses (Name TEXT PRIMARY KEY);
                """;
            command.ExecuteNonQuery();
        }
        foreach (var sql in new[]
        {
            "ALTER TABLE TicketComments ADD COLUMN FromRequester INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TicketAttachments ADD COLUMN VisibleToRequester INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TicketAttachments ADD COLUMN FromRequester INTEGER NOT NULL DEFAULT 0;"
        })
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = sql;
            try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
        // Existing tickets count as seen at the upgrade. Otherwise every past reply from IT would show as "New reply".
        using var seen = connection.CreateCommand();
        seen.CommandText = "ALTER TABLE Tickets ADD COLUMN RequesterSeenAt TEXT NULL;";
        try
        {
            seen.ExecuteNonQuery();
            using var backfill = connection.CreateCommand();
            backfill.CommandText = "UPDATE Tickets SET RequesterSeenAt = $now;";
            backfill.Parameters.AddWithValue("$now", Iso(DateTime.UtcNow));
            backfill.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
    }

    private static void ReadTicketProcess(SqliteConnection connection, StoreData data)
    {
        var byNumber = data.Tickets.ToDictionary(x => x.Number);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, StartedAt, EndedAt FROM TicketSlaPauses ORDER BY TicketNumber, StartedAt;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (byNumber.TryGetValue(reader.GetInt32(0), out var ticket))
                    ticket.SlaPauses.Add(new SlaPause(Date(reader, 1), reader.IsDBNull(2) ? null : Date(reader, 2)));
        }
        ReadStrings(connection, "SlaPauseStatuses", data.SlaPauseStatuses);
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'ReopenWindowDays';") as string, out var days)) data.ReopenWindowDays = days;
        // Upgrading: On Hold stops the clock from now on, as a new install's does. After that the list is the school's.
        if (ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SlaPauseStatusesSet';") is null
            && data.Statuses.FirstOrDefault(x => string.Equals(x, "On Hold", StringComparison.OrdinalIgnoreCase)) is { } onHold
            && !data.SlaPauseStatuses.Contains(onHold, StringComparer.OrdinalIgnoreCase))
            data.SlaPauseStatuses.Add(onHold);
    }

    private static void WriteTicketProcessSettings(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        InsertStrings(connection, transaction, "SlaPauseStatuses",
            data.SlaPauseStatuses.Where(x => data.Statuses.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        SetMetadata(connection, transaction, "SlaPauseStatusesSet", "1");
        SetMetadata(connection, transaction, "ReopenWindowDays", data.ReopenWindowDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
