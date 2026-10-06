using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Short messages for a browser to ping and toast about: "a new ticket has arrived", "your request has an update". Each
// is addressed to staff (everyone with the helpdesk open) or to one account, and a browser asks for what is newer than
// the last one it handled (Services/NotificationEndpoints.cs, wwwroot/js/notifications.js).
// Kept in a table of its own and written directly, like the sessions: a ping must not rewrite the whole database, a
// browser asking every few seconds must not queue behind a save, and Save's rewrite leaves the table alone. A message
// holds only what kind of event it was and the ticket number, never the ticket's words - the toast is generic on purpose,
// because classroom screens get projected.
public sealed partial class HelpdeskStore
{
    public const string StaffAudience = "staff";
    public const string PortalAudience = "portal";

    // What happened. A requester's new ticket and their reply on one go to staff; the test is addressed to whoever pressed it.
    public static class NotificationKinds
    {
        public const string Test = "test";
        public const string NewTicket = "ticket-new";
        public const string Reply = "ticket-reply";
        // A technician's comment, with Notify requester ticked: goes to that requester, on the portal side.
        public const string StaffComment = "staff-comment";
    }

    // An Id is the time it was made, in milliseconds, or one more than the last: it only ever goes up, even after the
    // table has been emptied or the app restarted, so a browser can keep "the last one I handled" and ask for later ones.
    // RecipientId is the account it is for; null means every staff account.
    public sealed record Notification(long Id, string Audience, Guid? RecipientId, string Kind, int? TicketNumber, DateTime CreatedAt);

    // Everything newer than the caller asked for that is addressed to them, oldest first, and the cursor to ask from next
    // time: the newest id there is, which is also right when nothing was found. Unread is the bell's count, when it was
    // asked for (staff only).
    public sealed record NotificationBatch(long Cursor, IReadOnlyList<Notification> Items, int? Unread = null);

    // A day-old ping is stale news, and a fortnight is longer than a school holiday week plus a bank holiday.
    public static readonly TimeSpan NotificationLifetime = TimeSpan.FromDays(14);
    private const int NotificationCap = 5000;
    // The most one answer carries; a browser that has been away longer than this still gets the newest and a cursor.
    private const int NotificationsPerAnswer = 50;

    private readonly List<Notification> _notifications = [];
    private readonly Lock _notificationSync = new();
    private long _lastNotificationId;
    private DateTime _nextNotificationPrune = DateTime.MinValue;
    // Completed whenever something is added, to wake the requests waiting for news; then replaced by a fresh one.
    private TaskCompletionSource _notificationArrived = NewSignal();

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Returns 0, having recorded nothing, when that side is switched off in Settings → Notifications - so turning it back
    // on doesn't announce a backlog.
    public long AddNotification(string audience, Guid? recipientId, string kind, int? ticketNumber = null)
    {
        if (!NotificationsOpenFor(audience)) return 0;
        lock (_notificationSync)
        {
            var now = DateTime.UtcNow;
            var id = Math.Max(_lastNotificationId + 1, new DateTimeOffset(now).ToUnixTimeMilliseconds());
            var notification = new Notification(id, audience, recipientId, kind, ticketNumber, now);
            // A ping that can't be written down must never stop the ticket or comment that caused it, so a failure is
            // logged and the ping still goes out from memory - it only won't survive a restart.
            try
            {
                ExecuteDirect("INSERT INTO Notifications (Id, Audience, RecipientId, Kind, TicketNumber, CreatedAt) VALUES ($id, $audience, $recipient, $kind, $ticket, $created);",
                    ("$id", id), ("$audience", audience), ("$recipient", recipientId?.ToString()), ("$kind", kind), ("$ticket", ticketNumber), ("$created", Iso(now)));
            }
            catch (Exception exception) when (exception is SqliteException or IOException)
            {
                LogProblem(exception, "A notification couldn't be saved");
            }
            _notifications.Add(notification);
            _lastNotificationId = id;
            if (_notifications.Count > NotificationCap || now >= _nextNotificationPrune) PruneNotifications(now);
            WakeNotificationWaiters();
            return id;
        }
    }

    // Called with the lock held.
    private void WakeNotificationWaiters()
    {
        var arrived = _notificationArrived;
        _notificationArrived = NewSignal();
        arrived.TrySetResult();
    }

