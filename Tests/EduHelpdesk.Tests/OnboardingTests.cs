namespace EduHelpdesk.Tests;

// Onboarding new staff (Services/HelpdeskStore.Onboarding.cs): one ticket per new starter, carrying its checklist.
public class OnboardingTests
{
    private static (int Number, OnboardingRecord Record) Start(TestStore test, string name = "Jane Smith", int startsInDays = 14, string? email = null)
    {
        var template = test.Store.OnboardingTemplates.Single(x => x.Name == "Teacher");
        var details = new HelpdeskStore.OnboardingDetails(name, email, "Teacher of Science", null, null, DateOnly.FromDateTime(DateTime.Today).AddDays(startsInDays), null);
        var (ok, message, number) = test.Store.StartOnboarding(details, template.Id, null);
        Assert.True(ok, message);
        return (number, test.Store.FindOnboarding(number)!);
    }

    [Fact]
    public void Starting_one_adds_the_starter_and_a_ticket_with_the_templates_checklist()
    {
        using var test = new TestStore();
        var template = test.Store.OnboardingTemplates.Single(x => x.Name == "Teacher");
        var (number, record) = Start(test);

        var starter = test.Store.Users.Single(x => x.Id == record.StarterId);
        Assert.Equal("Jane Smith", starter.Name);
        Assert.True(starter.IsActive);
        Assert.Null(starter.PasswordHash);
        Assert.Equal(template.Tasks.Count, record.Tasks.Count);

        var ticket = test.Store.Tickets.Single(x => x.Number == number);
        Assert.Equal(starter.Id, ticket.RequesterId);
        Assert.Equal(HelpdeskStore.OnboardingCategory, ticket.Category);
        Assert.StartsWith("Onboarding: Jane Smith", ticket.Title);
        // Due at the end of the day the earliest task is due.
        var earliest = record.Tasks.Min(x => record.DueOn(x));
        Assert.Equal(earliest, DateOnly.FromDateTime(ticket.DueDate!.Value.ToLocalTime()));
        Assert.True(test.Store.IsOnboardingTicket(number));
    }

    [Fact]
    public void The_ticket_closes_when_every_task_is_done_and_reopens_when_one_is_unticked()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        foreach (var task in record.Tasks)
            Assert.True(test.Store.SetOnboardingTaskDone(number, task.Id, true).Ok);

        var ticket = test.Reopen().Tickets.Single(x => x.Number == number);
        Assert.True(TicketInsights.IsClosed(ticket));
        Assert.Null(ticket.DueDate);
        Assert.Contains(ticket.History, x => x.Action == "Onboarding complete");
        Assert.All(test.Store.FindOnboarding(number)!.Tasks, x => Assert.NotNull(x.CompletedAt));

