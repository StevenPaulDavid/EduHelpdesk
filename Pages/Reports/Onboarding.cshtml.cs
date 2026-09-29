using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// New staff onboardings for whoever runs them and the IT lead: how many people started, whether their checklists were
// finished and how long that took, and which tasks - and whose - keep running late. "Now" figures ignore the period.
// The period (an academic year, or all time) picks onboardings by start date, so a September intake lands in the year
// it joined even when its before-arrival tasks were done in August.
public class OnboardingReportsModel(HelpdeskStore store) : PageModel
{
    public const string AllTime = "all";
    public const int SoonDays = 14;

    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);
    // Rows link through only when the onboarding page would let this person in (the officer, or anyone who works tickets).
    public bool CanOpenOnboarding => store.UserCan(User, Modules.Onboarding, ModulePermission.View) || store.UserCan(User, Modules.Tickets, ModulePermission.View);

    [BindProperty(SupportsGet = true, Name = "year")] public string? Year { get; set; }

    public int StartMonth { get; private set; }
    public IReadOnlyList<int> AvailableYears { get; private set; } = [];
    public int? StartYear { get; private set; }
    public string YearKey => StartYear?.ToString(CultureInfo.InvariantCulture) ?? AllTime;
    public string PeriodLabel { get; private set; } = "";
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);

    public sealed record TaskRow(OnboardingRecord Record, OnboardingTask Task, string Starter, string Doer)
    {
        public DateOnly DueOn => Record.DueOn(Task);
        public DateOnly? DoneOn => Task.CompletedAt is { } at ? DateOnly.FromDateTime(at.ToLocalTime()) : null;
        // Days past its due date: when it was done, or today for one still outstanding. Null when it isn't late (yet).
        public int? DaysLate(DateOnly today) => DoneOn is { } done ? (done > DueOn ? done.DayNumber - DueOn.DayNumber : null)
            : Record.IsOverdue(Task, today) ? today.DayNumber - DueOn.DayNumber : null;
        // Counts towards on-time figures: done, or past due and still outstanding. A task not yet due has no verdict,
        // nor does one left undone when its onboarding was cancelled.
        public bool Judged(DateOnly today) => Task.IsDone || Record.IsOverdue(Task, today);
    }

    public sealed record OnboardingRow(OnboardingRecord Record, string Starter, string Department, string? LineManager, IReadOnlyList<TaskRow> Tasks)
    {
        // The day the last task was ticked, once all are.
        public DateOnly? FinishedOn => Record.AllDone ? Tasks.Max(x => x.DoneOn) : null;
        // Days from the start date to finishing: negative when everything was ready before they arrived.
        public int? DaysToFinish => FinishedOn is { } done ? done.DayNumber - Record.StartDate.DayNumber : null;
        public string Status(DateOnly today) => Record.IsCancelled ? "Cancelled" : Record.AllDone ? "Finished"
            : Record.StartDate > today ? "Upcoming" : "In progress";
    }

    public sealed record LateTaskRow(string Title, string Owner, int Judged, int Late, int StillOverdue, double? MedianDaysLate);
    public sealed record DoerRow(string Name, bool IsTeam, int Tasks, int Done, int OnTime, int Late, int StillOverdue);
    public sealed record TemplateRow(string Name, int Starters, int Finished, int Cancelled, double? MedianDaysToFinish, int Judged, int OnTime);
    public sealed record MonthRow(DateOnly Month, int Starters, int Finished, int Cancelled, int Judged, int Late);

    public IReadOnlyList<OnboardingRow> Active { get; private set; } = [];
    public IReadOnlyList<OnboardingRow> InPeriod { get; private set; } = [];
    public IReadOnlyList<TaskRow> PeriodTasks { get; private set; } = [];
    public IReadOnlyList<MonthRow> ByMonth { get; private set; } = [];
    public IReadOnlyList<LateTaskRow> LateTasks { get; private set; } = [];
    public IReadOnlyList<DoerRow> ByDoer { get; private set; } = [];
    public IReadOnlyList<TemplateRow> ByTemplate { get; private set; } = [];

    public int StartingSoon => Active.Count(x => x.Record.StartDate >= Today && x.Record.StartDate <= Today.AddDays(SoonDays));
    public int OverdueNow => Active.Sum(x => x.Record.OverdueCount(Today));
    public int ItOverdueNow => Active.Sum(x => x.Record.ItTasks.Count(t => x.Record.IsOverdue(t, Today)));
    public IReadOnlyList<OnboardingRow> Started => InPeriod.Where(x => !x.Record.IsCancelled).ToList();
    public int Finished => InPeriod.Count(x => x.FinishedOn is not null);
    public int Cancelled => InPeriod.Count(x => x.Record.IsCancelled);
    public double? MedianDaysToFinish => ProjectReportsModel.Median(InPeriod.Select(x => x.DaysToFinish).OfType<int>());
    public int JudgedTasks => PeriodTasks.Count(x => x.Judged(Today));
    public int OnTimeTasks => PeriodTasks.Count(x => x.Judged(Today) && x.DaysLate(Today) is null);

    public void OnGet()
    {
        StartMonth = store.AcademicYearStartMonth;
        var users = store.Users.ToDictionary(x => x.Id);
        var technicians = store.Technicians.ToDictionary(x => x.Id, x => x.Name);
        string Doer(OnboardingTask task) => task.Owner == OnboardingOwners.IT
            ? task.TechnicianId is { } tech ? technicians.GetValueOrDefault(tech, "Unknown technician") : "IT (anyone)"
            : OnboardingOwners.Officer;
        var all = store.Onboardings.Select(record =>
        {
            var starter = users.GetValueOrDefault(record.StarterId);
            var name = starter?.Name ?? "Unknown";
            return new OnboardingRow(record, name, starter?.Department ?? "",
                record.LineManagerId is { } manager ? users.GetValueOrDefault(manager)?.Name : null,
                record.Ordered.Select(task => new TaskRow(record, task, name, Doer(task))).ToList());
        }).ToList();

        AvailableYears = AcademicYear.StartYearsFrom(all.Select(x => x.Record.StartDate), StartMonth, Today);
        if (string.Equals(Year, AllTime, StringComparison.OrdinalIgnoreCase)) StartYear = null;
        else StartYear = int.TryParse(Year, out var chosen) && AvailableYears.Contains(chosen) ? chosen : AcademicYear.StartYearOf(Today, StartMonth);
        PeriodLabel = StartYear is { } year ? $"Academic year {AcademicYear.Label(year, StartMonth)}" : "All time";

        Active = all.Where(x => !x.Record.IsCancelled && !x.Record.AllDone)
            .OrderBy(x => x.Record.StartDate).ThenBy(x => x.Record.TicketNumber).ToList();
        InPeriod = all.Where(x => StartYear is not { } y || AcademicYear.Contains(x.Record.StartDate, y, StartMonth))
            .OrderBy(x => x.Record.StartDate).ThenBy(x => x.Starter, StringComparer.OrdinalIgnoreCase).ToList();
        PeriodTasks = InPeriod.SelectMany(x => x.Tasks).ToList();

        ByMonth = InPeriod.GroupBy(x => new DateOnly(x.Record.StartDate.Year, x.Record.StartDate.Month, 1)).OrderBy(x => x.Key)
            .Select(group =>
            {
                var tasks = group.SelectMany(x => x.Tasks).Where(x => x.Judged(Today)).ToList();
                return new MonthRow(group.Key, group.Count(x => !x.Record.IsCancelled), group.Count(x => x.FinishedOn is not null),
                    group.Count(x => x.Record.IsCancelled), tasks.Count, tasks.Count(x => x.DaysLate(Today) is not null));
            }).ToList();

        // The same task across different starters, matched by title and owner, worst first.
        LateTasks = PeriodTasks.Where(x => x.Judged(Today))
            .GroupBy(x => (Title: x.Task.Title.Trim(), x.Task.Owner), TitleComparer.Instance)
            .Select(group =>
            {
                var late = group.Select(x => x.DaysLate(Today)).OfType<int>().ToList();
                return new LateTaskRow(group.First().Task.Title.Trim(), group.Key.Owner, group.Count(), late.Count,
                    group.Count(x => !x.Task.IsDone), ProjectReportsModel.Median(late));
            })
            .Where(x => x.Late > 0)
            .OrderByDescending(x => (double)x.Late / x.Judged).ThenByDescending(x => x.Late).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList();

        // Who the tasks fell to: the officer, the IT team as a whole, and each named technician.
        ByDoer = PeriodTasks.Where(x => !x.Record.IsCancelled || x.Task.IsDone).GroupBy(x => x.Doer)
            .Select(group => new DoerRow(group.Key, group.Key is OnboardingOwners.Officer or "IT (anyone)", group.Count(),
                group.Count(x => x.Task.IsDone),
                group.Count(x => x.Task.IsDone && x.DaysLate(Today) is null),
                group.Count(x => x.Task.IsDone && x.DaysLate(Today) is not null),
                group.Count(x => x.Record.IsOverdue(x.Task, Today))))
            .OrderBy(x => x.IsTeam ? 0 : 1).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

        ByTemplate = InPeriod.GroupBy(x => string.IsNullOrWhiteSpace(x.Record.TemplateName) ? "No template" : x.Record.TemplateName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var tasks = group.SelectMany(x => x.Tasks).Where(x => x.Judged(Today)).ToList();
                return new TemplateRow(group.First().Record.TemplateName is { Length: > 0 } name ? name : "No template",
                    group.Count(x => !x.Record.IsCancelled), group.Count(x => x.FinishedOn is not null), group.Count(x => x.Record.IsCancelled),
                    ProjectReportsModel.Median(group.Select(x => x.DaysToFinish).OfType<int>()), tasks.Count, tasks.Count(x => x.DaysLate(Today) is null));
            })
            .OrderByDescending(x => x.Starters).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class TitleComparer : IEqualityComparer<(string Title, string Owner)>
    {
        public static readonly TitleComparer Instance = new();
        public bool Equals((string Title, string Owner) x, (string Title, string Owner) y) =>
            string.Equals(x.Title, y.Title, StringComparison.OrdinalIgnoreCase) && x.Owner == y.Owner;
        public int GetHashCode((string Title, string Owner) value) => HashCode.Combine(value.Title.ToUpperInvariant(), value.Owner);
    }

    public string YearLabel(int startYear) => AcademicYear.Label(startYear, StartMonth);
    public static string Percent(int part, int whole) => whole == 0 ? "—" : $"{Math.Round(100.0 * part / whole)}%";
    // "3 days after start", "On the start date", "2 days before start".
    public static string FinishText(double? days) => days switch
    {
        null => "—",
        0 => "On the start date",
        > 0 and var d => $"{ProjectReportsModel.Days(d)} after start",
        var d => $"{ProjectReportsModel.Days(-d)} before start"
    };

    public IActionResult OnGetExport(string? table)
    {
        if (!CanExport) return Forbid();
        OnGet();
        static string Day(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "";
        var (name, csv) = (table ?? "").ToLowerInvariant() switch
        {
            "onboardings" => ("onboardings", Csv.Table(
                ["Ticket", "Starter", "Job title", "Department", "Line manager", "Template", "Start date", "Status", "Tasks", "Done",
                 "Late or overdue", "Finished", "Days from start to finish", "Cancelled"],
                InPeriod,
                x => [$"#{x.Record.TicketNumber}", x.Starter, x.Record.JobTitle, x.Department, x.LineManager ?? "", x.Record.TemplateName,
                      Day(x.Record.StartDate), x.Status(Today), x.Tasks.Count.ToString(), x.Record.Done.ToString(),
                      x.Tasks.Count(t => t.DaysLate(Today) is not null).ToString(), Day(x.FinishedOn), x.DaysToFinish?.ToString() ?? "",
                      x.Record.CancelledAt is { } at ? Day(DateOnly.FromDateTime(at.ToLocalTime())) : ""])),
            "tasks" => ("onboarding-tasks", Csv.Table(
                ["Ticket", "Starter", "Start date", "Task", "Stage", "Owner", "For", "Due", "Done", "Ticked by", "Days late", "Still overdue"],
                PeriodTasks,
                x => [$"#{x.Record.TicketNumber}", x.Starter, Day(x.Record.StartDate), x.Task.Title, x.Task.Stage,
                      x.Task.Owner, x.Doer, Day(x.DueOn), Day(x.DoneOn), x.Task.CompletedBy?.Name ?? "",
                      x.DaysLate(Today)?.ToString() ?? "", x.Record.IsOverdue(x.Task, Today) ? "Yes" : "No"])),
            _ => ("", "")
        };
        if (name.Length == 0) return NotFound();
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName(name, DateTime.Now));
    }
}
