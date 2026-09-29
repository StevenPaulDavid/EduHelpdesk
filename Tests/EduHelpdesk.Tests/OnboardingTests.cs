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

    private static AssetRecord AddLaptop(TestStore test, string tag = "LT-TEST1")
    {
        var laptop = new AssetRecord(Guid.NewGuid(), tag, "Dell", "Latitude 5440", "Laptop", "SN-" + tag, "", null) { Status = "In stock or spare" };
        test.Store.AddAsset(laptop);
        return laptop;
    }

    // Does every task the way it has to be done: devices issued, the portal account made, the rest ticked.
    private static void CompleteAll(TestStore test, int number, bool onlyIt = false)
    {
        foreach (var task in test.Store.FindOnboarding(number)!.Tasks.Where(x => !onlyIt || x.Owner == OnboardingOwners.IT))
        {
            var (ok, message) = task.Action switch
            {
                OnboardingActions.IssueAsset => test.Store.IssueOnboardingAsset(number, task.Id, AddLaptop(test, "LT-" + task.Id.ToString("N")[..6]).Id),
                OnboardingActions.PortalAccount => test.Store.CreateOnboardingPortalAccount(number, task.Id, PasswordHasher.Hash("Temporary-Otter-Maple-12")),
                _ => test.Store.SetOnboardingTaskDone(number, task.Id, true)
            };
            Assert.True(ok, message);
        }
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
        var (number, record) = Start(test, email: "jane.smith@school.example");
        CompleteAll(test, number);

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
    public void The_portal_task_makes_their_account_once_they_have_an_email()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        var portalTask = record.Tasks.Single(x => x.Action == OnboardingActions.PortalAccount);

        // Plain ticking can't stand in for making the account, and there's no account without an email to sign in with.
        Assert.False(test.Store.SetOnboardingTaskDone(number, portalTask.Id, true).Ok);
        Assert.False(test.Store.CreateOnboardingPortalAccount(number, portalTask.Id, PasswordHasher.Hash("Temporary-Otter-Maple-12")).Ok);

        var starter = test.Store.Users.Single(x => x.Id == record.StarterId);
        test.Store.UpdateOnboardingDetails(number, new HelpdeskStore.OnboardingDetails(starter.Name, "jane.smith@school.example", record.JobTitle, null, null, record.StartDate, null));
        Assert.True(test.Store.CreateOnboardingPortalAccount(number, portalTask.Id, PasswordHasher.Hash("Temporary-Otter-Maple-12")).Ok);

        starter = test.Reopen().Users.Single(x => x.Id == record.StarterId);
        Assert.True(PasswordHasher.Verify(starter.PasswordHash, "Temporary-Otter-Maple-12"));
        Assert.True(starter.RequirePasswordChange);
        Assert.True(test.Store.FindOnboarding(number)!.Tasks.Single(x => x.Id == portalTask.Id).IsDone);
        // With an account, the portal task can be unticked and ticked again without a new password.
        Assert.True(test.Store.SetOnboardingTaskDone(number, portalTask.Id, false).Ok);
        Assert.True(test.Store.SetOnboardingTaskDone(number, portalTask.Id, true).Ok);
    }

    [Fact]
    public void The_laptop_task_issues_a_free_laptop_and_taking_it_back_returns_it_to_stock()
    {
        using var test = new TestStore();
        var (number, record) = Start(test);
        var laptopTask = record.Tasks.Single(x => x.Action == OnboardingActions.IssueAsset);
        Assert.Equal("Laptop", laptopTask.AssetType);
        var laptop = AddLaptop(test);
        var someoneElse = test.AddRequester("Other Person");
        var taken = AddLaptop(test, "LT-TAKEN");
        test.Store.UpdateAsset(taken with { AssignedUserId = someoneElse.Id });

        var free = test.Store.AssetsFreeToIssue("Laptop");
        Assert.Contains(free, x => x.Id == laptop.Id);
        Assert.DoesNotContain(free, x => x.Id == taken.Id);
        Assert.False(test.Store.SetOnboardingTaskDone(number, laptopTask.Id, true).Ok);
        Assert.False(test.Store.IssueOnboardingAsset(number, laptopTask.Id, taken.Id).Ok);

        Assert.True(test.Store.IssueOnboardingAsset(number, laptopTask.Id, laptop.Id).Ok);
        var issued = test.Reopen().Assets.Single(x => x.Id == laptop.Id);
        Assert.Equal(record.StarterId, issued.AssignedUserId);
        Assert.Null(issued.LoanDueDate);
        Assert.Contains(laptop.Id, test.Store.Tickets.Single(x => x.Number == number).AssetIds);
        Assert.Equal(laptop.Id, test.Store.FindOnboarding(number)!.Tasks.Single(x => x.Id == laptopTask.Id).AssetId);

        Assert.True(test.Store.ReturnOnboardingAsset(number, laptopTask.Id).Ok);
        var back = test.Store.Assets.Single(x => x.Id == laptop.Id);
        Assert.Null(back.AssignedUserId);
        Assert.Equal("In stock or spare", back.Status);
        Assert.False(test.Store.FindOnboarding(number)!.Tasks.Single(x => x.Id == laptopTask.Id).IsDone);
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
    public void The_ticket_queues_show_onboardings_with_IT_work_and_put_named_tasks_in_My_tickets()
    {
        using var test = new TestStore();
        var (number, record) = Start(test, email: "jane.smith@school.example");
        var jo = test.Store.Technicians.First(x => x.IsActive && x.Role != StaffRoles.Administrator);
        var itTask = record.Tasks.First(x => x.Owner == OnboardingOwners.IT);
        TicketContext Context(Guid? me) => new(test.Store.Users, test.Store.Technicians, test.Store.Assets, test.Store.Priorities, test.Store.Statuses,
            DateTime.UtcNow, 24, me, test.Store.Onboardings.ToDictionary(x => x.TicketNumber));
        bool In(string view, Guid? me) => TicketListQuery.InView(test.Store.Tickets.Single(x => x.Number == number), view, Context(me));

        Assert.True(In("onboarding", null));
        Assert.False(In("mine", jo.Id));
        test.Store.AssignOnboardingTask(number, itTask.Id, jo.Id);
        Assert.True(In("mine", jo.Id));

        // With every IT task done it leaves the Onboarding queue and My tickets, though the officer's tasks remain.
        CompleteAll(test, number, onlyIt: true);
        Assert.False(In("onboarding", null));
        Assert.False(In("mine", jo.Id));
        Assert.True(In("open", null));
    }

    [Fact]
    public void A_new_install_has_the_standard_roles_and_an_onboarding_officer()
    {
        using var test = new TestStore();
        var roles = test.Store.Roles.Select(x => x.Name).ToList();
        Assert.Contains("Technician", roles);
        Assert.Contains("Senior Technician", roles);
        var officer = test.Store.Roles.Single(x => x.Name == HelpdeskStore.OnboardingOfficerRole);
        Assert.Equal(ModulePermission.Access | ModulePermission.View | ModulePermission.New | ModulePermission.Edit | ModulePermission.Delete, officer.GrantsFor(Modules.Onboarding));
        Assert.Equal(ModulePermission.None, officer.GrantsFor(Modules.Tickets));
        // Deleted, it isn't put back on the next start.
        test.Store.DeleteRole(HelpdeskStore.OnboardingOfficerRole);
        Assert.DoesNotContain(test.Reopen().Roles, x => x.Name == HelpdeskStore.OnboardingOfficerRole);
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
