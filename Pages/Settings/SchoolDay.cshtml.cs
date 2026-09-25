using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// The school week and its lesson periods, which SLAs measured in work days or periods count against (see SlaClock).
public class SchoolDayModel(HelpdeskStore store) : PageModel
{
    // Monday first, the way a school timetable reads.
    public static readonly DayOfWeek[] Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public IReadOnlyList<DayOfWeek> SchoolDays => store.SchoolDays;
    // What the form shows: the saved periods, or what was posted when a save is refused, so nothing typed is lost.
    public IReadOnlyList<(string Name, string Start, string End)> Rows { get; private set; } = [];
    public IReadOnlyList<SlaDefinition> PeriodSlas => store.Slas.Where(x => x.DurationUnit == SlaUnits.Periods).ToList();
    [TempData] public string? Message { get; set; }
    public string? Error { get; private set; }

    // Worked examples from right now, so the effect of the settings can be checked before relying on them.
    public IReadOnlyList<(string Rule, DateTime? Due)> Examples { get; private set; } = [];
    public DateTime Now { get; } = DateTime.UtcNow;

    public void OnGet() => Load(store.SchoolPeriods.Select(x => (x.Name, Time(x.Start), Time(x.End))));

    public IActionResult OnPostDays(DayOfWeek[]? days)
    {
        Message = store.SaveSchoolDays(days).Message;
        return RedirectToPage();
    }

    // The rows arrive as three parallel lists in page order, so removing a row in the browser needs no renumbering.
    // A row left completely blank is ignored - that is how the spare "add" row and removed rows drop out.
    public IActionResult OnPostPeriods(string[]? name, string[]? start, string[]? end)
    {
        name ??= []; start ??= []; end ??= [];
        var count = new[] { name.Length, start.Length, end.Length }.Max();
        string At(string[] values, int i) => i < values.Length ? values[i]?.Trim() ?? "" : "";
        var rows = Enumerable.Range(0, count).Select(i => (Name: At(name, i), Start: At(start, i), End: At(end, i)))
            .Where(x => x.Name.Length + x.Start.Length + x.End.Length > 0).ToList();

        var periods = new List<SchoolPeriod>();
        foreach (var row in rows)
        {
            if (!TryTime(row.Start, out var from) || !TryTime(row.End, out var to))
            {
                Error = $"Give {(row.Name.Length == 0 ? "every period" : row.Name)} a start and an end time.";
                Load(rows);
                return Page();
            }
            periods.Add(new SchoolPeriod(row.Name, from, to));
        }
        var (ok, message) = store.SaveSchoolPeriods(periods);
        if (!ok)
        {
            Error = message;
            Load(rows);
            return Page();
        }
        Message = message;
        return RedirectToPage();
    }

    private void Load(IEnumerable<(string Name, string Start, string End)> rows)
    {
        Rows = rows.ToList();
        var days = store.SchoolDays;
        var periods = store.SchoolPeriods;
        Examples =
        [
            ("1 work day", SlaClock.Due(Now, 1, SlaUnits.WorkDays, days.ToList(), periods)),
            ("3 work days", SlaClock.Due(Now, 3, SlaUnits.WorkDays, days.ToList(), periods)),
            ("1 period", SlaClock.Due(Now, 1, SlaUnits.Periods, days.ToList(), periods)),
            ("2 periods", SlaClock.Due(Now, 2, SlaUnits.Periods, days.ToList(), periods)),
        ];
    }

    public static string Format(DateTime? utc) => utc?.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture) ?? "—";
    private static string Time(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);
    private static bool TryTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