        Assert.True(test.Store.SetOnboardingTaskDone(number, record.Tasks[0].Id, false).Ok);
        ticket = test.Store.Tickets.Single(x => x.Number == number);
        Assert.False(TicketInsights.IsClosed(ticket));
        Assert.NotNull(ticket.DueDate);
    }

    [Fact]
    public void Moving_the_start_date_moves_every_due_date()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        var before = record.Tasks.Select(record.DueOn).ToList();
        var starter = test.Store.Users.Single(x => x.Id == record.StarterId);
        var moved = record.StartDate.AddDays(7);
        Assert.True(test.Store.UpdateOnboardingDetails(number, new HelpdeskStore.OnboardingDetails(starter.Name, "jane.smith@school.example", record.JobTitle, null, null, moved, null)).Ok);

        var after = test.Store.FindOnboarding(number)!;
        Assert.Equal(before.Select(x => x.AddDays(7)), after.Tasks.Select(after.DueOn));
        Assert.Equal("jane.smith@school.example", test.Store.Users.Single(x => x.Id == record.StarterId).Email);
    }

    [Fact]
    public void Cancelling_closes_it_and_stops_changes_until_resumed()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        Assert.True(test.Store.CancelOnboarding(number, "Took another job").Ok);
        Assert.True(TicketInsights.IsClosed(test.Store.Tickets.Single(x => x.Number == number)));
        Assert.False(test.Store.SetOnboardingTaskDone(number, record.Tasks[0].Id, true).Ok);

        Assert.True(test.Store.ResumeOnboarding(number).Ok);
        Assert.False(TicketInsights.IsClosed(test.Store.Tickets.Single(x => x.Number == number)));
        Assert.True(test.Store.SetOnboardingTaskDone(number, record.Tasks[0].Id, true).Ok);
    }

    [Fact]
    public void Onboarding_tickets_stay_out_of_the_portal_merges_and_bulk_passwords()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        var other = test.AddTicket(record.StarterId);

        Assert.Null(test.Store.PortalTicket(record.StarterId, number));
        Assert.DoesNotContain(test.Store.PortalTickets(record.StarterId), x => x.Number == number);
        Assert.Contains(test.Store.PortalTickets(record.StarterId), x => x.Number == other);
        Assert.NotNull(test.Store.MergeTicket(number, other));
        Assert.NotNull(test.Store.MergeTicket(other, number));
        Assert.DoesNotContain(test.Store.RequestersWithoutPassword(), x => x.Id == record.StarterId);
    }

    [Fact]
    public void Deleting_the_ticket_removes_the_onboarding_but_keeps_the_person()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        Assert.Null(test.Store.DeleteTicket(number));
        Assert.Null(test.Reopen().FindOnboarding(number));
        Assert.Contains(test.Store.Users, x => x.Id == record.StarterId);
    }

    [Fact]
    public void Tasks_can_be_added_removed_and_given_to_a_technician()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        var technician = test.Store.Technicians.First(x => x.IsActive);
        var itTask = record.Tasks.First(x => x.Owner == OnboardingOwners.IT);
        var officerTask = record.Tasks.First(x => x.Owner == OnboardingOwners.Officer);

        Assert.True(test.Store.AssignOnboardingTask(number, itTask.Id, technician.Id).Ok);
        Assert.False(test.Store.AssignOnboardingTask(number, officerTask.Id, technician.Id).Ok);
        Assert.True(test.Store.AddOnboardingTask(number, "Order a lanyard", OnboardingStages.BeforeArrival, -2, OnboardingOwners.Officer).Ok);
        Assert.False(test.Store.AddOnboardingTask(number, "", OnboardingStages.BeforeArrival, -2, OnboardingOwners.Officer).Ok);
        Assert.True(test.Store.RemoveOnboardingTask(number, officerTask.Id).Ok);

        var saved = test.Reopen().FindOnboarding(number)!;
        Assert.Equal(technician.Id, saved.Tasks.Single(x => x.Id == itTask.Id).TechnicianId);
        Assert.Contains(saved.Tasks, x => x.Title == "Order a lanyard");
        Assert.DoesNotContain(saved.Tasks, x => x.Id == officerTask.Id);
    }

    [Fact]
    public void Templates_are_validated_and_copies_are_independent()
    {
        using var test = new TestStore();
        var teacher = test.Store.OnboardingTemplates.Single(x => x.Name == "Teacher");
        Assert.False(test.Store.AddOnboardingTemplate("teacher", null).Ok);
        var (ok, _, id) = test.Store.AddOnboardingTemplate("Support staff", teacher.Id);
        Assert.True(ok);
        Assert.True(test.Store.DeleteOnboardingTemplateTask(id!.Value, test.Store.OnboardingTemplates.Single(x => x.Id == id).Tasks[0].Id).Ok);
        Assert.False(test.Store.AddOnboardingTemplateTask(id.Value, "Too far ahead", OnboardingStages.FirstWeek, 400, OnboardingOwners.IT).Ok);

        var templates = test.Reopen().OnboardingTemplates;
        Assert.Equal(teacher.Tasks.Count, templates.Single(x => x.Name == "Teacher").Tasks.Count);
        Assert.Equal(teacher.Tasks.Count - 1, templates.Single(x => x.Name == "Support staff").Tasks.Count);
    }
}
