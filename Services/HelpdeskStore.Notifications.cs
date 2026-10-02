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
    // time: the newest id there is, which is also right when nothing was found.
    public sealed record NotificationBatch(long Cursor, IReadOnlyList<Notification> Items);

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
            var arrived = _notificationArrived;
            _notificationArrived = NewSignal();
            arrived.TrySetResult();
            return id;
        }
    }

    // What is waiting for this account after the cursor the browser holds. A cursor below zero means "I have none": the
    // answer is empty and carries the current cursor, so a browser that has just been switched on starts from now rather
    // than being told about everything still on file.
    // broadcast says whether this account may be told what is sent to every member of staff: only those who can see
    // tickets should hear that one has arrived. What is addressed to them personally always gets through.
    public NotificationBatch NotificationsFor(string audience, Guid accountId, long after, bool broadcast = true)
    {
        lock (_notificationSync) return BatchFor(audience, accountId, after, broadcast);
    }

    private NotificationBatch BatchFor(string audience, Guid accountId, long after, bool broadcast)
    {
        if (after < 0) return new NotificationBatch(_lastNotificationId, []);
        var items = _notifications
            .Where(x => x.Id > after && x.Audience == audience && (x.RecipientId == accountId || (broadcast && x.RecipientId is null && audience == StaffAudience)))
            .TakeLast(NotificationsPerAnswer)
            .ToList();
        return new NotificationBatch(_lastNotificationId, items);
    }

    // As NotificationsFor, but when there is nothing yet, waits up to this long for something to arrive - so a browser
    // hears about a ticket the moment it is submitted without asking every few seconds. It also ends when the request
    // does (the browser moved to another page) and when the app is stopping.
    public async Task<NotificationBatch> WaitForNotificationsAsync(string audience, Guid accountId, long after, TimeSpan wait, CancellationToken cancel, bool broadcast = true)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            NotificationBatch batch;
            Task signal;
            lock (_notificationSync)
            {
                batch = BatchFor(audience, accountId, after, broadcast);
                signal = _notificationArrived.Task;
            }
            var remaining = deadline - DateTime.UtcNow;
            if (batch.Items.Count > 0 || after < 0 || remaining <= TimeSpan.Zero || cancel.IsCancellationRequested) return batch;
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
            ExecuteDirect("DELETE FROM Notifications;");
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
        // Ids rise with time, so everything before the oldest survivor is what went.
        var oldestKept = _notifications.Count > 0 ? _notifications[0].Id : _lastNotificationId + 1;
        ExecuteDirect("DELETE FROM Notifications WHERE Id < $oldest;", ("$oldest", oldestKept));
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
            using var reader = command.ExecuteReader();
            while (reader.Read())
                _notifications.Add(new Notification(reader.GetInt64(0), reader.GetString(1),
                    !reader.IsDBNull(2) && Guid.TryParse(reader.GetString(2), out var recipient) ? recipient : null,
                    reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), Date(reader, 5)));
            _lastNotificationId = _notifications.Count > 0 ? _notifications[^1].Id : 0;
            _nextNotificationPrune = DateTime.UtcNow.AddHours(1);
        }
    }

    private static void EnsureNotificationSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Notifications (Id INTEGER PRIMARY KEY, Audience TEXT NOT NULL, RecipientId TEXT NULL, Kind TEXT NOT NULL, TicketNumber INTEGER NULL, CreatedAt TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }
}
