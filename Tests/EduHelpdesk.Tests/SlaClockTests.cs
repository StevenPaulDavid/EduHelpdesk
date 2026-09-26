namespace EduHelpdesk.Tests;

// SLA due dates. Worked out in UTC so the tests don't depend on the machine's time zone.
public class SlaClockTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DayOfWeek[] Week = SlaClock.DefaultSchoolDays;
    private static readonly SchoolPeriod[] Periods =
    [
        new("P1", new TimeOnly(9, 0), new TimeOnly(10, 0)),
        new("P2", new TimeOnly(10, 0), new TimeOnly(11, 0)),
        new("P3", new TimeOnly(11, 20), new TimeOnly(12, 20)),
    ];

    // Monday 28 September 2026.
    private static DateTime Monday(int hour, int minute = 0) => new(2026, 9, 28, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Clock_units_are_plain_time()
    {
        Assert.Equal(Monday(14), SlaClock.Due(Monday(10), 4, SlaUnits.Hours, Week, Periods, Utc));
        Assert.Equal(Monday(10, 30), SlaClock.Due(Monday(10), 30, SlaUnits.Minutes, Week, Periods, Utc));
        Assert.Equal(Monday(10).AddDays(2), SlaClock.Due(Monday(10), 2, SlaUnits.Days, Week, Periods, Utc));
    }

    [Fact]
    public void Work_days_skip_the_weekend()
    {
        var friday = Monday(15).AddDays(-3);
        Assert.Equal(Monday(15), SlaClock.Due(friday, 1, SlaUnits.WorkDays, Week, Periods, Utc));
    }

    [Fact]
    public void A_weekend_ticket_starts_counting_on_Monday()
    {
        var saturday = Monday(11).AddDays(-2);
        // One work day from Saturday is the end of Monday.
        Assert.Equal(new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc), SlaClock.Due(saturday, 1, SlaUnits.WorkDays, Week, Periods, Utc));
    }

    [Fact]
    public void The_period_under_way_does_not_count()
    {
        Assert.Equal(Monday(11), SlaClock.Due(Monday(9, 40), 1, SlaUnits.Periods, Week, Periods, Utc));
        Assert.Equal(Monday(12, 20), SlaClock.Due(Monday(9, 40), 2, SlaUnits.Periods, Week, Periods, Utc));
    }

    [Fact]
    public void Periods_after_the_last_lesson_carry_to_the_next_school_day()
    {
        Assert.Equal(Monday(10).AddDays(1), SlaClock.Due(Monday(12, 30), 1, SlaUnits.Periods, Week, Periods, Utc));
        var friday = Monday(13).AddDays(-3);
        Assert.Equal(Monday(10), SlaClock.Due(friday, 1, SlaUnits.Periods, Week, Periods, Utc));
    }

    [Fact]
    public void A_periods_SLA_without_a_timetable_has_no_due_date() =>
        Assert.Null(SlaClock.Due(Monday(9), 1, SlaUnits.Periods, Week, [], Utc));

    [Fact]
    public void A_pause_gives_back_clock_time_in_a_clock_SLA() =>
        Assert.Equal(Monday(16), SlaClock.Extend(Monday(14), Monday(9), Monday(11), SlaUnits.Hours, Week, Periods, Utc));

    [Fact]
    public void A_pause_over_one_lesson_moves_a_periods_SLA_on_one_lesson() =>
        // Paused for the whole of P1; due at the end of P2, so it moves to the end of P3 - not simply an hour later.
        Assert.Equal(Monday(12, 20), SlaClock.Extend(Monday(11), Monday(9), Monday(10), SlaUnits.Periods, Week, Periods, Utc));

    [Fact]
    public void A_pause_over_the_weekend_gives_nothing_back_in_work_days()
    {
        var saturday = Monday(0).AddDays(-2);
        Assert.Equal(Monday(15).AddDays(2), SlaClock.Extend(Monday(15).AddDays(2), saturday, Monday(0), SlaUnits.WorkDays, Week, Periods, Utc));
    }

    [Fact]
    public void An_empty_school_week_counts_every_day_rather_than_never() =>
        Assert.Equal(Monday(15).AddDays(-2), SlaClock.Due(Monday(15).AddDays(-3), 1, SlaUnits.WorkDays, [], Periods, Utc));
}
