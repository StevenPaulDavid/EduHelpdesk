using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Tests;

// The pings a browser asks for (HelpdeskStore.Notifications): who each one reaches, that a browser's "last one I handled"
// stays valid, that waiting for news ends when it arrives, and that none of it disturbs the main save.
public class NotificationTests
{
    private const string Staff = HelpdeskStore.StaffAudience;
    private const string Portal = HelpdeskStore.PortalAudience;
    private const string Test = HelpdeskStore.NotificationKinds.Test;

    [Fact]
    public void A_ping_for_staff_reaches_every_staff_account_but_never_the_portal()
    {
        using var test = new TestStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var id = test.Store.AddNotification(Staff, null, Test);

        Assert.Equal([id], test.Store.NotificationsFor(Staff, first, 0).Items.Select(x => x.Id));
        Assert.Equal([id], test.Store.NotificationsFor(Staff, second, 0).Items.Select(x => x.Id));
        Assert.Empty(test.Store.NotificationsFor(Portal, first, 0).Items);
    }

    [Fact]
    public void Someone_who_cannot_see_tickets_is_not_told_about_them_but_still_gets_what_is_addressed_to_them()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        test.Store.AddNotification(Staff, null, HelpdeskStore.NotificationKinds.NewTicket, 7);
        var mine = test.Store.AddNotification(Staff, account, Test);

        Assert.Equal([mine], test.Store.NotificationsFor(Staff, account, 0, broadcast: false).Items.Select(x => x.Id));
        Assert.Equal(2, test.Store.NotificationsFor(Staff, account, 0, broadcast: true).Items.Count);
        // The cursor still moves on, so being given ticket access later doesn't replay what was missed meanwhile.
        Assert.Equal(mine, test.Store.NotificationsFor(Staff, account, 0, broadcast: false).Cursor);
    }

    [Fact]
    public void A_new_ticket_and_a_reply_carry_the_ticket_number_and_nothing_else()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        test.Store.AddNotification(Staff, null, HelpdeskStore.NotificationKinds.NewTicket, 12);
        test.Store.AddNotification(Staff, null, HelpdeskStore.NotificationKinds.Reply, 12);

        var items = test.Store.NotificationsFor(Staff, account, 0).Items;
        Assert.Equal([HelpdeskStore.NotificationKinds.NewTicket, HelpdeskStore.NotificationKinds.Reply], items.Select(x => x.Kind));
        Assert.All(items, x => Assert.Equal(12, x.TicketNumber));
    }

    [Fact]
    public void A_ping_for_one_account_reaches_only_that_account_and_only_on_its_own_side()
    {
        using var test = new TestStore();
        var mine = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var id = test.Store.AddNotification(Portal, mine, Test, 42);

        var found = Assert.Single(test.Store.NotificationsFor(Portal, mine, 0).Items);
        Assert.Equal((id, 42), (found.Id, found.TicketNumber));
        Assert.Empty(test.Store.NotificationsFor(Portal, someoneElse, 0).Items);
        // The same account id on the staff side is a different person's inbox.
        Assert.Empty(test.Store.NotificationsFor(Staff, mine, 0).Items);
    }

    [Fact]
    public void A_browser_asks_for_what_is_newer_than_the_last_one_it_handled()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var first = test.Store.AddNotification(Staff, account, Test);
        var second = test.Store.AddNotification(Staff, account, Test);
        var third = test.Store.AddNotification(Staff, account, Test);

        Assert.True(first < second && second < third);
        var batch = test.Store.NotificationsFor(Staff, account, first);
        Assert.Equal([second, third], batch.Items.Select(x => x.Id));
        Assert.Equal(third, batch.Cursor);
        Assert.Empty(test.Store.NotificationsFor(Staff, account, third).Items);
    }

    [Fact]
    public void A_browser_with_no_cursor_starts_from_now_instead_of_being_told_everything_on_file()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        test.Store.AddNotification(Staff, account, Test);
        var latest = test.Store.AddNotification(Staff, account, Test);

        var batch = test.Store.NotificationsFor(Staff, account, -1);
        Assert.Empty(batch.Items);
        Assert.Equal(latest, batch.Cursor);
    }

    [Fact]
    public void Ids_keep_rising_across_a_restart_and_after_everything_is_cleared()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var before = test.Store.AddNotification(Staff, account, Test);

        var store = test.Reopen();
        Assert.Equal([before], store.NotificationsFor(Staff, account, 0).Items.Select(x => x.Id));
        var after = store.AddNotification(Staff, account, Test);
        Assert.True(after > before);

        // A browser holding `after` as its cursor must still hear about the next one once the table is emptied and
        // the app restarted - which is why ids are times rather than a counter that starts again at 1.
        store.ClearNotifications();
        var next = test.Reopen().AddNotification(Staff, account, Test);
        Assert.True(next > after);
    }

    [Fact]
    public void Pings_past_their_lifetime_are_dropped_when_the_app_starts()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var kept = test.Store.AddNotification(Staff, account, Test);
        test.Store.AddNotification(Staff, account, Test);
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Notifications SET CreatedAt = $old WHERE Id <> $kept;";
            command.Parameters.AddWithValue("$old", DateTime.UtcNow.AddDays(-15).ToString("O"));
            command.Parameters.AddWithValue("$kept", kept);
            command.ExecuteNonQuery();
        }

        Assert.Equal([kept], test.Reopen().NotificationsFor(Staff, account, 0).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Waiting_ends_the_moment_something_arrives_for_that_account()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var waiting = test.Store.WaitForNotificationsAsync(Staff, account, test.Store.NotificationsFor(Staff, account, -1).Cursor, TimeSpan.FromSeconds(20), CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        // Someone else's ping wakes the request but isn't an answer for it, so it carries on waiting.
        test.Store.AddNotification(Portal, Guid.NewGuid(), Test);
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);

        var id = test.Store.AddNotification(Staff, account, Test);
        var batch = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([id], batch.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Waiting_gives_an_empty_answer_at_the_end_of_the_wait_and_when_the_request_is_dropped()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var cursor = test.Store.NotificationsFor(Staff, account, -1).Cursor;

        var timedOut = await test.Store.WaitForNotificationsAsync(Staff, account, cursor, TimeSpan.FromMilliseconds(150), CancellationToken.None);
        Assert.Empty(timedOut.Items);

        using var dropped = new CancellationTokenSource();
        var waiting = test.Store.WaitForNotificationsAsync(Staff, account, cursor, TimeSpan.FromSeconds(20), dropped.Token);
        dropped.Cancel();
        Assert.Empty((await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Items);
    }

    [Fact]
    public void Pings_never_disturb_the_main_save_and_a_factory_reset_clears_them()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Ada Lovelace");
        test.Store.AddNotification(Staff, null, Test);
        test.AddTicket(requester.Id);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
        test.Store.AddNotification(Staff, null, Test);

        Assert.True(test.Store.ResetFactory("DELETE").Ok);
        Assert.Empty(test.Store.NotificationsFor(Staff, Guid.NewGuid(), 0).Items);
        Assert.Empty(test.Reopen().NotificationsFor(Staff, Guid.NewGuid(), 0).Items);
    }
}
