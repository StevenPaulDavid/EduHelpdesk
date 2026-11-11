namespace EduHelpdesk.Tests;

// The day a ticket is booked in for (planner stage 1): it survives a restart, is written to the history, leaves the due
// date alone, can be set on many tickets at once, and the list can filter and sort by it.
public class PlannedForTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private static TicketContext Context(HelpdeskStore store) =>
        new(store.Users, store.Technicians, store.Assets, store.Priorities, store.Statuses, DateTime.UtcNow, store.TicketDueSoonHours, null);

    [Fact]
    public void A_planned_day_is_saved_kept_over_a_restart_and_written_to_the_history()
    {
        using var test = new TestStore();
        var number = test.AddTicket(test.AddRequester("Priya Shah").Id);
        var before = test.Store.Tickets.Single(x => x.Number == number);
        var thursday = Today.AddDays(3);

        test.Store.UpdateTicket(before with { PlannedFor = thursday });
        Assert.Null(test.Store.CompareDatabaseWithMemory());

        var ticket = test.Reopen().Tickets.Single(x => x.Number == number);
        Assert.Equal(thursday, ticket.PlannedFor);
        // The plan is the team's own: the deadline doesn't move with it.
        Assert.Equal(before.DueDate, ticket.DueDate);
        Assert.Equal(before.DueDateOverridden, ticket.DueDateOverridden);
        Assert.StartsWith("Planned for ", Assert.Single(ticket.History, x => x.Action == "Planned date changed").Details);

        test.Store.UpdateTicket(ticket with { PlannedFor = thursday.AddDays(1) });
        test.Store.UpdateTicket(test.Store.Tickets.Single(x => x.Number == number) with { PlannedFor = null });
        ticket = test.Reopen().Tickets.Single(x => x.Number == number);
        Assert.Null(ticket.PlannedFor);
        var lines = ticket.History.Where(x => x.Action == "Planned date changed").Select(x => x.Details).ToList();
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("Moved from ", lines[1]);
        Assert.Equal("Taken off the plan.", lines[2]);
    }

    [Fact]
    public void Many_tickets_can_be_planned_and_unplanned_at_once_and_closed_ones_are_skipped()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Sam Taylor");
        var first = test.AddTicket(person.Id);
        var second = test.AddTicket(person.Id, "Whiteboard pen tray loose");
        var closed = test.AddTicket(person.Id, "Old job", status: "Closed", closedAt: DateTime.UtcNow);
        var day = Today.AddDays(2).ToString("yyyy-MM-dd");

        Assert.NotNull(test.Store.BulkUpdateTickets([first], new("plan", "next Tuesday")).Error);

        var result = test.Store.BulkUpdateTickets([first, second, closed], new("plan", day));
        Assert.Equal((2, 0, 1), (result.Updated, result.Unchanged, result.Skipped));
        Assert.Equal("A closed ticket can't be planned.", result.SkippedReason);
        Assert.Equal(Today.AddDays(2), test.Store.Tickets.Single(x => x.Number == first).PlannedFor);
        Assert.Null(test.Store.Tickets.Single(x => x.Number == closed).PlannedFor);

        // The same day again changes nothing; an empty date takes them off the plan.
        Assert.Equal(2, test.Store.BulkUpdateTickets([first, second], new("plan", day)).Unchanged);
        Assert.Equal(2, test.Store.BulkUpdateTickets([first, second], new("plan", "")).Updated);
        Assert.All(test.Reopen().Tickets, x => Assert.Null(x.PlannedFor));
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void The_list_filters_by_today_this_week_gone_by_and_not_planned_and_sorts_by_the_day()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Alex Jones");
        int Add(string title, DateOnly? planned, string status = "Open")
        {
            var number = test.AddTicket(person.Id, title, status, closedAt: status == "Closed" ? DateTime.UtcNow : null);
            if (planned is not null) test.Store.UpdateTicket(test.Store.Tickets.Single(x => x.Number == number) with { PlannedFor = planned });
            return number;
        }
        var (weekFrom, weekTo) = TicketInsights.WeekOf(Today);
        var today = Add("today", Today);
        var late = Add("late", Today.AddDays(-8));
        var done = Add("done", Today.AddDays(-8), "Closed");
        var future = Add("future", Today.AddDays(30));
        var unplanned = Add("unplanned", null);
        var mine = new[] { today, late, done, future, unplanned };

        List<int> Run(string? plan = null, DateOnly? on = null, string sort = "number", bool descending = false) =>
            new TicketListQuery { View = "all", Planned = plan, PlannedOn = on, Sort = sort, Descending = descending }
                .Run(test.Store.Tickets.Where(x => mine.Contains(x.Number)), Context(test.Store)).Select(x => x.Number).ToList();

        Assert.Equal([today], Run("today"));
        Assert.Contains(today, Run("week"));
        Assert.DoesNotContain(future, Run("week"));
        Assert.DoesNotContain(late, Run("week"));
        // A closed ticket's day going by is no one's problem, and it isn't waiting to be planned either.
        Assert.Equal([late], Run("late"));
        Assert.Equal([unplanned], Run("none"));
        Assert.Equal([future], Run(on: Today.AddDays(30)));
        Assert.Equal(5, Run().Count);

        // Soonest first, with the unplanned one last whichever way it's sorted.
        Assert.Equal(unplanned, Run(sort: "planned")[^1]);
        Assert.Equal(unplanned, Run(sort: "planned", descending: true)[^1]);
        Assert.Equal(future, Run(sort: "planned", descending: true)[0]);

        Assert.True(weekFrom.DayOfWeek == DayOfWeek.Monday && weekTo.DayOfWeek == DayOfWeek.Sunday && weekFrom <= Today && Today <= weekTo);
    }

    [Fact]
    public void A_planned_day_reads_as_today_tomorrow_or_the_date()
    {
        var day = new DateOnly(2026, 11, 11);
        Assert.Equal("Today", TicketInsights.PlannedText(day, day));
        Assert.Equal("Tomorrow", TicketInsights.PlannedText(day.AddDays(1), day));
        Assert.Equal("Yesterday", TicketInsights.PlannedText(day.AddDays(-1), day));
        Assert.Equal("Mon 16 Nov", TicketInsights.PlannedText(new DateOnly(2026, 11, 16), day));
        Assert.Equal("Mon 4 Jan 2027", TicketInsights.PlannedText(new DateOnly(2027, 1, 4), day));
    }
}
