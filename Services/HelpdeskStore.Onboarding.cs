using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Onboarding new staff (Models/OnboardingModels.cs). Each new starter gets one ticket - so the work is in the ticket
// list, with a ticket's assignee, notes and history - and an OnboardingRecord beside it holding the checklist.
//
// The ticket follows the checklist rather than the SLAs: its due date is the earliest task still to do, it closes itself
// when the last task is ticked, and opens again if one is unticked. Everything done to the checklist is written into
// the ticket's history, which is also what the audit log folds in, as for any ticket.
//
// Onboarding tickets are internal: the staff portal never shows them, to the new starter or anyone else (PortalTickets).
public sealed partial class HelpdeskStore
{
    public const string OnboardingCategory = "Onboarding";
    public const int MaxOnboardingNameLength = 120;
    public const int MaxOnboardingTextLength = 150;
    public const int MaxOnboardingOffsetDays = 90;

    public IReadOnlyList<OnboardingTemplate> OnboardingTemplates { get { lock (_sync) return _data.OnboardingTemplates.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(); } }
    public IReadOnlyList<OnboardingRecord> Onboardings { get { lock (_sync) return _data.Onboardings.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.TicketNumber).ToList(); } }
    public OnboardingRecord? FindOnboarding(int number) { lock (_sync) return _data.Onboardings.FirstOrDefault(x => x.TicketNumber == number); }
    public bool IsOnboardingTicket(int number) { lock (_sync) return IsOnboardingTicketCore(number); }
    private bool IsOnboardingTicketCore(int number) => _data.Onboardings.Any(x => x.TicketNumber == number);

    // ---- The staff portal ----

    // A requester's tickets as the portal shows them: their own, less onboarding, which is internal whoever it names.
    public IReadOnlyList<TicketRecord> PortalTickets(Guid requesterId)
    {
        lock (_sync)
        {
            var hidden = _data.Onboardings.Select(x => x.TicketNumber).ToHashSet();
            return _data.Tickets.Where(x => x.RequesterId == requesterId && !hidden.Contains(x.Number)).ToList();
        }
    }

    public TicketRecord? PortalTicket(Guid requesterId, int number)
    {
        lock (_sync) return IsOnboardingTicketCore(number) ? null : _data.Tickets.FirstOrDefault(x => x.Number == number && x.RequesterId == requesterId);
    }

    // ---- Templates (Settings → Onboarding) ----

    // The example checklist, put in place once (see EnsureProjectDefaults for why it is version-gated).
    private void EnsureOnboardingDefaults()
    {
        if (_data.OnboardingVersion >= 1) return;
        if (_data.OnboardingTemplates.Count == 0)
        {
            OnboardingTemplateTask Task(string title, string stage, int offset, string owner) => new(Guid.NewGuid(), title, stage, offset, owner);
            _data.OnboardingTemplates.Add(new OnboardingTemplate(Guid.NewGuid(), "Teacher")
            {
                Tasks =
                [
                    Task("Confirm start date, role and department with HR", OnboardingStages.BeforeArrival, -14, OnboardingOwners.Officer),
                    Task("Create school email account", OnboardingStages.BeforeArrival, -10, OnboardingOwners.IT),
                    Task("Create MIS and network accounts", OnboardingStages.BeforeArrival, -10, OnboardingOwners.IT),
                    Task("Add to staff email groups and Teams", OnboardingStages.BeforeArrival, -5, OnboardingOwners.IT),
                    Task("Build and set up laptop", OnboardingStages.BeforeArrival, -5, OnboardingOwners.IT),
                    Task("Programme keyfob / door access", OnboardingStages.BeforeArrival, -3, OnboardingOwners.IT),
                    Task("Take ID badge photo", OnboardingStages.FirstDay, 0, OnboardingOwners.Officer),
                    Task("Hand over laptop, keyfob and sign-in details", OnboardingStages.FirstDay, 0, OnboardingOwners.IT),
                    Task("Site tour and fire procedures", OnboardingStages.FirstDay, 0, OnboardingOwners.Officer),
                    Task("Sign the acceptable use policy", OnboardingStages.FirstDay, 0, OnboardingOwners.Officer),
                    Task("Check printing, email and classroom display work", OnboardingStages.FirstWeek, 3, OnboardingOwners.IT),
                    Task("Check in: any IT problems?", OnboardingStages.FirstWeek, 5, OnboardingOwners.Officer),
                ]
            });
        }
        _data.OnboardingVersion = 1;
    }

    public (bool Ok, string Message, Guid? Id) AddOnboardingTemplate(string? name, Guid? copyFrom)
    {
        lock (_sync)
        {
            if (CheckTemplateName(name, null) is { } error) return (false, error, null);
            var source = copyFrom is { } id ? _data.OnboardingTemplates.FirstOrDefault(x => x.Id == id) : null;
            var template = new OnboardingTemplate(Guid.NewGuid(), name!.Trim())
            {
                Tasks = source?.Tasks.Select(x => x with { Id = Guid.NewGuid() }).ToList() ?? []
            };
            _data.OnboardingTemplates.Add(template);
            Save();
            return (true, source is null ? $"{template.Name} added. Add its tasks below." : $"{template.Name} added, with {source.Name}'s tasks to start from.", template.Id);
        }
    }

    public (bool Ok, string Message) RenameOnboardingTemplate(Guid id, string? name) => EditTemplate(id, template =>
        CheckTemplateName(name, id) is { } error ? (null, error) : (template with { Name = name!.Trim() }, "Template renamed."));

    public (bool Ok, string Message) DeleteOnboardingTemplate(Guid id)
    {
        lock (_sync)
        {
            var template = _data.OnboardingTemplates.FirstOrDefault(x => x.Id == id);
            if (template is null) return (false, "That template couldn't be found.");
            // Onboardings already started keep their own copy of the checklist, so nothing else changes.
            _data.OnboardingTemplates.Remove(template);
            Save();
            return (true, $"{template.Name} deleted. Onboardings started from it keep their checklists.");
        }
    }

    public (bool Ok, string Message) AddOnboardingTemplateTask(Guid templateId, string? title, string? stage, int offset, string? owner) => EditTemplate(templateId, template =>
    {
        var task = CheckTask(title, stage, offset, owner);
        if (task.Error is { } error) return (null, error);
        return (template with { Tasks = [.. template.Tasks, new OnboardingTemplateTask(Guid.NewGuid(), task.Title, task.Stage, task.Offset, task.Owner)] }, $"\"{task.Title}\" added.");
    });

    public (bool Ok, string Message) UpdateOnboardingTemplateTask(Guid templateId, Guid taskId, string? title, string? stage, int offset, string? owner) => EditTemplate(templateId, template =>
    {
        var index = template.Tasks.FindIndex(x => x.Id == taskId);
        if (index < 0) return (null, "That task couldn't be found.");
        var task = CheckTask(title, stage, offset, owner);
        if (task.Error is { } error) return (null, error);
        var tasks = template.Tasks.ToList();
        tasks[index] = tasks[index] with { Title = task.Title, Stage = task.Stage, OffsetDays = task.Offset, Owner = task.Owner };
        return (template with { Tasks = tasks }, $"\"{task.Title}\" saved.");
    });

    public (bool Ok, string Message) DeleteOnboardingTemplateTask(Guid templateId, Guid taskId) => EditTemplate(templateId, template =>
        template.Tasks.FirstOrDefault(x => x.Id == taskId) is { } task
            ? (template with { Tasks = template.Tasks.Where(x => x.Id != taskId).ToList() }, $"\"{task.Title}\" removed.")
            : (null, "That task couldn't be found."));

    private (bool Ok, string Message) EditTemplate(Guid id, Func<OnboardingTemplate, (OnboardingTemplate? Updated, string Message)> change)
    {
        lock (_sync)
        {
            var index = _data.OnboardingTemplates.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "That template couldn't be found.");
            var (updated, message) = change(_data.OnboardingTemplates[index]);
            if (updated is null) return (false, message);
            _data.OnboardingTemplates[index] = updated;
            Save();
            return (true, message);
        }
    }

    private string? CheckTemplateName(string? name, Guid? except)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0) return "Give the template a name, such as Teacher or Support staff.";
        if (text.Length > 100) return "Keep the template's name under 100 characters.";
        return _data.OnboardingTemplates.Any(x => x.Id != except && string.Equals(x.Name, text, StringComparison.OrdinalIgnoreCase)) ? $"There is already a template called {text}." : null;
    }

    private static (string Title, string Stage, int Offset, string Owner, string? Error) CheckTask(string? title, string? stage, int offset, string? owner)
    {
        var text = (title ?? "").Trim();
        if (text.Length == 0) return ("", "", 0, "", "Say what the task is.");
        if (text.Length > MaxOnboardingTextLength) return ("", "", 0, "", $"Keep the task under {MaxOnboardingTextLength} characters.");
        if (OnboardingStages.Find(stage) is not { } validStage) return ("", "", 0, "", "Choose when the task happens.");
        if (OnboardingOwners.Find(owner) is not { } validOwner) return ("", "", 0, "", "Choose who does the task.");
        if (Math.Abs(offset) > MaxOnboardingOffsetDays) return ("", "", 0, "", $"Keep the task within {MaxOnboardingOffsetDays} days of the start date.");
        return (text, validStage, offset, validOwner, null);
    }

    // ---- Starting an onboarding ----

    public sealed record OnboardingDetails(string? Name, string? Email, string? JobTitle, string? Department, string? Location, DateOnly? StartDate, Guid? LineManagerId);

    // Adds the new starter as a requester - active, but with no password, so they can't sign in to anything yet - and
    // opens their onboarding ticket with the template's checklist.
    public (bool Ok, string Message, int Number) StartOnboarding(OnboardingDetails details, Guid? templateId, Guid? technicianId)
    {
        lock (_sync)
        {
            if (CheckOnboardingDetails(details, null) is { } error) return (false, error, 0);
            var template = templateId is { } id ? _data.OnboardingTemplates.FirstOrDefault(x => x.Id == id) : null;
            if (templateId is not null && template is null) return (false, "That checklist template couldn't be found.", 0);
            if (technicianId is { } tech && !_data.Technicians.Any(x => x.Id == tech && x.IsActive)) return (false, "Choose an active technician, or leave it unassigned.", 0);

            var now = DateTime.UtcNow;
            var starter = WithLeaverDates(new UserRecord(Guid.NewGuid(), details.Name!.Trim(), (details.Email ?? "").Trim(), (details.Department ?? "").Trim(), (details.Location ?? "").Trim()), null);
            _data.Users.Add(starter);

            var number = ++_data.LastTicketNumber;
            var record = new OnboardingRecord(number, starter.Id, details.StartDate!.Value, now)
            {
                JobTitle = (details.JobTitle ?? "").Trim(),
                TemplateName = template?.Name ?? "",
                LineManagerId = details.LineManagerId,
                Tasks = template?.Tasks.Select(x => new OnboardingTask(Guid.NewGuid(), x.Title, x.Stage, x.OffsetDays, x.Owner)).ToList() ?? []
            };
            _data.Onboardings.Add(record);

            var priority = _data.Priorities.FirstOrDefault(x => string.Equals(x, "Normal", StringComparison.OrdinalIgnoreCase)) ?? _data.Priorities.FirstOrDefault() ?? "Normal";
            var ticket = new TicketRecord(number, OnboardingTitle(record, starter), OnboardingDescription(record, starter), starter.Id, [], technicianId,
                priority, OpenStatus() ?? "Open", OnboardingCategory, now, null, null, OnboardingDue(record), true, true, null, NullIfBlank(starter.Location))
            {
                Type = TicketTypes.Request,
                History =
                [
                    new("Ticket created", "The ticket was created.", now) { By = CurrentActor() },
                    new("Onboarding started", $"{starter.Name} starts on {record.StartDate:dddd d MMMM yyyy}. " +
                        (template is null ? "No checklist template was used." : $"Checklist from the {template.Name} template: {Plural(record.Tasks.Count, "task")}."), now) { By = CurrentActor() }
                ]
            };
            _data.Tickets.Add(WithSlaClock(ticket, now));
            Save();
            return (true, $"Onboarding started for {starter.Name}.", number);
        }
    }

    private string? CheckOnboardingDetails(OnboardingDetails details, Guid? starterId)
    {
        var name = (details.Name ?? "").Trim();
        if (name.Length == 0) return "Give the new starter's name.";
        if (name.Length > MaxOnboardingNameLength) return $"Keep the name under {MaxOnboardingNameLength} characters.";
        var email = (details.Email ?? "").Trim();
        // Their school email is often one of the tasks, so it can be added later.
        if (email.Length > 0)
        {
            if (email.Length > 254 || !email.Contains('@') || email.Contains(' ')) return "That email address doesn't look right. Leave it blank if they don't have one yet.";
            if (_data.Users.Any(x => x.Id != starterId && string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase))) return $"{email} is already used by someone in People.";
        }
        if ((details.JobTitle ?? "").Trim().Length > 100) return "Keep the job title under 100 characters.";
        if (!string.IsNullOrWhiteSpace(details.Department) && !_data.Departments.Contains(details.Department.Trim(), StringComparer.OrdinalIgnoreCase)) return "Choose a department from the list.";
        if (!string.IsNullOrWhiteSpace(details.Location) && !_data.Locations.Contains(details.Location.Trim(), StringComparer.OrdinalIgnoreCase)) return "Choose a location from the list.";
        if (details.StartDate is null) return "Give their start date.";
        if (details.LineManagerId is { } manager && !_data.Users.Any(x => x.Id == manager)) return "That line manager couldn't be found.";
        return null;
    }

    // ---- Working an onboarding ----

    public (bool Ok, string Message) UpdateOnboardingDetails(int number, OnboardingDetails details) => EditOnboarding(number, record =>
    {
        if (CheckOnboardingDetails(details, record.StarterId) is { } error) return (null, error, []);
        var userIndex = _data.Users.FindIndex(x => x.Id == record.StarterId);
        if (userIndex < 0) return (null, "The new starter's record in People couldn't be found.", []);
        var before = _data.Users[userIndex];
        var starter = before with { Name = details.Name!.Trim(), Email = (details.Email ?? "").Trim(), Department = (details.Department ?? "").Trim(), Location = (details.Location ?? "").Trim() };
        var updated = record with { JobTitle = (details.JobTitle ?? "").Trim(), StartDate = details.StartDate!.Value, LineManagerId = details.LineManagerId };

        var changes = new List<string>();
        if (before.Name != starter.Name) changes.Add($"name {before.Name} → {starter.Name}");
        if (before.Email != starter.Email) changes.Add($"email {Blank(before.Email)} → {Blank(starter.Email)}");
        if (record.JobTitle != updated.JobTitle) changes.Add($"job title {Blank(record.JobTitle)} → {Blank(updated.JobTitle)}");
        if (before.Department != starter.Department) changes.Add($"department {Blank(before.Department)} → {Blank(starter.Department)}");
        if (before.Location != starter.Location) changes.Add($"location {Blank(before.Location)} → {Blank(starter.Location)}");
        if (record.StartDate != updated.StartDate) changes.Add($"start date {record.StartDate:d MMM yyyy} → {updated.StartDate:d MMM yyyy} (task due dates moved with it)");
        if (record.LineManagerId != updated.LineManagerId) changes.Add($"line manager {ManagerName(record.LineManagerId)} → {ManagerName(updated.LineManagerId)}");
        if (changes.Count == 0) return (null, "Nothing had changed.", []);

        _data.Users[userIndex] = WithLeaverDates(starter, before);
        return (updated, "Details saved.", [Line("Onboarding details changed", Capitalise(string.Join("; ", changes)) + ".")]);
    });

    public (bool Ok, string Message) SetOnboardingTaskDone(int number, Guid taskId, bool done) => EditTask(number, taskId, (record, task) =>
    {
        if (task.IsDone == done) return (null, done ? "That task was already done." : "That task wasn't done yet.", []);
        var updated = done ? task with { CompletedAt = DateTime.UtcNow, CompletedBy = CurrentActor() } : task with { CompletedAt = null, CompletedBy = null };
        return (updated, done ? $"\"{task.Title}\" done." : $"\"{task.Title}\" is to do again.", [Line(done ? "Onboarding task done" : "Onboarding task reopened", task.Title)]);
    });

    // Only IT tasks name a technician: the officer's tasks are the officer's.
    public (bool Ok, string Message) AssignOnboardingTask(int number, Guid taskId, Guid? technicianId) => EditTask(number, taskId, (record, task) =>
    {
        if (task.Owner != OnboardingOwners.IT) return (null, "Only IT tasks are given to a technician.", []);
        if (technicianId is { } tech && !_data.Technicians.Any(x => x.Id == tech && x.IsActive)) return (null, "Choose an active technician.", []);
        if (task.TechnicianId == technicianId) return (null, "Nothing had changed.", []);
        return (task with { TechnicianId = technicianId }, technicianId is null ? $"\"{task.Title}\" is for the IT team." : $"\"{task.Title}\" given to {TechnicianName(technicianId)}.",
            [Line("Onboarding task assigned", $"{task.Title}: {TechnicianName(task.TechnicianId)} → {TechnicianName(technicianId)}")]);
    });

    public (bool Ok, string Message) AddOnboardingTask(int number, string? title, string? stage, int offset, string? owner) => EditOnboarding(number, record =>
    {
        var task = CheckTask(title, stage, offset, owner);
        if (task.Error is { } error) return (null, error, []);
        var added = new OnboardingTask(Guid.NewGuid(), task.Title, task.Stage, task.Offset, task.Owner);
        return (record with { Tasks = [.. record.Tasks, added] }, $"\"{task.Title}\" added.",
            [Line("Onboarding task added", $"{task.Title} ({task.Stage}, {task.Owner}, due {record.DueOn(added):d MMM yyyy})")]);
    });

    public (bool Ok, string Message) RemoveOnboardingTask(int number, Guid taskId) => EditOnboarding(number, record =>
        record.Tasks.FirstOrDefault(x => x.Id == taskId) is { } task
            ? (record with { Tasks = record.Tasks.Where(x => x.Id != taskId).ToList() }, $"\"{task.Title}\" removed.", [Line("Onboarding task removed", task.Title)])
            : (null, "That task couldn't be found.", []));

    // For someone who won't be starting after all. The ticket closes; nothing is deleted, and it can be reopened.
    public (bool Ok, string Message) CancelOnboarding(int number, string? reason) => EditOnboarding(number, record =>
    {
        if (record.IsCancelled) return (null, "This onboarding was already cancelled.", []);
        var why = (reason ?? "").Trim();
        if (why.Length > 500) return (null, "Keep the reason under 500 characters.", []);
        return (record with { CancelledAt = DateTime.UtcNow }, "Onboarding cancelled.", [Line("Onboarding cancelled", why.Length > 0 ? why : "No reason given.")]);
    }, allowCancelled: true);

    public (bool Ok, string Message) ResumeOnboarding(int number) => EditOnboarding(number, record =>
        record.IsCancelled
            ? (record with { CancelledAt = null }, "Onboarding resumed.", [Line("Onboarding resumed", "The onboarding was taken up again.")])
            : (null, "This onboarding isn't cancelled.", []), allowCancelled: true);

    // The ticket's own assignee: the technician who leads the IT side.
    public (bool Ok, string Message) SetOnboardingTechnician(int number, Guid? technicianId)
    {
        lock (_sync)
        {
            if (!IsOnboardingTicketCore(number)) return (false, "That onboarding couldn't be found.");
            if (technicianId is { } tech && !_data.Technicians.Any(x => x.Id == tech && x.IsActive)) return (false, "Choose an active technician.");
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            var ticket = _data.Tickets[index];
            if (ticket.TechnicianId == technicianId) return (false, "Nothing had changed.");
            var history = ticket.History.ToList();
            history.Add(new("Technician changed", technicianId is null ? "The technician assignment was removed." : $"Assigned to {TechnicianName(technicianId)}.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { TechnicianId = technicianId, History = history };
            Save();
            return (true, technicianId is null ? "No technician leads this onboarding now." : $"{TechnicianName(technicianId)} leads the IT side.");
        }
    }

    private (bool Ok, string Message) EditTask(int number, Guid taskId, Func<OnboardingRecord, OnboardingTask, (OnboardingTask? Updated, string Message, IReadOnlyList<TicketActivity> Lines)> change) =>
        EditOnboarding(number, record =>
        {
            var index = record.Tasks.FindIndex(x => x.Id == taskId);
            if (index < 0) return (null, "That task couldn't be found.", []);
            var (task, message, lines) = change(record, record.Tasks[index]);
            if (task is null) return (null, message, []);
            var tasks = record.Tasks.ToList();
            tasks[index] = task;
            return (record with { Tasks = tasks }, message, lines);
        });

    // Every change to an onboarding goes through here: the record changes, the ticket is brought into line with it, and
    // the history lines go on the ticket - all in one save.
    private (bool Ok, string Message) EditOnboarding(int number, Func<OnboardingRecord, (OnboardingRecord? Updated, string Message, IReadOnlyList<TicketActivity> Lines)> change, bool allowCancelled = false)
    {
        lock (_sync)
        {
            var index = _data.Onboardings.FindIndex(x => x.TicketNumber == number);
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0 || ticketIndex < 0) return (false, "That onboarding couldn't be found.");
            if (_data.Onboardings[index].IsCancelled && !allowCancelled) return (false, "This onboarding was cancelled. Resume it to carry on.");
            var (updated, message, lines) = change(_data.Onboardings[index]);
            if (updated is null) return (false, message);
            _data.Onboardings[index] = updated;
            SyncOnboardingTicket(ticketIndex, updated, lines);
            Save();
            return (true, message);
        }
    }

    // The ticket as the checklist says it should be: its title and location from the starter's details, its due date the
    // earliest task still to do, closed when every task is done (or the onboarding is cancelled) and open otherwise.
    private void SyncOnboardingTicket(int ticketIndex, OnboardingRecord record, IReadOnlyList<TicketActivity> lines)
    {
        var now = DateTime.UtcNow;
        var ticket = _data.Tickets[ticketIndex];
        var starter = _data.Users.FirstOrDefault(x => x.Id == record.StarterId);
        var history = ticket.History.ToList();
        var from = history.Count;
        history.AddRange(lines);
        var next = ticket with
        {
            Title = OnboardingTitle(record, starter),
            Location = NullIfBlank(starter?.Location),
            DueDate = OnboardingDue(record),
            DueDateOverridden = true,
            SlaOverridden = true,
            SlaId = null,
        };
        var shouldClose = record.IsCancelled || record.AllDone;
        if (shouldClose && !TicketInsights.IsClosed(next))
        {
            next = next with { Status = TicketInsights.ClosedStatus, ClosedAt = now };
            history.Add(new("Status changed", $"{ticket.Status} -> {TicketInsights.ClosedStatus}", now));
            if (!record.IsCancelled) history.Add(new("Onboarding complete", "Every task is done, so the ticket was closed.", now));
        }
        else if (!shouldClose && TicketInsights.IsClosed(next) && OpenStatus() is { } open)
        {
            next = next with { Status = open, ClosedAt = null };
            history.Add(new("Status changed", $"{ticket.Status} -> {open}", now));
        }
        var clocked = WithSlaClock(next, now);
        if (SlaClockActivity(next, clocked) is { } clockLine) history.Add(clockLine);
        StampActor(history, from);
        _data.Tickets[ticketIndex] = clocked with { History = history };
    }

    private static string OnboardingTitle(OnboardingRecord record, UserRecord? starter)
    {
        var about = string.Join(", ", new[] { record.JobTitle, starter?.Department }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return $"Onboarding: {starter?.Name ?? "new starter"}{(about.Length > 0 ? $" - {about}" : "")}";
    }

    private static string OnboardingDescription(OnboardingRecord record, UserRecord starter) =>
        $"Onboarding for {starter.Name}, starting {record.StartDate:dddd d MMMM yyyy}. The checklist is on the onboarding page.";

    // End of the day the earliest unfinished task is due, as the ticket's due date. Null once nothing is left.
    private static DateTime? OnboardingDue(OnboardingRecord record)
    {
        var open = record.Tasks.Where(x => !x.IsDone).Select(record.DueOn).ToList();
        return open.Count == 0 ? null : DateTime.SpecifyKind(open.Min().ToDateTime(new TimeOnly(23, 59)), DateTimeKind.Local).ToUniversalTime();
    }

    private string? OpenStatus() => _data.Statuses.FirstOrDefault(x => !string.Equals(x, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase));
    private static TicketActivity Line(string action, string details) => new(action, details, DateTime.UtcNow);
    private string TechnicianName(Guid? id) => id is { } tech ? _data.Technicians.FirstOrDefault(x => x.Id == tech)?.Name ?? "a former technician" : "the IT team";
    private string ManagerName(Guid? id) => id is { } user ? _data.Users.FirstOrDefault(x => x.Id == user)?.Name ?? "someone no longer listed" : "none";
    private static string Blank(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    // Called when a ticket goes (deleted, purged, demo data removed): its onboarding goes with it. The starter stays in
    // People - they may be working here by now.
    private void RemoveOnboarding(int number) => _data.Onboardings.RemoveAll(x => x.TicketNumber == number);

    // ---- Storage ----

    private static void EnsureOnboardingSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // No foreign keys: every save rewrites these whole, after Users, Technicians and Tickets, and a starter or
        // technician deleted in between is tolerated where the rows are read back.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS OnboardingTemplates (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS OnboardingTemplateTasks (Id TEXT PRIMARY KEY, TemplateId TEXT NOT NULL, Position INTEGER NOT NULL,
                Title TEXT NOT NULL, Stage TEXT NOT NULL, OffsetDays INTEGER NOT NULL, Owner TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Onboardings (TicketNumber INTEGER PRIMARY KEY, StarterId TEXT NOT NULL, JobTitle TEXT NOT NULL DEFAULT '',
                StartDate TEXT NOT NULL, LineManagerId TEXT NULL, TemplateName TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL, CancelledAt TEXT NULL);
            CREATE TABLE IF NOT EXISTS OnboardingTasks (Id TEXT PRIMARY KEY, TicketNumber INTEGER NOT NULL, Position INTEGER NOT NULL, Title TEXT NOT NULL,
                Stage TEXT NOT NULL, OffsetDays INTEGER NOT NULL, Owner TEXT NOT NULL, TechnicianId TEXT NULL, CompletedAt TEXT NULL, Actor TEXT NULL, ActorId TEXT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadOnboarding(SqliteConnection connection, StoreData data)
    {
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'OnboardingVersion';") as string, out var version)) data.OnboardingVersion = version;
        var templates = new Dictionary<Guid, OnboardingTemplate>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name FROM OnboardingTemplates;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var template = new OnboardingTemplate(Guid.Parse(reader.GetString(0)), reader.GetString(1));
                data.OnboardingTemplates.Add(template);
                templates[template.Id] = template;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TemplateId, Id, Title, Stage, OffsetDays, Owner FROM OnboardingTemplateTasks ORDER BY TemplateId, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (templates.TryGetValue(Guid.Parse(reader.GetString(0)), out var template))
                    template.Tasks.Add(new OnboardingTemplateTask(Guid.Parse(reader.GetString(1)), reader.GetString(2),
                        OnboardingStages.Find(reader.GetString(3)) ?? OnboardingStages.FirstDay, reader.GetInt32(4), OnboardingOwners.Find(reader.GetString(5)) ?? OnboardingOwners.IT));
        }
        var records = new Dictionary<int, OnboardingRecord>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, StarterId, JobTitle, StartDate, LineManagerId, TemplateName, CreatedAt, CancelledAt FROM Onboardings ORDER BY TicketNumber;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var record = new OnboardingRecord(reader.GetInt32(0), Guid.Parse(reader.GetString(1)), NullableDateOnly(reader, 3) ?? DateOnly.FromDateTime(Date(reader, 6)), Date(reader, 6))
                {
                    JobTitle = reader.GetString(2),
                    LineManagerId = NullableGuid(reader, 4),
                    TemplateName = reader.GetString(5),
                    CancelledAt = reader.IsDBNull(7) ? null : Date(reader, 7)
                };
                data.Onboardings.Add(record);
                records[record.TicketNumber] = record;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, Id, Title, Stage, OffsetDays, Owner, TechnicianId, CompletedAt, Actor, ActorId FROM OnboardingTasks ORDER BY TicketNumber, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (records.TryGetValue(reader.GetInt32(0), out var record))
                    record.Tasks.Add(new OnboardingTask(Guid.Parse(reader.GetString(1)), reader.GetString(2), OnboardingStages.Find(reader.GetString(3)) ?? OnboardingStages.FirstDay,
                        reader.GetInt32(4), OnboardingOwners.Find(reader.GetString(5)) ?? OnboardingOwners.IT)
                    {
                        TechnicianId = NullableGuid(reader, 6),
                        CompletedAt = reader.IsDBNull(7) ? null : Date(reader, 7),
                        CompletedBy = reader.IsDBNull(7) ? null : ReadActor(reader, 8, 9)
                    });
        }
    }

    private static void WriteOnboarding(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "OnboardingVersion", data.OnboardingVersion.ToString(CultureInfo.InvariantCulture));
        foreach (var template in data.OnboardingTemplates)
        {
            Execute(connection, transaction, "INSERT INTO OnboardingTemplates (Id, Name) VALUES ($id,$name);", ("$id", template.Id.ToString()), ("$name", template.Name));
            var position = 0;
            foreach (var task in template.Tasks)
                Execute(connection, transaction, "INSERT INTO OnboardingTemplateTasks (Id, TemplateId, Position, Title, Stage, OffsetDays, Owner) VALUES ($id,$template,$position,$title,$stage,$offset,$owner);",
                    ("$id", task.Id.ToString()), ("$template", template.Id.ToString()), ("$position", position++), ("$title", task.Title), ("$stage", task.Stage), ("$offset", task.OffsetDays), ("$owner", task.Owner));
        }
        var ticketNumbers = data.Tickets.Select(x => x.Number).ToHashSet();
        foreach (var record in data.Onboardings.Where(x => ticketNumbers.Contains(x.TicketNumber)))
        {
            Execute(connection, transaction, "INSERT INTO Onboardings (TicketNumber, StarterId, JobTitle, StartDate, LineManagerId, TemplateName, CreatedAt, CancelledAt) VALUES ($number,$starter,$job,$start,$manager,$template,$created,$cancelled);",
                ("$number", record.TicketNumber), ("$starter", record.StarterId.ToString()), ("$job", record.JobTitle), ("$start", IsoDay(record.StartDate)),
                ("$manager", record.LineManagerId?.ToString()), ("$template", record.TemplateName), ("$created", Iso(record.CreatedAt)), ("$cancelled", record.CancelledAt is { } cancelled ? Iso(cancelled) : null));
            var position = 0;
            foreach (var task in record.Tasks)
                Execute(connection, transaction, "INSERT INTO OnboardingTasks (Id, TicketNumber, Position, Title, Stage, OffsetDays, Owner, TechnicianId, CompletedAt, Actor, ActorId) VALUES ($id,$number,$position,$title,$stage,$offset,$owner,$tech,$completed,$actor,$actorid);",
                    ("$id", task.Id.ToString()), ("$number", record.TicketNumber), ("$position", position++), ("$title", task.Title), ("$stage", task.Stage), ("$offset", task.OffsetDays),
                    ("$owner", task.Owner), ("$tech", task.TechnicianId?.ToString()), ("$completed", task.CompletedAt is { } done ? Iso(done) : null),
                    ("$actor", task.CompletedBy?.Name), ("$actorid", task.CompletedBy?.Id?.ToString()));
        }
    }
}
