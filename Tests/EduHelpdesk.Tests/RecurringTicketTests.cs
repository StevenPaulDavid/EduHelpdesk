namespace EduHelpdesk.Tests;

// Recurring tickets: which dates a schedule falls on, and the run that logs them - skipped while the last one is open,
// one ticket for dates missed while the helpdesk was off, and nothing lost over a restart.
public class RecurringTicketTests
{
    // Monday 12 October 2026, the week these tests start in.
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly IReadOnlyCollection<DayOfWeek> SchoolWeek = SlaClock.DefaultSchoolDays;

    private static RecurrenceRule Rule(string frequency, int interval = 1, DayOfWeek[]? days = null, int monthDay = 1, int weekOfMonth = 0,
        DayOfWeek weekDay = DayOfWeek.Monday, int month = 9, bool move = false, DateOnly? start = null) =>
        new(frequency, interval, start ?? Monday, null, new TimeOnly(7, 30), days ?? [], monthDay, weekOfMonth, weekDay, month, move);

    private static HelpdeskStore.RecurringTicketInput Input(HelpdeskStore store, Guid requesterId, string frequency = Recurrence.Daily, bool active = true,
        DayOfWeek[]? days = null, string? category = null, string? priority = null, string name = "Backup check") =>
        new(name, active, "Check last night's backup", "Look at the log.", TicketTypes.Request, category ?? store.Categories[0], priority ?? store.Priorities[0],
            requesterId, null, null, null, null, frequency, 1, Monday, null, new TimeOnly(7, 0), days ?? [DayOfWeek.Monday], 1, 0, DayOfWeek.Monday, 9, false);

    [Fact]
    public void Every_other_week_falls_on_the_ticked_days_of_alternate_weeks()
    {
        var rule = Rule(Recurrence.Weekly, 2, [DayOfWeek.Monday, DayOfWeek.Thursday]);
        var dates = Enumerable.Range(0, 21).Select(Monday.AddDays).Where(d => Recurrence.Occurs(rule, d)).ToList();
        Assert.Equal([Monday, Monday.AddDays(3), Monday.AddDays(14), Monday.AddDays(17)], dates);
    }

    [Fact]
    public void Monthly_dates_handle_the_last_weekday_and_short_months()
    {
        var lastFriday = Rule(Recurrence.Monthly, weekOfMonth: Recurrence.LastWeek, weekDay: DayOfWeek.Friday);
        Assert.Equal(new DateTime(2026, 10, 30, 7, 30, 0), Recurrence.NextAfter(lastFriday, new DateTime(2026, 10, 16), SchoolWeek));
        Assert.Equal(new DateTime(2026, 11, 27, 7, 30, 0), Recurrence.NextAfter(lastFriday, new DateTime(2026, 10, 31), SchoolWeek));

        var secondTuesdayQuarterly = Rule(Recurrence.Monthly, 3, weekOfMonth: 2, weekDay: DayOfWeek.Tuesday);
        Assert.Equal(new DateTime(2026, 10, 13, 7, 30, 0), Recurrence.NextAfter(secondTuesdayQuarterly, new DateTime(2026, 10, 12), SchoolWeek));
        Assert.Equal(new DateTime(2027, 1, 12, 7, 30, 0), Recurrence.NextAfter(secondTuesdayQuarterly, new DateTime(2026, 10, 14), SchoolWeek));

        var the31st = Rule(Recurrence.Monthly, monthDay: 31);
        Assert.Equal(new DateTime(2027, 2, 28, 7, 30, 0), Recurrence.NextAfter(the31st, new DateTime(2027, 2, 1), SchoolWeek));
        var yearly = Rule(Recurrence.Yearly, monthDay: 1, month: 9);
        Assert.Equal(new DateTime(2027, 9, 1, 7, 30, 0), Recurrence.NextAfter(yearly, new DateTime(2026, 10, 16), SchoolWeek));
    }