    // What is waiting for this account after the cursor the browser holds. A cursor below zero means "I have none": the
    // answer is empty and carries the current cursor, so a browser that has just been switched on starts from now rather
    // than being told about everything still on file.
    // broadcast says whether this account may be told what is sent to every member of staff: only those who can see
    // tickets should hear that one has arrived. What is addressed to them personally always gets through.
    public NotificationBatch NotificationsFor(string audience, Guid accountId, long after, bool broadcast = true)
    {
        lock (_notificationSync) return BatchFor(audience, accountId, after, broadcast, countUnread: false);
    }

    private NotificationBatch BatchFor(string audience, Guid accountId, long after, bool broadcast, bool countUnread)
    {
        int? unread = countUnread && audience == StaffAudience ? UnreadFor(accountId, broadcast) : null;
        if (after < 0) return new NotificationBatch(_lastNotificationId, [], unread);
        var items = _notifications
            .Where(x => x.Id > after && x.Audience == audience && (x.RecipientId == accountId || (broadcast && x.RecipientId is null && audience == StaffAudience)))
            .TakeLast(NotificationsPerAnswer)
            .ToList();
        return new NotificationBatch(_lastNotificationId, items, unread);
    }

    // As NotificationsFor, but when there is nothing yet, waits up to this long for something to arrive - so a browser
    // hears about a ticket the moment it is submitted without asking every few seconds. It also ends when the request
    // does (the browser moved to another page) and when the app is stopping.
    // knownUnread is the bell's count as the page shows it (staff only): the wait also ends when the real count differs,
    // so reading something in one tab, or on another computer, clears the badge everywhere else straight away.
    public async Task<NotificationBatch> WaitForNotificationsAsync(string audience, Guid accountId, long after, TimeSpan wait, CancellationToken cancel, bool broadcast = true, int? knownUnread = null)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            NotificationBatch batch;
            Task signal;
            lock (_notificationSync)
            {
                batch = BatchFor(audience, accountId, after, broadcast, countUnread: knownUnread is not null);
                signal = _notificationArrived.Task;
            }
            var remaining = deadline - DateTime.UtcNow;
            if (batch.Items.Count > 0 || after < 0 || (batch.Unread is { } unread && unread != knownUnread) || remaining <= TimeSpan.Zero || cancel.IsCancellationRequested) return batch;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(remaining);
            await Task.WhenAny(signal, Task.Delay(Timeout.Infinite, timeout.Token));
            timeout.Cancel();
        }
    }

    // Everything, for a factory reset: the accounts the messages were addressed to no longer exist.
    public void ClearNotifications()
    {
        lock (_notificationSync)
        {
            _notifications.Clear();
            _readUpTo.Clear();
            _readIds.Clear();
            ExecuteDirect("DELETE FROM Notifications;");
            ExecuteDirect("DELETE FROM NotificationReads;");
            ExecuteDirect("DELETE FROM NotificationReadMarks;");
        }
    }

    // ---- The notification centre: the bell in the helpdesk's header ----
    // A desktop pop-up is gone once it has been missed, so the same messages are also listed under a bell, with a count
    // of those the technician hasn't looked at yet. Unlike the pop-up's "last one this browser handled", what has been
    // read belongs to the account, not the browser - reading one on the office PC clears it on the laptop too.
    // Kept per account as a mark ("everything up to here is read", moved by Mark all as read) plus the ones read
    // individually above it, in two small tables written directly like the messages themselves.

    public sealed record InboxEntry(Notification Notification, bool Unread);
    public sealed record NotificationInbox(int Unread, IReadOnlyList<InboxEntry> Entries);

    private readonly Dictionary<Guid, long> _readUpTo = [];
    private readonly Dictionary<Guid, HashSet<long>> _readIds = [];

    // The test only proves pop-ups work; it isn't something that needs doing, so it never sits in the list.
    private static bool BelongsInCentre(Notification x) => x.Audience == StaffAudience && x.Kind != NotificationKinds.Test;

    private static bool AddressedTo(Notification x, Guid accountId, bool broadcast) =>
        x.RecipientId == accountId || (broadcast && x.RecipientId is null);

    // How many the bell shows. broadcast as for NotificationsFor: only those who can see tickets hear about them.
    public int UnreadNotificationCount(Guid accountId, bool broadcast)
    {
        lock (_notificationSync) return UnreadFor(accountId, broadcast);
    }

    // The newest first, up to take of them, with which are unread.
    public NotificationInbox InboxFor(Guid accountId, bool broadcast, int take)
    {
        lock (_notificationSync)
        {
            var upTo = ReadUpTo(accountId);
            var read = _readIds.GetValueOrDefault(accountId);
            var entries = new List<InboxEntry>();
            for (var i = _notifications.Count - 1; i >= 0 && entries.Count < take; i--)
            {
                var x = _notifications[i];
                if (BelongsInCentre(x) && AddressedTo(x, accountId, broadcast))
                    entries.Add(new InboxEntry(x, x.Id > upTo && read?.Contains(x.Id) != true));
            }
            return new NotificationInbox(UnreadFor(accountId, broadcast), entries);
        }
    }

    public Notification? FindNotification(long id)
    {
        lock (_notificationSync) return _notifications.FirstOrDefault(x => x.Id == id);
    }

    public void MarkNotificationRead(Guid accountId, long id)
    {
        lock (_notificationSync)
        {
            if (_notifications.Any(x => x.Id == id && BelongsInCentre(x) && AddressedTo(x, accountId, broadcast: true)) && MarkRead(accountId, id))
                WakeNotificationWaiters();
        }
    }

    // Opening a ticket - from the bell, a pop-up, the list or anywhere else - deals with whatever the bell was holding
    // about it, so it doesn't sit there unread after the work is done.
    public void MarkTicketNotificationsRead(Guid accountId, int ticketNumber)
    {
        lock (_notificationSync)
        {
            var changed = false;
            foreach (var x in _notifications)
                if (x.TicketNumber == ticketNumber && BelongsInCentre(x) && AddressedTo(x, accountId, broadcast: true))
                    changed |= MarkRead(accountId, x.Id);
            if (changed) WakeNotificationWaiters();
        }
    }

    public void MarkAllNotificationsRead(Guid accountId)
    {
        lock (_notificationSync)
        {
            var hadIds = _readIds.Remove(accountId);
            if (_readUpTo.GetValueOrDefault(accountId, -1) == _lastNotificationId && !hadIds) return;
            _readUpTo[accountId] = _lastNotificationId;
            TryDirect("A notification couldn't be marked as read",
                ("INSERT OR REPLACE INTO NotificationReadMarks (AccountId, ReadUpTo) VALUES ($account, $upTo);", [("$account", accountId.ToString()), ("$upTo", _lastNotificationId)]),
                ("DELETE FROM NotificationReads WHERE AccountId = $account;", [("$account", accountId.ToString())]));
            WakeNotificationWaiters();
        }
    }

    // Called with the lock held. Whether anything changed.
    private bool MarkRead(Guid accountId, long id)
    {
        if (id <= ReadUpTo(accountId)) return false;
        if (!_readIds.TryGetValue(accountId, out var read)) _readIds[accountId] = read = [];
        if (!read.Add(id)) return false;
        TryDirect("A notification couldn't be marked as read",
            ("INSERT OR IGNORE INTO NotificationReads (AccountId, NotificationId) VALUES ($account, $id);", [("$account", accountId.ToString()), ("$id", id)]));
        return true;
    }

    // Called with the lock held. Messages are in id order, so counting stops at the account's mark.
    private int UnreadFor(Guid accountId, bool broadcast)
    {
        var upTo = ReadUpTo(accountId);
        var read = _readIds.GetValueOrDefault(accountId);
        var count = 0;
        for (var i = _notifications.Count - 1; i >= 0 && _notifications[i].Id > upTo; i--)
        {
            var x = _notifications[i];
            if (BelongsInCentre(x) && AddressedTo(x, accountId, broadcast) && read?.Contains(x.Id) != true) count++;
        }
        return count;
    }

    // Called with the lock held. An account the bell hasn't seen before - a new technician, or everyone on the day this
    // was installed - starts with nothing unread rather than a fortnight's backlog.
    private long ReadUpTo(Guid accountId)
    {
        if (_readUpTo.TryGetValue(accountId, out var upTo)) return upTo;
        _readUpTo[accountId] = _lastNotificationId;
        TryDirect("A notification read mark couldn't be saved",
            ("INSERT OR REPLACE INTO NotificationReadMarks (AccountId, ReadUpTo) VALUES ($account, $upTo);", [("$account", accountId.ToString()), ("$upTo", _lastNotificationId)]));
        return _lastNotificationId;
    }

    // Like AddNotification, losing track of what was read must never break the page that asked: it is logged, and holds
    // in memory until the next restart.
    private void TryDirect(string problem, params (string Sql, (string Name, object? Value)[] Values)[] statements)
    {
        try
        {
            foreach (var (sql, values) in statements) ExecuteDirect(sql, values);
        }
        catch (Exception exception) when (exception is SqliteException or IOException)
        {
            LogProblem(exception, problem);
        }
    }

    // Anything past its lifetime, and the oldest beyond the cap. Called with the lock held.
    private void PruneNotifications(DateTime now)
    {
        _nextNotificationPrune = now.AddHours(1);
        var cutoff = now - NotificationLifetime;
        var removed = _notifications.RemoveAll(x => x.CreatedAt < cutoff);
        if (_notifications.Count > NotificationCap)
        {
            removed += _notifications.Count - NotificationCap;
            _notifications.RemoveRange(0, _notifications.Count - NotificationCap);
        }
        if (removed == 0) return;
        // Ids rise with time, so everything before the oldest survivor is what went - and so is any record of reading it.
        var oldestKept = _notifications.Count > 0 ? _notifications[0].Id : _lastNotificationId + 1;
        ExecuteDirect("DELETE FROM Notifications WHERE Id < $oldest;", ("$oldest", oldestKept));
        foreach (var read in _readIds.Values) read.RemoveWhere(id => id < oldestKept);
        ExecuteDirect("DELETE FROM NotificationReads WHERE NotificationId < $oldest;", ("$oldest", oldestKept));
    }

    private void LoadNotifications()
    {
        lock (_notificationSync)
        {
            _notifications.Clear();
            ExecuteDirect("DELETE FROM Notifications WHERE CreatedAt < $cutoff;", ("$cutoff", Iso(DateTime.UtcNow - NotificationLifetime)));
            using var connection = new SqliteConnection($"Data Source={_path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Audience, RecipientId, Kind, TicketNumber, CreatedAt FROM Notifications ORDER BY Id;";
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    _notifications.Add(new Notification(reader.GetInt64(0), reader.GetString(1),
                        !reader.IsDBNull(2) && Guid.TryParse(reader.GetString(2), out var recipient) ? recipient : null,
                        reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), Date(reader, 5)));
            // Ids are times, so a mark can be ahead of the newest message left (all of them pruned); that's still right.
            _lastNotificationId = _notifications.Count > 0 ? _notifications[^1].Id : 0;
            _nextNotificationPrune = DateTime.UtcNow.AddHours(1);

            _readUpTo.Clear();
            _readIds.Clear();
            command.CommandText = "SELECT AccountId, ReadUpTo FROM NotificationReadMarks;";
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (Guid.TryParse(reader.GetString(0), out var account)) _readUpTo[account] = reader.GetInt64(1);
            var oldest = _notifications.Count > 0 ? _notifications[0].Id : long.MaxValue;
            command.CommandText = "SELECT AccountId, NotificationId FROM NotificationReads;";
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (Guid.TryParse(reader.GetString(0), out var account) && reader.GetInt64(1) >= oldest)
                    {
                        if (!_readIds.TryGetValue(account, out var read)) _readIds[account] = read = [];
                        read.Add(reader.GetInt64(1));
                    }
        }
    }

    private static void EnsureNotificationSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Notifications (Id INTEGER PRIMARY KEY, Audience TEXT NOT NULL, RecipientId TEXT NULL, Kind TEXT NOT NULL, TicketNumber INTEGER NULL, CreatedAt TEXT NOT NULL);"
            + "CREATE TABLE IF NOT EXISTS NotificationReadMarks (AccountId TEXT PRIMARY KEY, ReadUpTo INTEGER NOT NULL);"
            + "CREATE TABLE IF NOT EXISTS NotificationReads (AccountId TEXT NOT NULL, NotificationId INTEGER NOT NULL, PRIMARY KEY (AccountId, NotificationId));";
        command.ExecuteNonQuery();
    }
}
