using EduHelpdesk.Models;
using EduHelpdesk.Services;

namespace EduHelpdesk.Tests;

// The ticket report's trend (TicketReports.Trend): weekly steps for a short period, monthly for a long one, and the
// open count at the end of each step.
public class TicketTrendTests
{
    private static TicketRecord Ticket(DateTime created, DateTime? closed) =>
        new(0, "Projector", "", Guid.NewGuid(), [], null, "Medium", closed is null ? "Open" : "Closed", "Hardware", created, closed);

    [Fact]
    public void ShortPeriodsStepByWeekAndCountWhatIsOpenAtEachEnd()
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var since = now.AddDays(-28);
        var tickets = new[]
        {
            Ticket(now.AddDays(-20), now.AddDays(-2)),
            Ticket(now.AddDays(-20), null),
            Ticket(now.AddDays(-1), null),
        };

        var trend = TicketReports.Trend(tickets, since, now);

        Assert.All(trend, x => Assert.StartsWith("w/c ", x.Label));
        Assert.All(trend, x => Assert.Equal(DayOfWeek.Monday, x.Start.ToLocalTime().DayOfWeek));
        Assert.Equal(3, trend.Sum(x => x.Logged));
        Assert.Equal(1, trend.Sum(x => x.Closed));
        // Now: one closed two days ago, two still open.
        Assert.Equal(2, trend[^1].OpenAtEnd);
        Assert.Equal(0, trend[0].OpenAtEnd);
    }

    [Fact]
    public void LongPeriodsStepByMonthAndKeepTheLatestFiveYears()
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var trend = TicketReports.Trend([Ticket(now.AddYears(-8), null), Ticket(now.AddDays(-3), null)], DateTime.MinValue, now);

        Assert.Equal(60, trend.Count);
        Assert.Equal("Oct 2026", trend[^1].Label);
        Assert.Equal(1, trend[^1].Logged);
        Assert.Equal(2, trend[^1].OpenAtEnd);
    }
}
