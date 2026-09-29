using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Onboarding;

// One new starter's onboarding: the checklist first, their details beside it, and the ticket's notes and history below.
// It opens for the onboarding officer (Onboarding) and for technicians arriving from the ticket list (Tickets) - see the
// policy in Program.cs - and each action here checks what it needs:
// - IT tasks can be ticked, and notes added, with Tickets: Edit or Onboarding: Edit;
// - the officer's tasks, the details, the task list, the lead technician and cancelling need Onboarding: Edit;
// - deleting needs Onboarding: Delete.
public class DetailsModel(HelpdeskStore store) : PageModel
{
    public OnboardingRecord Record { get; private set; } = null!;
    public TicketRecord Ticket { get; private set; } = null!;
    public UserRecord? Starter { get; private set; }
    public UserRecord? LineManager { get; private set; }
    public IReadOnlyDictionary<Guid, string> TechnicianNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyList<TechnicianRecord> ActiveTechnicians { get; private set; } = [];
    public IReadOnlyList<UserRecord> People { get; private set; } = [];
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    [TempData] public string? Message { get; set; }

    public bool CanEdit => store.UserCan(User, Modules.Onboarding, ModulePermission.Edit);
    public bool CanWorkIt => CanEdit || store.UserCan(User, Modules.Tickets, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Onboarding, ModulePermission.Delete);
    public bool CanTick(OnboardingTask task) => !Record.IsCancelled && (task.Owner == OnboardingOwners.IT ? CanWorkIt : CanEdit);
    private bool CanView => store.UserCan(User, Modules.Onboarding, ModulePermission.View) || store.UserCan(User, Modules.Tickets, ModulePermission.View);

    public IActionResult OnGet(int number)
    {
        if (!CanView) return Forbid();
        return Load(number) ? Page() : NotFound();
    }

    public IActionResult OnPostTick(int number, Guid taskId, bool done)
    {
        if (store.FindOnboarding(number) is not { } record) return NotFound();
        if (record.Tasks.FirstOrDefault(x => x.Id == taskId) is not { } task) return Back(number, "That task couldn't be found.");
        if (!(task.Owner == OnboardingOwners.IT ? CanWorkIt : CanEdit)) return Forbid();
        return Back(number, store.SetOnboardingTaskDone(number, taskId, done).Message, $"task-{taskId}");
    }

    public IActionResult OnPostAssignTask(int number, Guid taskId, Guid? technicianId) =>
        CanEdit ? Back(number, store.AssignOnboardingTask(number, taskId, technicianId).Message, $"task-{taskId}") : Forbid();

    public IActionResult OnPostAddTask(int number, string? title, string? stage, int offsetDays, string? owner) =>
        CanEdit ? Back(number, store.AddOnboardingTask(number, title, stage, offsetDays, owner).Message, "checklist") : Forbid();

    public IActionResult OnPostRemoveTask(int number, Guid taskId) =>
        CanEdit ? Back(number, store.RemoveOnboardingTask(number, taskId).Message, "checklist") : Forbid();

    public IActionResult OnPostDetails(int number, string? name, string? email, string? jobTitle, string? department, string? location, DateOnly? startDate, Guid? lineManagerId) =>
        CanEdit ? Back(number, store.UpdateOnboardingDetails(number, new HelpdeskStore.OnboardingDetails(name, email, jobTitle, department, location, startDate, lineManagerId)).Message) : Forbid();

    public IActionResult OnPostTechnician(int number, Guid? technicianId) =>
        CanEdit ? Back(number, store.SetOnboardingTechnician(number, technicianId).Message) : Forbid();

    public IActionResult OnPostCancel(int number, string? reason) =>
        CanEdit ? Back(number, store.CancelOnboarding(number, reason).Message) : Forbid();

    public IActionResult OnPostResume(int number) =>
        CanEdit ? Back(number, store.ResumeOnboarding(number).Message) : Forbid();

    public IActionResult OnPostNote(int number, string? text)
    {
        if (!CanWorkIt) return Forbid();
        if (!store.IsOnboardingTicket(number)) return NotFound();
        var note = (text ?? "").Trim();
        if (note.Length == 0) return Back(number, "Write the note first.", "notes");
        if (note.Length > HelpdeskStore.MaxTicketTextLength) return Back(number, $"Keep notes under {HelpdeskStore.MaxTicketTextLength} characters.", "notes");
        // Always internal: nobody outside the helpdesk sees an onboarding.
        store.AddTicketComment(number, note, isInternal: true);
        return Back(number, "Note added.", "notes");
    }

    public IActionResult OnPostDelete(int number)
    {
        if (!CanDelete) return Forbid();
        if (store.FindOnboarding(number) is not { } record) return NotFound();
        var name = store.Users.FirstOrDefault(x => x.Id == record.StarterId)?.Name ?? "the new starter";
        var error = store.DeleteTicket(number);
        TempData["Message"] = error ?? $"The onboarding for {name} was deleted. They are still in People.";
        return error is null ? RedirectToPage("/Onboarding/Index") : RedirectToPage(new { number });
    }

    private IActionResult Back(int number, string message, string? fragment = null)
    {
        Message = message;
        return RedirectToPage(null, null, new { number }, fragment);
    }

    private bool Load(int number)
    {
        if (store.FindOnboarding(number) is not { } record || store.Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return false;
        Record = record;
        Ticket = ticket;
        var users = store.Users;
        Starter = users.FirstOrDefault(x => x.Id == record.StarterId);
        LineManager = record.LineManagerId is { } manager ? users.FirstOrDefault(x => x.Id == manager) : null;
        var technicians = store.Technicians;
        TechnicianNames = technicians.ToDictionary(x => x.Id, x => x.Name);
        ActiveTechnicians = technicians.Where(x => x.IsActive).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (CanEdit) People = users.Where(x => x.IsActive && x.AnonymisedAt is null && x.Id != record.StarterId).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return true;
    }

    // "Due Mon 25 Oct", "Due today", "2 days overdue" - for a task not yet done.
    public string DueText(OnboardingTask task)
    {
        var due = Record.DueOn(task);
        var days = due.DayNumber - Today.DayNumber;
        return days switch
        {
            0 => "Due today",
            1 => "Due tomorrow",
            < 0 => $"{-days} day{(days == -1 ? "" : "s")} overdue",
            _ => $"Due {due:ddd d MMM}"
        };
    }

    public string OwnerText(OnboardingTask task) =>
        task.Owner == OnboardingOwners.IT && task.TechnicianId is { } tech ? $"IT · {TechnicianNames.GetValueOrDefault(tech, "a former technician")}" : task.Owner;
}
