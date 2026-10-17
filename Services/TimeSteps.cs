namespace EduHelpdesk.Services;

// The steps a chart over time is drawn in: days for a period up to 45 days, weeks (each from a Monday) up to four
// months, calendar months beyond - so the steps read as the school counts them. Start and End are UTC; From and To the
// local days a step covers, for a link to the matching list.
public static class TimeSteps
{
    public const string Day = "day", Week = "week", Month = "month";
    // A chart of years of months is unreadable; only the latest five years are kept.
    public const int MaxSteps = 60;

    public sealed record Step(string Label, string Unit, DateTime Start, DateTime End)
    {
        public DateOnly From => DateOnly.FromDateTime(Start.ToLocalTime());
        public DateOnly To => DateOnly.FromDateTime(End.ToLocalTime()).AddDays(-1);
        // How a tooltip names it: "Mon 6 Oct", "Week of 6 Oct", "Oct 2026".
        public string Name => Unit == Day ? $"{From:ddd d MMM}" : Unit == Week ? $"Week of {From:d MMM}" : Label;
    }

    public static IReadOnlyList<Step> Between(DateTime start, DateTime now, string? unit = null)
    {
        var days = (now - start).TotalDays;
        unit ??= days <= 45 ? Day : days <= 124 ? Week : Month;
        var local = start.ToLocalTime().Date;
        var first = unit == Day ? local : unit == Week ? local.AddDays(-(((int)local.DayOfWeek + 6) % 7)) : new DateTime(local.Year, local.Month, 1);
        var steps = new List<Step>();
        for (var from = first; from.ToUniversalTime() <= now; from = Next(from, unit))
            steps.Add(new Step(unit == Day ? $"{from:d MMM}" : unit == Week ? $"w/c {from:d MMM}" : from.ToString("MMM yyyy"),
                unit, from.ToUniversalTime(), Next(from, unit).ToUniversalTime()));
        return steps.Count > MaxSteps ? steps.Skip(steps.Count - MaxSteps).ToList() : steps;
    }

    private static DateTime Next(DateTime from, string unit) => unit == Day ? from.AddDays(1) : unit == Week ? from.AddDays(7) : from.AddMonths(1);
}
