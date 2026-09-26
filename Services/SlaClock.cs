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

    // A due date moved on by the time a pause took out of the SLA's own clock. An On Hold ticket in an hours SLA gets
    // every hour back; in a work-days SLA only the school-day time it spent paused; in a periods SLA only the lesson
    // time. The due date then moves on through the same kind of time - so a pause that covered one whole period moves
    // the due date to the end of the next period, not simply 60 minutes later.
    public static DateTime Extend(DateTime dueUtc, DateTime pausedUtc, DateTime resumedUtc, string unit, IReadOnlyCollection<DayOfWeek> schoolDays,
        IReadOnlyList<SchoolPeriod> periods, TimeZoneInfo? zone = null)
    {
        if (resumedUtc <= pausedUtc) return dueUtc;
        var normalized = SlaUnits.Normalize(unit);
        // Clock units, and a periods SLA with no timetable (which has no due date to move anyway), count every minute.
        if (normalized is SlaUnits.Minutes or SlaUnits.Hours or SlaUnits.Days || (normalized == SlaUnits.Periods && periods.Count == 0))
            return dueUtc + (resumedUtc - pausedUtc);

        zone ??= TimeZoneInfo.Local;
        DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var days = schoolDays.Count == 0 ? Enum.GetValues<DayOfWeek>().ToHashSet() : schoolDays.ToHashSet();
        var from = Local(pausedUtc);
        var to = Local(resumedUtc);
        var due = Local(dueUtc);

        var paused = TimeSpan.Zero;
        foreach (var (start, end) in WorkingTime(from, normalized, days, periods))
        {
            if (start >= to) break;
            var overlap = (end < to ? end : to) - (start > from ? start : from);
            if (overlap > TimeSpan.Zero) paused += overlap;
        }
        if (paused == TimeSpan.Zero) return dueUtc;

        foreach (var (start, end) in WorkingTime(due, normalized, days, periods))
        {
            var begin = start > due ? start : due;
            if (end <= begin) continue;
            if (end - begin >= paused)
                return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(begin + paused, DateTimeKind.Unspecified), zone);
            paused -= end - begin;
        }
        // More than a year of school time to give back - nothing sensible to say, so give back the wall-clock time.
        return dueUtc + (resumedUtc - pausedUtc);
    }

    // The stretches of time the SLA counts, in order from the day of `from`: whole school days for work days, lessons for
    // periods.
    private static IEnumerable<(DateTime Start, DateTime End)> WorkingTime(DateTime from, string unit, HashSet<DayOfWeek> schoolDays, IReadOnlyList<SchoolPeriod> periods)
    {
        var ordered = periods.OrderBy(x => x.Start).ToList();
        for (var day = from.Date; day <= from.Date.AddDays(MaxDaysAhead); day = day.AddDays(1))
        {
            if (!schoolDays.Contains(day.DayOfWeek)) continue;
            if (unit == SlaUnits.WorkDays) { yield return (day, day.AddDays(1)); continue; }
            foreach (var period in ordered) yield return (day + period.Start.ToTimeSpan(), day + period.End.ToTimeSpan());
        }
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