    [Fact]
    public void A_date_on_a_day_off_moves_to_the_next_school_day_only_when_ticked()
    {
        // 1 November 2026 is a Sunday.
        Assert.Equal(new DateTime(2026, 11, 1, 7, 30, 0), Recurrence.NextAfter(Rule(Recurrence.Monthly), new DateTime(2026, 10, 16), SchoolWeek));
        Assert.Equal(new DateTime(2026, 11, 2, 7, 30, 0), Recurrence.NextAfter(Rule(Recurrence.Monthly, move: true), new DateTime(2026, 10, 16), SchoolWeek));
        // A daily schedule moved off the weekend logs once on Monday, not three times.
        var daily = Rule(Recurrence.Daily, move: true);
        Assert.Equal(new DateTime(2026, 10, 19, 7, 30, 0), Recurrence.NextAfter(daily, new DateTime(2026, 10, 16, 8, 0, 0), SchoolWeek));
        Assert.Equal(new DateTime(2026, 10, 20, 7, 30, 0), Recurrence.NextAfter(daily, new DateTime(2026, 10, 19, 7, 31, 0), SchoolWeek));
    }

    [Fact]
    public void Schedules_describe_themselves_in_plain_words()
    {
        Assert.Equal("Every weekday at 07:30", Recurrence.Describe(Rule(Recurrence.Weekly, days: [.. SlaClock.DefaultSchoolDays])));
        Assert.Equal("Every 2 weeks on Mon, Thu at 07:30", Recurrence.Describe(Rule(Recurrence.Weekly, 2, [DayOfWeek.Thursday, DayOfWeek.Monday])));
        Assert.Equal("Every 3 months on the last Friday at 07:30, moved to the next school day",
            Recurrence.Describe(Rule(Recurrence.Monthly, 3, weekOfMonth: Recurrence.LastWeek, weekDay: DayOfWeek.Friday, move: true)));
        Assert.Equal("Every year on 1 September at 07:30", Recurrence.Describe(Rule(Recurrence.Yearly)));
    }

    [Fact]
    public void A_due_schedule_logs_a_ticket_once_skips_while_it_is_open_and_survives_a_restart()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Ada Lovelace");
        var store = test.Store;
        var (ok, message) = store.SaveRecurringTicket(null, Input(store, requester.Id), Monday.ToDateTime(new TimeOnly(6, 0)));
        Assert.True(ok, message);

        Assert.Equal(0, store.RunRecurringTickets(Monday.ToDateTime(new TimeOnly(6, 59))));
        Assert.Equal(1, store.RunRecurringTickets(Monday.ToDateTime(new TimeOnly(7, 1))));
        Assert.Equal(0, store.RunRecurringTickets(Monday.ToDateTime(new TimeOnly(7, 2))));
        var schedule = store.RecurringTickets.Single();
        var ticket = store.Tickets.Single(x => x.Number == schedule.LastTicketNumber);
        Assert.Equal(("Check last night's backup", TicketTypes.Request, requester.Id), (ticket.Title, ticket.Type, ticket.RequesterId));
        Assert.Contains("recurring ticket \"Backup check\"", ticket.History[0].Details);

        // Tuesday: Monday's is still open, so nothing new.
        Assert.Equal(0, store.RunRecurringTickets(Monday.AddDays(1).ToDateTime(new TimeOnly(7, 1))));
        Assert.Contains("still open", store.RecurringTickets.Single().LastResult);

