namespace EduHelpdesk.Services;

// Academic years for the finance and audit report. Kept in one place because the arithmetic is easy to get subtly
// wrong: a date in July belongs to the year that started the previous September, not the one starting this September.
// The start month is configurable (HelpdeskStore.AcademicYearStartMonth) because Trusts differ - most schools use
// September, some align to August for financial reasons.
public static class AcademicYear
{
    public const int DefaultStartMonth = 9;

    public static readonly (int Month, string Name)[] Months =
    [
        (1, "January"), (2, "February"), (3, "March"), (4, "April"), (5, "May"), (6, "June"),
        (7, "July"), (8, "August"), (9, "September"), (10, "October"), (11, "November"), (12, "December")
    ];

    // The calendar year the academic year started in. A date before the start month belongs to the previous one.
    public static int StartYearOf(DateOnly date, int startMonth) =>
        date.Month >= Normalize(startMonth) ? date.Year : date.Year - 1;

    public static DateOnly Start(int startYear, int startMonth) => new(startYear, Normalize(startMonth), 1);

    // Exclusive end: the first day of the next academic year, which is what range checks want.
    public static DateOnly EndExclusive(int startYear, int startMonth) => Start(startYear + 1, startMonth);

    public static bool Contains(DateOnly date, int startYear, int startMonth) =>
        date >= Start(startYear, startMonth) && date < EndExclusive(startYear, startMonth);

    // "2025/26". A start month of January gives just "2025", because the year does not straddle two.
    public static string Label(int startYear, int startMonth) =>
        Normalize(startMonth) == 1 ? startYear.ToString() : $"{startYear}/{(startYear + 1) % 100:00}";

    public static string LabelFor(DateOnly date, int startMonth) => Label(StartYearOf(date, startMonth), startMonth);

    // Newest first, and only as far back as there is something to show.
    public static IReadOnlyList<int> StartYearsFrom(IEnumerable<DateOnly> dates, int startMonth, DateOnly today)
    {
        var current = StartYearOf(today, startMonth);
        var years = dates.Select(x => StartYearOf(x, startMonth)).Append(current).Distinct().ToList();
        return years.OrderByDescending(x => x).ToList();
    }

    private static int Normalize(int month) => month is >= 1 and <= 12 ? month : DefaultStartMonth;
}
