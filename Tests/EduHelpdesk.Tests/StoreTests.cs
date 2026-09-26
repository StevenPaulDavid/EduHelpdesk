namespace EduHelpdesk.Tests;

// The store against a real SQLite database: what a new install starts with, that saves survive a restart, and the
// rules that past bugs broke.
public class StoreTests
{
    [Fact]
    public void A_new_install_has_an_administrator_and_the_starter_roles()
    {
        using var test = new TestStore();
        Assert.Contains(test.Store.Technicians, x => x.Role == StaffRoles.Administrator && x.IsActive);
        Assert.Equal(["Administrator", "Junior Technician", "Senior Technician", "Technician"], test.Store.Roles.Select(x => x.Name).Order());
        Assert.Contains("Closed", test.Store.Statuses);
    }

    [Fact]
    public void Roles_allow_what_they_were_given_and_nothing_else()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.RoleAllows(StaffRoles.Administrator, Modules.Settings, ModulePermission.Edit));
        Assert.True(store.RoleAllows("Junior Technician", Modules.Tickets, ModulePermission.Access));
        Assert.False(store.RoleAllows("Junior Technician", Modules.Settings, ModulePermission.Edit));
        Assert.True(store.RoleAllows("Junior Technician", Modules.Kits, ModulePermission.Edit));
        Assert.False(store.RoleAllows("Junior Technician", Modules.Kits, ModulePermission.Delete));
        Assert.False(store.RoleAllows("Junior Technician", Modules.StaffAccounts, ModulePermission.Access));
        // A role that doesn't exist - a typo, or one deleted while someone was signed in - gets nothing.
        Assert.False(store.RoleAllows("Junior Technicain", Modules.Tickets, ModulePermission.Access));
        Assert.False(store.RoleAllows(null, Modules.Tickets, ModulePermission.Access));
    }

    [Fact]
    public void Changes_survive_a_restart()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Priya Shah");
        var number = test.AddTicket(person.Id, "Printer jammed");
        Assert.True(test.Store.AddTicketComment(number, "Cleared the tray.", isInternal: true));

        var store = test.Reopen();
        var ticket = store.Tickets.Single(x => x.Number == number);
        Assert.Equal("Printer jammed", ticket.Title);
        Assert.Equal(person.Id, ticket.RequesterId);
        Assert.Contains(ticket.Comments, x => x.Text == "Cleared the tray." && x.IsInternal);
        Assert.Contains(store.Users, x => x.Email == "priya.shah@test.example");
    }

    [Fact]
    public void Each_record_gets_back_its_own_history_after_a_restart()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Nina Holt");
        var first = new AssetRecord(Guid.NewGuid(), "TST-10", "Dell", "Latitude 5440", "Laptop", "SN-10", "Room 1", null);
        var second = first with { Id = Guid.NewGuid(), AssetTag = "TST-11", SerialNumber = "SN-11" };
        test.Store.AddAsset(first);
        test.Store.AddAsset(second);
        test.Store.AddAssetComment(first.Id, "Screen replaced.");
        test.Store.AddAssetComment(second.Id, "Keyboard sticky.");
        test.Store.AddAssetComment(first.Id, "Battery replaced.");
        Assert.True(test.Store.LoanAsset(second.Id, person.Id, DateOnly.FromDateTime(DateTime.Today.AddDays(7)), test.Store.LoanReasons[0]).Ok);
        var one = test.AddTicket(person.Id, "One");
        var two = test.AddTicket(person.Id, "Two");
        test.Store.AddTicketComment(two, "Second ticket, first comment.");
        test.Store.AddTicketComment(one, "First ticket, first comment.");
        test.Store.AddTicketComment(two, "Second ticket, second comment.");

        // Everything each record carries, before and after: nothing lost, nothing given to the wrong record, in order.
        string Shape(HelpdeskStore s) => string.Join("\n",
            s.Assets.OrderBy(x => x.AssetTag).Select(a => $"{a.AssetTag}: {string.Join("|", a.Comments.Select(c => c.Text))} / {string.Join("|", a.History.Select(h => h.Action + h.Details))} / {string.Join("|", a.Assignments.Select(x => $"{x.UserId}{x.StartedAt:O}{x.EndedAt:O}{x.Reason}"))}")
            .Concat(s.Tickets.OrderBy(x => x.Number).Select(t => $"#{t.Number}: {string.Join("|", t.Comments.Select(c => c.Text + c.IsInternal))} / {string.Join("|", t.History.Select(h => h.Action + h.Details))}")));
        var before = Shape(test.Store);

        var store = test.Reopen();
        Assert.Equal(before, Shape(store));
        Assert.Equal(["Screen replaced.", "Battery replaced."], store.Assets.Single(x => x.Id == first.Id).Comments.Select(x => x.Text));
        Assert.Equal(["Keyboard sticky."], store.Assets.Single(x => x.Id == second.Id).Comments.Select(x => x.Text));
        Assert.Equal(person.Id, store.Assets.Single(x => x.Id == second.Id).Assignments.Single().UserId);
        Assert.Empty(store.Assets.Single(x => x.Id == first.Id).Assignments);
        Assert.NotEmpty(store.Assets.Single(x => x.Id == second.Id).History);
        Assert.Equal(["Second ticket, first comment.", "Second ticket, second comment."], store.Tickets.Single(x => x.Number == two).Comments.Select(x => x.Text));
        Assert.Equal(["First ticket, first comment."], store.Tickets.Single(x => x.Number == one).Comments.Select(x => x.Text));
    }

    [Fact]
    public void Ticket_numbers_are_never_reused_even_after_deleting_the_newest_and_restarting()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Sam Carter");
        var newest = test.AddTicket(person.Id);
        Assert.Null(test.Store.DeleteTicket(newest));

        test.Reopen();
        Assert.Equal(newest + 1, test.AddTicket(person.Id));
    }

    [Fact]
    public void The_Closed_status_cannot_be_renamed_or_deleted()
    {
        using var test = new TestStore();
        test.Store.UpdateTicketOption("Status", "Closed", "Resolved");
        test.Store.DeleteTicketOption("Status", "Closed");
        Assert.Contains("Closed", test.Reopen().Statuses);
        Assert.DoesNotContain("Resolved", test.Store.Statuses);
    }

    [Fact]
    public void A_deleted_default_option_stays_deleted_after_a_restart()
    {
        using var test = new TestStore();
        var category = test.Store.Categories.First(c => !test.Store.Tickets.Any(t => t.Category == c) && !test.Store.Slas.Any(s => s.Categories.Contains(c)));
        test.Store.DeleteTicketOption("Category", category);
        Assert.DoesNotContain(category, test.Store.Categories);
        Assert.DoesNotContain(category, test.Reopen().Categories);
    }

    [Fact]
    public void Every_save_writes_an_audit_line_naming_what_changed()
    {
        using var test = new TestStore();
        test.AddRequester("Leah Moss");
        Assert.Contains(test.Store.GetAuditEntries(), x => x.Area == "Users" && x.Entity == "Leah Moss");
    }

    [Fact]
    public void Merging_closes_the_source_and_copies_its_comments()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Tom Reid");
        var source = test.AddTicket(person.Id, "Screen dead");
        var target = test.AddTicket(person.Id, "Screen dead again");
        test.Store.AddTicketComment(source, "Checked the cable.");

        Assert.Null(test.Store.MergeTicket(source, target));
        var tickets = test.Reopen().Tickets;
        Assert.Equal("Closed", tickets.Single(x => x.Number == source).Status);
        Assert.Contains(tickets.Single(x => x.Number == target).Comments, x => x.Text.Contains("Checked the cable."));
    }
}