        // Closed, Wednesday's is logged - and after a restart Wednesday is not logged again.
        Assert.True(store.UpdateTicket(ticket with { Status = TicketInsights.ClosedStatus, ClosedAt = DateTime.UtcNow }));
        Assert.Equal(1, store.RunRecurringTickets(Monday.AddDays(2).ToDateTime(new TimeOnly(7, 1))));
        // Saving only what changed wrote the schedule's run state as well as its ticket.
        Assert.Null(store.CompareDatabaseWithMemory());
        var reopened = test.Reopen();
        Assert.Equal(0, reopened.RunRecurringTickets(Monday.AddDays(2).ToDateTime(new TimeOnly(7, 30))));
        var again = reopened.RecurringTickets.Single();
        Assert.Equal(store.RecurringTickets.Single().LastTicketNumber, again.LastTicketNumber);
        Assert.Equal(Monday.AddDays(3).ToDateTime(new TimeOnly(7, 0)), reopened.NextRecurringDue(again));
    }

    [Fact]
    public void Dates_missed_while_the_helpdesk_was_off_make_one_ticket()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Ada Lovelace");
        test.Store.SaveRecurringTicket(null, Input(test.Store, requester.Id, Recurrence.Weekly), Monday.ToDateTime(new TimeOnly(6, 0)));
        Assert.Equal(1, test.Store.RunRecurringTickets(Monday.AddDays(22).ToDateTime(new TimeOnly(12, 0))));
        Assert.Equal(0, test.Store.RunRecurringTickets(Monday.AddDays(22).ToDateTime(new TimeOnly(12, 1))));
        Assert.Equal(Monday.AddDays(28).ToDateTime(new TimeOnly(7, 0)), test.Store.NextRecurringDue(test.Store.RecurringTickets.Single()));
    }

    [Fact]
    public void A_paused_schedule_logs_nothing_and_counts_from_when_it_is_switched_back_on()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Ada Lovelace");
        store.SaveRecurringTicket(null, Input(store, requester.Id, active: false), Monday.ToDateTime(new TimeOnly(6, 0)));
        Assert.Equal(0, store.RunRecurringTickets(Monday.AddDays(3).ToDateTime(new TimeOnly(8, 0))));
        var id = store.RecurringTickets.Single().Id;
        store.SaveRecurringTicket(id, Input(store, requester.Id), Monday.AddDays(3).ToDateTime(new TimeOnly(9, 0)));
        Assert.Equal(0, store.RunRecurringTickets(Monday.AddDays(3).ToDateTime(new TimeOnly(9, 1))));
        Assert.Equal(1, store.RunRecurringTickets(Monday.AddDays(4).ToDateTime(new TimeOnly(7, 0))));
    }

    [Fact]
    public void Schedules_follow_renamed_options_and_hold_on_to_the_ones_they_use()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Ada Lovelace");
        store.AddTicketOption("Category", "Backups");
        store.AddTicketOption("Priority", "Routine");
        Assert.True(store.SaveRecurringTicket(null, Input(store, requester.Id, category: "Backups", priority: "Routine"), DateTime.Now).Ok);

        store.UpdateTicketOption("Category", "Backups", "Backup and restore");
        Assert.Equal("Backup and restore", store.RecurringTickets.Single().Category);
        Assert.Contains("recurring ticket", store.DeleteTicketOption("Priority", "Routine"));
        Assert.Contains("Routine", store.Priorities);
        Assert.Contains("recurring ticket", store.DeleteUser(requester.Id));
    }

    [Fact]
    public void A_schedule_is_refused_without_what_it_needs()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Ada Lovelace");
        Assert.False(store.SaveRecurringTicket(null, Input(store, Guid.NewGuid()), DateTime.Now).Ok);
        Assert.False(store.SaveRecurringTicket(null, Input(store, requester.Id, Recurrence.Weekly, days: []), DateTime.Now).Ok);
        Assert.False(store.SaveRecurringTicket(null, Input(store, requester.Id) with { AssetTags = "NO-SUCH-TAG" }, DateTime.Now).Ok);
        Assert.False(store.SaveRecurringTicket(null, Input(store, requester.Id) with { Interval = 0 }, DateTime.Now).Ok);
        Assert.True(store.SaveRecurringTicket(null, Input(store, requester.Id), DateTime.Now).Ok);
        Assert.False(store.SaveRecurringTicket(null, Input(store, requester.Id), DateTime.Now).Ok);
        Assert.DoesNotContain(store.Tickets, x => x.Title == "Check last night's backup");

        var (logged, _, number) = store.LogRecurringTicketNow(store.RecurringTickets.Single().Id);
        Assert.True(logged);
        Assert.Contains(store.Tickets, x => x.Number == number);
    }
}
