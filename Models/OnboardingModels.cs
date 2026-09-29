namespace EduHelpdesk.Models;

// Onboarding a new member of staff (Services/HelpdeskStore.Onboarding.cs). Each new starter gets one ticket, so the
// work sits in the ticket list and keeps a ticket's notes, history and assignee. The OnboardingRecord beside it, keyed
// by the ticket number, holds the checklist and the details that aren't a ticket's.

// When in the new starter's first days a task falls. The stage groups the checklist; the task's own offset from the
// start date sets its due date.
public static class OnboardingStages
{
    public const string BeforeArrival = "Before arrival";
    public const string FirstDay = "First day";
    public const string FirstWeek = "First week";
    public static readonly string[] All = [BeforeArrival, FirstDay, FirstWeek];
    public static string? Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    public static int Order(string stage) => Array.IndexOf(All, stage) is var index and >= 0 ? index : All.Length;
    // What a new task in the stage starts with, in days from the start date.
    public static int DefaultOffset(string stage) => stage switch { BeforeArrival => -5, FirstWeek => 3, _ => 0 };
}

// Who does a task. The onboarding officer may not be a technician; IT tasks can also name the technician doing them.
public static class OnboardingOwners
{
    public const string Officer = "Onboarding officer";
    public const string IT = "IT";
    public static readonly string[] All = [IT, Officer];
    public static string? Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

// A checklist for one kind of new starter - Teacher, Support staff, Supply/cover - edited in Settings → Onboarding.
public record OnboardingTemplate(Guid Id, string Name)
{
    public List<OnboardingTemplateTask> Tasks { get; init; } = [];
}

// OffsetDays is days from the start date: -5 is five days before they start, 0 their first day.
public record OnboardingTemplateTask(Guid Id, string Title, string Stage, int OffsetDays, string Owner);

// One new starter's onboarding. The person themselves is a requester record (StarterId), made when the onboarding
// starts, which is where their name, email, department and location live - and what equipment is assigned to.
public record OnboardingRecord(int TicketNumber, Guid StarterId, DateOnly StartDate, DateTime CreatedAt)
{
    public string JobTitle { get; init; } = "";
    // The template it was started from, by name, so it still says after the template is renamed or deleted.
    public string TemplateName { get; init; } = "";
    public Guid? LineManagerId { get; init; }
    public List<OnboardingTask> Tasks { get; init; } = [];
    // Set when the onboarding was stopped - the person didn't start, say. Its ticket is closed and the checklist stays
    // as it was left.
    public DateTime? CancelledAt { get; init; }

    public bool IsCancelled => CancelledAt is not null;
    public int Done => Tasks.Count(x => x.IsDone);
    public bool AllDone => Tasks.Count > 0 && Tasks.All(x => x.IsDone);
    public IEnumerable<OnboardingTask> ItTasks => Tasks.Where(x => x.Owner == OnboardingOwners.IT);
    public DateOnly DueOn(OnboardingTask task) => StartDate.AddDays(task.OffsetDays);
    public bool IsOverdue(OnboardingTask task, DateOnly today) => !task.IsDone && !IsCancelled && DueOn(task) < today;
    public int OverdueCount(DateOnly today) => Tasks.Count(x => IsOverdue(x, today));
    // In checklist order: stage, then how far into it, then the order the tasks were added.
    public IEnumerable<OnboardingTask> Ordered => Tasks.Select((task, index) => (task, index))
        .OrderBy(x => OnboardingStages.Order(x.task.Stage)).ThenBy(x => x.task.OffsetDays).ThenBy(x => x.index).Select(x => x.task);
}

public record OnboardingTask(Guid Id, string Title, string Stage, int OffsetDays, string Owner)
{
    // For an IT task: the technician doing it. Blank means the IT team as a whole.
    public Guid? TechnicianId { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Actor? CompletedBy { get; init; }
    public bool IsDone => CompletedAt is not null;
}
