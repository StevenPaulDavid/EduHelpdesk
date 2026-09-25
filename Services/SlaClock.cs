using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The units an SLA can be measured in, and how each one turns "logged at" into "due by". Minutes, hours and days are
// plain clock time. Work days and periods follow the school's week and timetable (Settings → School day), because a
// ticket logged on Friday afternoon is not really two days late on Sunday.
public static class SlaUnits
{
    public const string Minutes = "minutes";
    public const string Hours = "hours";
    public const string Days = "days";
    public const string WorkDays = "workdays";
    public const string Periods = "periods";

    public static readonly (string Value, string Label)[] All =
    [
        (Minutes, "Minutes"), (Hours, "Hours"), (Days, "Days"), (WorkDays, "Work days"), (Periods, "Periods"),
    ];

    public static string Normalize(string? value) =>
        All.FirstOrDefault(x => string.Equals(x.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase)).Value ?? Hours;

    // "1 hour", "3 work days", "2 periods" - for dropdowns, lists and the audit log.
    public static string Describe(int duration, string unit)
    {
        var singular = Normalize(unit) switch
        {
            Minutes => "minute", Days => "day", WorkDays => "work day", Periods => "period", _ => "hour"
        };
        return $"{duration} {singular}{(duration == 1 ? "" : "s")}";
    }

    public static string Describe(SlaDefinition sla) => Describe(sla.Duration, sla.DurationUnit);
}

public static class SlaClock
{
    // Nothing a school sets up should need to look further ahead than this; it also stops a loop running forever if
    // the settings leave nothing to count (no school days, say).
    private const int MaxDaysAhead = 400;

    public static readonly DayOfWeek[] DefaultSchoolDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    // Times are stored in UTC, but the school week and its periods are local wall-clock times, so the working-out is
    // done in the school's time zone (the server's, since the helpdesk runs on the school's own machine) and converted
    // back. Null when the SLA can't be worked out - a periods SLA with no periods set up.
    public static DateTime? Due(DateTime createdUtc, int duration, string unit, IReadOnlyCollection<DayOfWeek> schoolDays,
        IReadOnlyList<SchoolPeriod> periods, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var created = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        switch (SlaUnits.Normalize(unit))
        {
            case SlaUnits.Minutes: return created.AddMinutes(duration);
            case SlaUnits.Days: return created.AddDays(duration);
            case SlaUnits.Hours: return created.AddHours(duration);
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(created, zone);
        // An empty week would never reach a school day; treat it as every day rather than never being due.
        var days = schoolDays.Count == 0 ? Enum.GetValues<DayOfWeek>().ToHashSet() : schoolDays.ToHashSet();
        var due = SlaUnits.Normalize(unit) == SlaUnits.WorkDays
            ? AddWorkDays(local, duration, days)
            : AddPeriods(local, duration, days, periods);
        return due is { } value ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), zone) : null;
    }

    // Like "days", but only school days count. Due at the same time of day, N school days on. Logged on a weekend, the
    // clock starts at the beginning of the next school day - so 1 work day from Saturday is the end of Monday.
    private static DateTime? AddWorkDays(DateTime local, int count, HashSet<DayOfWeek> schoolDays)
    {
        var at = local;
        for (var guard = 0; !schoolDays.Contains(at.DayOfWeek); guard++)
        {
            if (guard > MaxDaysAhead) return null;
            at = at.Date.AddDays(1);
        }
        for (int added = 0, guard = 0; added < count; guard++)
        {
            if (guard > MaxDaysAhead * 2) return null;
            at = at.AddDays(1);
            if (schoolDays.Contains(at.DayOfWeek)) added++;
        }
        return at;
    }

    // Counts whole periods. The one already under way when the ticket was logged doesn't count - a ticket logged at
    // 09:40 in a 09:00-10:00 period with a 1-period SLA is due at the end of the next period, so there is always at
    // least one whole period to deal with it. Breaks, lunch and after school are simply gaps between periods.
    private static DateTime? AddPeriods(DateTime local, int count, HashSet<DayOfWeek> schoolDays, IReadOnlyList<SchoolPeriod> periods)
    {
        if (periods.Count == 0) return null;
        var ordered = periods.OrderBy(x => x.Start).ToList();
        var counted = 0;
        for (var day = local.Date; day <= local.Date.AddDays(MaxDaysAhead); day = day.AddDays(1))
        {
            if (!schoolDays.Contains(day.DayOfWeek)) continue;
            foreach (var period in ordered)
            {
                if (day + period.Start.ToTimeSpan() < local) continue;
                if (++counted == count) return day + period.End.ToTimeSpan();
            }
        }
        return null;
    }
}
