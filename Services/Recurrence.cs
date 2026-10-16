using System.Globalization;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// When a recurring ticket is due (Settings → Recurring tickets). Every date is counted from the start date, so "every 2
// weeks" stays on the same fortnight however long the helpdesk was off, and the next date is found by walking forward a
// day at a time - simple to get right, and a few hundred steps at most.
public static class Recurrence
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";

    public static readonly (string Value, string Label, string Unit)[] Frequencies =
    [
        (Daily, "Daily", "day"), (Weekly, "Weekly", "week"), (Monthly, "Monthly", "month"), (Yearly, "Yearly", "year"),
    ];

    // WeekOfMonth values for monthly: 0 is "on day MonthDay".
    public static readonly (int Value, string Label)[] WeeksOfMonth = [(1, "first"), (2, "second"), (3, "third"), (4, "fourth"), (5, "last")];
    public const int LastWeek = 5;

    // Monday first, as a school week is read.
    public static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static string Normalize(string? frequency) =>
        Frequencies.FirstOrDefault(x => string.Equals(x.Value, frequency?.Trim(), StringComparison.OrdinalIgnoreCase)).Value ?? Weekly;

    public static int MaxInterval(string frequency) => Normalize(frequency) switch { Daily => 365, Weekly => 52, Monthly => 24, _ => 5 };

    // Far enough to find the next date of anything MaxInterval allows (once every five years, moved past a long weekend).
    private const int MaxDaysAhead = 5 * 366 + 14;

    public static bool Occurs(RecurrenceRule rule, DateOnly day)
    {
        if (day < rule.StartDate || (rule.EndDate is { } end && day > end)) return false;
        var start = rule.StartDate;
        var every = Math.Max(1, rule.Interval);
        switch (Normalize(rule.Frequency))
        {
            case Daily:
                return (day.DayNumber - start.DayNumber) % every == 0;
            case Weekly:
                if (!DaysOf(rule).Contains(day.DayOfWeek)) return false;
                return (MondayOf(day).DayNumber - MondayOf(start).DayNumber) / 7 % every == 0;
            case Monthly:
                var months = (day.Year - start.Year) * 12 + day.Month - start.Month;
                if (months % every != 0) return false;
                if (rule.WeekOfMonth is >= 1 and <= LastWeek)
                    return day.DayOfWeek == rule.WeekDay
                        && (rule.WeekOfMonth == LastWeek ? day.Day + 7 > DateTime.DaysInMonth(day.Year, day.Month) : (day.Day - 1) / 7 + 1 == rule.WeekOfMonth);
                return day.Day == Math.Min(Math.Clamp(rule.MonthDay, 1, 31), DateTime.DaysInMonth(day.Year, day.Month));
            default:
                if ((day.Year - start.Year) % every != 0 || day.Month != rule.Month) return false;
                return day.Day == Math.Min(Math.Clamp(rule.MonthDay, 1, 31), DateTime.DaysInMonth(day.Year, day.Month));
        }
    }

    // The first time the schedule is due after the given school time, or null when it has ended.
    public static DateTime? NextAfter(RecurrenceRule rule, DateTime after, IReadOnlyCollection<DayOfWeek> schoolDays)
    {
        // A week back, because a Saturday date moved to Monday can still be ahead.
        var from = DateOnly.FromDateTime(after).AddDays(-7);
        if (from < rule.StartDate) from = rule.StartDate;
        var last = from.AddDays(MaxDaysAhead);
        for (var day = from; day <= last; day = day.AddDays(1))
        {
            if (rule.EndDate is { } end && day > end) return null;
            if (!Occurs(rule, day)) continue;
            var due = (rule.MoveToSchoolDay ? OnSchoolDay(day, schoolDays) : day).ToDateTime(rule.At);
            if (due > after) return due;
        }
        return null;
    }

    // The day itself when it is a school day, or the next one that is. A school with no school days set keeps the day.
    public static DateOnly OnSchoolDay(DateOnly day, IReadOnlyCollection<DayOfWeek> schoolDays)
    {
        if (schoolDays.Count == 0) return day;
        while (!schoolDays.Contains(day.DayOfWeek)) day = day.AddDays(1);
        return day;
    }

    // Weekly with nothing ticked means the start date's day.
    public static IReadOnlyList<DayOfWeek> DaysOf(RecurrenceRule rule) => rule.Days.Count > 0 ? rule.Days : [rule.StartDate.DayOfWeek];

    // "Every weekday at 07:30", "Every 2 weeks on Mon, Thu at 08:00", "Every month on the last Friday at 07:00".
    public static string Describe(RecurrenceRule rule)
    {
        var every = Math.Max(1, rule.Interval);
        var unit = Frequencies.First(x => x.Value == Normalize(rule.Frequency)).Unit;
        var period = every == 1 ? $"Every {unit}" : $"Every {every} {unit}s";
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var when = Normalize(rule.Frequency) switch
        {
            Weekly when every == 1 && DaysOf(rule).Order().SequenceEqual(SlaClock.DefaultSchoolDays.Order()) => "Every weekday",
            Weekly => $"{period} on {string.Join(", ", Week.Where(DaysOf(rule).Contains).Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d)))}",
            Monthly when rule.WeekOfMonth is >= 1 and <= LastWeek => $"{period} on the {WeeksOfMonth.First(x => x.Value == rule.WeekOfMonth).Label} {culture.DateTimeFormat.GetDayName(rule.WeekDay)}",
            Monthly => $"{period} on day {Math.Clamp(rule.MonthDay, 1, 31)}{(rule.MonthDay > 28 ? " (or the month's last day)" : "")}",
            Yearly => $"{period} on {Math.Clamp(rule.MonthDay, 1, 31)} {culture.DateTimeFormat.GetMonthName(Math.Clamp(rule.Month, 1, 12))}",
            _ => period,
        };
        return $"{when} at {rule.At:HH\\:mm}{(rule.MoveToSchoolDay ? ", moved to the next school day" : "")}";
    }

    // Whether two rules fall on the same dates and time - a save that changes this counts from the save.
    public static bool SameTiming(RecurrenceRule a, RecurrenceRule b) =>
        a with { Days = Array.Empty<DayOfWeek>() } == b with { Days = Array.Empty<DayOfWeek>() } && a.Days.Order().SequenceEqual(b.Days.Order());

    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
