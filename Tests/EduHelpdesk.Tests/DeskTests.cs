using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Tests;

// Desks (HelpdeskStore.Desks): IT, Site, Cleaning, each with its own categories, teams, statuses and people. A ticket is
// on its category's desk, so these cover the rules that keep tickets, accounts and the lists consistent with that.
public class DeskTests
{
    private static Desk AddSite(HelpdeskStore store)
    {
        Assert.True(store.AddDesk("Site", "site", "Heating, leaks, doors and furniture").Ok);
        return store.Desks.Single(x => x.Name == "Site");
    }

    private static TechnicianRecord DemoTechnician(HelpdeskStore store) => store.Technicians.First(x => x.Name.Contains("(demo)"));

    [Fact]
    public void A_new_system_has_one_IT_desk_holding_everything()
    {
        using var test = new TestStore();
        var store = test.Store;

        var desk = Assert.Single(store.Desks);
        Assert.Equal("IT", desk.Name);
        Assert.Equal("IT", desk.Prefix);
        Assert.False(store.HasSeveralDesks);
        Assert.All(store.Categories, x => Assert.Equal(desk.Id, store.DeskForCategory(x).Id));
        Assert.All(store.TechnicianTeams, x => Assert.Equal(desk.Id, store.DeskForTeam(x)!.Id));
        Assert.All(store.Technicians, x => Assert.Equal([desk.Id], x.DeskIds));
        var number = test.AddTicket(test.AddRequester("Priya Shah").Id);
        Assert.Equal($"IT-{number}", store.Ref(number));
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void An_upgrading_database_gets_an_IT_desk_with_what_it_already_had()
    {
        using var test = new TestStore();
        var number = test.AddTicket(test.AddRequester("Priya Shah").Id);
        // What a database from before desks looks like: no desk tables at all.
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE Desks; DROP TABLE CategoryDesks; DROP TABLE TeamDesks; DROP TABLE StatusDesks; DROP TABLE TechnicianDesks; UPDATE Tickets SET DeskNumber = 0; DELETE FROM Metadata WHERE Key = 'DeskNumberingVersion';";
            command.ExecuteNonQuery();
        }

        var store = test.Reopen();

        var desk = Assert.Single(store.Desks);
        Assert.Equal("IT", desk.Name);
        Assert.All(store.Categories, x => Assert.Equal(desk.Id, store.DeskForCategory(x).Id));
        Assert.All(store.Technicians, x => Assert.Contains(desk.Id, x.DeskIds));
        Assert.Equal($"IT-{number}", store.Ref(number));
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Desks_are_checked_before_they_are_saved()
    {
        using var test = new TestStore();
        var store = test.Store;

        Assert.False(store.AddDesk("  ", "", "").Ok);
        Assert.False(store.AddDesk("Site", "SI TE", "").Ok);
        Assert.False(store.AddDesk("Site", "TOOLONGX", "").Ok);
        Assert.False(store.AddDesk("it", "", "").Ok);
        Assert.False(store.AddDesk("Site", "it", "").Ok);
        Assert.Single(store.Desks);

        var site = AddSite(store);
        Assert.Equal("SITE", site.Prefix);
        Assert.True(store.HasSeveralDesks);
        // A blank prefix shows the plain number.
        Assert.True(store.AddDesk("Cleaning", "", "").Ok);
        Assert.Equal("#1043", HelpdeskStore.RefFor(store.Desks.Single(x => x.Name == "Cleaning"), 1043));
    }

    [Fact]
    public void Moving_a_ticket_to_another_desk_drops_what_belongs_to_the_old_one()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        store.AddTicketOption("Category", "Heating", site.Id);
        var technician = DemoTechnician(store);
        var number = test.AddTicket(test.AddRequester("Priya Shah").Id, status: "In Progress");
        store.UpdateTicket(store.Tickets.Single(x => x.Number == number) with { TeamName = technician.Team, TechnicianId = technician.Id });

        var (ok, message) = store.ChangeTicketCategory(number, "Heating");

        Assert.True(ok);
        Assert.StartsWith("Moved to the Site desk.", message);
        var ticket = store.Tickets.Single(x => x.Number == number);
        Assert.Equal(site.Id, store.DeskOf(ticket).Id);
        Assert.Null(ticket.TeamName);
        Assert.Null(ticket.TechnicianId);
        // In Progress is used on every desk, so it stays.
        Assert.Equal("In Progress", ticket.Status);
        Assert.Contains(ticket.History, x => x.Action == "Desk changed" && x.Details == "IT -> Site");
        // The Site desk counts its own tickets, so this is its first.
        Assert.Equal("SITE-1", store.Ref(number));
        Assert.Contains($"IT-{number}", ticket.FormerRefs);
    }

    [Fact]
    public void A_desk_only_status_stays_on_its_desk()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        store.AddTicketOption("Category", "Heating", site.Id);
        store.AddTicketOption("Status", "Awaiting contractor", site.Id);
        var requester = test.AddRequester("Priya Shah");
        var itTicket = test.AddTicket(requester.Id);
        var siteTicket = store.AddTicket(new TicketRecord(0, "Radiator cold", "", requester.Id, [], null, "Normal", "Open", "Heating", DateTime.UtcNow, null));

        Assert.DoesNotContain("Awaiting contractor", store.StatusesFor(store.Desks[0].Id));
        Assert.Contains("Awaiting contractor", store.StatusesFor(site.Id));
        var bulk = store.BulkUpdateTickets([itTicket, siteTicket], new("status", "Awaiting contractor"));
        Assert.Equal(1, bulk.Updated);
        Assert.Equal(1, bulk.Skipped);
        Assert.Equal("Open", store.Tickets.Single(x => x.Number == itTicket).Status);

        // Moved back to IT, it can't keep a Site status.
        store.ChangeTicketCategory(siteTicket, "Hardware");
        Assert.Equal("Open", store.Tickets.Single(x => x.Number == siteTicket).Status);

        // Keeping a status to one desk while other desks' open tickets are in it is refused, and Closed is always shared.
        store.UpdateTicket(store.Tickets.Single(x => x.Number == itTicket) with { Status = "On Hold" });
        Assert.False(store.SetStatusDesk("On Hold", site.Id).Ok);
        Assert.False(store.SetStatusDesk("Closed", site.Id).Ok);
        Assert.True(store.SetStatusDesk("Awaiting contractor", null).Ok);
        Assert.Null(store.DeskForStatus("Awaiting contractor"));
    }

    [Fact]
    public void New_tickets_start_in_their_desks_first_status()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        store.AddTicketOption("Category", "Heating", site.Id);
        Assert.Equal("Open", store.StartingStatusFor("Heating"));
        Assert.Equal("Open", store.StartingStatusFor("Hardware"));
    }

    [Fact]
    public void Moving_a_category_takes_its_open_tickets_with_it()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        var technician = DemoTechnician(store);
        var requester = test.AddRequester("Priya Shah");
        var open = store.AddTicket(new TicketRecord(0, "Door sticks", "", requester.Id, [], technician.Id, "Normal", "Open", "Other", DateTime.UtcNow, null));
        var closed = store.AddTicket(new TicketRecord(0, "Door stuck", "", requester.Id, [], technician.Id, "Normal", "Closed", "Other", DateTime.UtcNow, DateTime.UtcNow));

        var (ok, message) = store.SetCategoryDesk("Other", site.Id);

        Assert.True(ok);
        Assert.Contains("1 open ticket", message);
        Assert.Equal(site.Id, store.DeskForCategory("Other").Id);
        Assert.Null(store.Tickets.Single(x => x.Number == open).TechnicianId);
        // Who worked a closed ticket stays on the record.
        Assert.Equal(technician.Id, store.Tickets.Single(x => x.Number == closed).TechnicianId);
        Assert.Contains(store.Tickets.Single(x => x.Number == open).History, x => x.Action == "Desk rules applied");
    }

    [Fact]
    public void Accounts_are_always_on_their_teams_desk_and_keep_their_desks_when_a_form_leaves_them_out()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        store.AddTechnicianTeam("Caretakers", site.Id);

        var caretaker = new TechnicianRecord(Guid.NewGuid(), "Sam Hughes", "sam.hughes@test.example", "Caretakers");
        store.AddTechnician(caretaker);
        Assert.Equal([site.Id], store.Technicians.Single(x => x.Id == caretaker.Id).DeskIds);

        var both = store.Technicians.Single(x => x.Id == caretaker.Id) with { DeskIds = [store.Desks[0].Id, site.Id] };
        store.UpdateTechnician(both);
        // An edit that doesn't mention desks (a password reset, an import) keeps them.
        store.UpdateTechnician(new TechnicianRecord(caretaker.Id, "Sam Hughes", "sam.hughes@test.example", "Caretakers"));
        Assert.Equal([store.Desks[0].Id, site.Id], store.Technicians.Single(x => x.Id == caretaker.Id).DeskIds);
        // Asking for IT alone still keeps the team's desk.
        store.UpdateTechnician(both with { DeskIds = [store.Desks[0].Id] });
        Assert.Contains(site.Id, store.Technicians.Single(x => x.Id == caretaker.Id).DeskIds);
    }

    [Fact]
    public void Only_an_empty_desk_can_be_deleted()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.False(store.DeleteDesk(store.Desks[0].Id).Ok);
        var site = AddSite(store);
        store.AddTicketOption("Category", "Heating", site.Id);
        Assert.False(store.DeleteDesk(site.Id).Ok);
        Assert.True(store.SetCategoryDesk("Heating", store.Desks[0].Id).Ok);
        var technician = DemoTechnician(store);
        store.UpdateTechnician(technician with { DeskIds = [store.Desks[0].Id, site.Id] });

        Assert.True(store.DeleteDesk(site.Id).Ok);
        Assert.Single(store.Desks);
        Assert.DoesNotContain(site.Id, store.Technicians.Single(x => x.Id == technician.Id).DeskIds);
    }

    [Fact]
    public void Desks_and_their_placings_survive_a_restart_and_follow_renames()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        store.AddTicketOption("Category", "Heating", site.Id);
        store.AddTicketOption("Status", "Awaiting contractor", site.Id);
        store.AddTechnicianTeam("Caretakers", site.Id);
        store.SetDeskLook(site.Id, "wrench", "orange");
        Assert.Equal("Category updated.", store.UpdateTicketOption("Category", "Heating", "Heating and hot water"));
        Assert.Equal("Status updated.", store.UpdateTicketOption("Status", "Awaiting contractor", "Waiting for contractor"));
        Assert.Null(store.CompareDatabaseWithMemory());

        store = test.Reopen();

        Assert.Equal(["IT", "Site"], store.Desks.Select(x => x.Name));
        var reloaded = store.Desks[1];
        Assert.Equal(("SITE", "wrench", "orange"), (reloaded.Prefix, reloaded.Icon, reloaded.Color));
        Assert.Equal(reloaded.Id, store.DeskForCategory("Heating and hot water").Id);
        Assert.Equal(reloaded.Id, store.DeskForStatus("Waiting for contractor")?.Id);
        Assert.Equal(reloaded.Id, store.DeskForTeam("Caretakers")?.Id);
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Renaming_a_team_on_the_settings_page_carries_its_people_and_tickets()
    {
        using var test = new TestStore();
        var store = test.Store;
        var technician = DemoTechnician(store);
        var number = test.AddTicket(test.AddRequester("Priya Shah").Id);
        store.UpdateTicket(store.Tickets.Single(x => x.Number == number) with { TeamName = technician.Team });

        // The Teams page goes through the managed-option methods, which used to rename only the list.
        store.UpdateManagedOption("Teams", technician.Team, "IT Helpdesk");

        Assert.Equal("IT Helpdesk", store.Technicians.Single(x => x.Id == technician.Id).Team);
        Assert.Equal("IT Helpdesk", store.Tickets.Single(x => x.Number == number).TeamName);
        Assert.Equal(store.Desks[0].Id, store.DeskForTeam("IT Helpdesk")?.Id);
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    private static int AddTicketIn(HelpdeskStore store, Guid requesterId, string category, string title = "Radiator cold", string status = "Open", DateTime? closedAt = null) =>
        store.AddTicket(new TicketRecord(0, title, "", requesterId, [], null, "Normal", status, category, DateTime.UtcNow, closedAt));

    [Fact]
    public void Each_desk_counts_its_own_tickets()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        store.AddTicketOption("Category", "Heating", site.Id);
        var requester = test.AddRequester("Priya Shah");

        var first = AddTicketIn(store, requester.Id, "Heating");
        var it = test.AddTicket(requester.Id);
        var second = AddTicketIn(store, requester.Id, "Heating");

        Assert.Equal("SITE-1", store.Ref(first));
        Assert.Equal("SITE-2", store.Ref(second));
        // IT goes on from where the shared count was, even though Site's tickets took keys in between.
        Assert.Equal("IT-1003", store.Ref(it));
        Assert.Equal(1004, it);
        Assert.Equal(3, store.Desks.Single(x => x.Id == site.Id).NextNumber);
        Assert.Null(store.CompareDatabaseWithMemory());

        store = test.Reopen();
        Assert.Equal("SITE-2", store.Ref(second));
        Assert.Equal("SITE-3", HelpdeskStore.RefFor(store.Desks[1], store.Desks[1].NextNumber));
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_new_desk_can_start_anywhere_and_its_next_number_only_moves_on()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.AddDesk("Site", "SITE", "", firstNumber: 500).Ok);
        var site = store.Desks.Single(x => x.Name == "Site");
        store.AddTicketOption("Category", "Heating", site.Id);
        var requester = test.AddRequester("Priya Shah");
        var number = AddTicketIn(store, requester.Id, "Heating");
        Assert.Equal("SITE-500", store.Ref(number));

        Assert.False(store.SetDeskNextNumber(site.Id, 500).Ok);
        Assert.False(store.SetDeskNextNumber(site.Id, 0).Ok);
        Assert.True(store.SetDeskNextNumber(site.Id, 900).Ok);
        Assert.Equal("SITE-900", store.Ref(AddTicketIn(store, requester.Id, "Heating")));
        Assert.False(store.AddDesk("Cleaning", "CLN", "", firstNumber: 0).Ok);
    }

    [Fact]
    public void A_moved_ticket_takes_the_new_desks_next_number_and_is_still_found_by_its_old_one()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        store.AddTicketOption("Category", "Heating", site.Id);
        var number = test.AddTicket(test.AddRequester("Priya Shah").Id);
        var before = store.Ref(number);

        store.ChangeTicketCategory(number, "Heating");
        Assert.Equal("SITE-1", store.Ref(number));
        Assert.Contains(store.Tickets.Single(x => x.Number == number).History, x => x.Action == "Number changed" && x.Details == $"{before} -> SITE-1");
        Assert.Equal((number, (string?)null), store.FindTicketByRef(before));

        // Back again: a new IT number, and both old ones are remembered.
        store.ChangeTicketCategory(number, "Hardware");
        var ticket = store.Tickets.Single(x => x.Number == number);
        Assert.NotEqual(before, store.Ref(number));
        Assert.Equal($"{before}, SITE-1", ticket.FormerRefs);
        Assert.Equal(number, store.FindTicketByRef("site-1").Number);
    }

    [Fact]
    public void Moving_a_category_renumbers_all_its_tickets_closed_ones_too()
    {
        using var test = new TestStore();
        var store = test.Store;
        var site = AddSite(store);
        var requester = test.AddRequester("Priya Shah");
        var open = AddTicketIn(store, requester.Id, "Other", "Door sticks");
        var closed = AddTicketIn(store, requester.Id, "Other", "Door stuck", "Closed", DateTime.UtcNow);

        store.SetCategoryDesk("Other", site.Id);

        Assert.Equal("SITE-1", store.Ref(open));
        Assert.Equal("SITE-2", store.Ref(closed));
        Assert.Contains(store.Tickets.Single(x => x.Number == closed).History, x => x.Action == "Number changed");
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Typed_ticket_numbers_are_found_or_explained()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.AddDesk("Site", "SITE", "", firstNumber: 1003).Ok);
        var site = store.Desks.Single(x => x.Name == "Site");
        store.AddTicketOption("Category", "Heating", site.Id);
        var requester = test.AddRequester("Priya Shah");
        var it = test.AddTicket(requester.Id);
        var siteTicket = AddTicketIn(store, requester.Id, "Heating");
        Assert.Equal("IT-1003", store.Ref(it));
        Assert.Equal("SITE-1003", store.Ref(siteTicket));

        Assert.Equal(siteTicket, store.FindTicketByRef("SITE-1003").Number);
        Assert.Equal(siteTicket, store.FindTicketByRef(" site 1003 ").Number);
        Assert.Equal(it, store.FindTicketByRef("it-1003").Number);
        Assert.Equal(1001, store.FindTicketByRef("#1001").Number is { } demo ? store.Tickets.Single(x => x.Number == demo).DeskNumber : 0);
        var (none, ambiguous) = store.FindTicketByRef("1003");
        Assert.Null(none);
        Assert.Contains("IT-1003", ambiguous);
        Assert.Contains("SITE-1003", ambiguous);
        Assert.NotNull(store.FindTicketByRef("CLN-4").Error);
        Assert.NotNull(store.FindTicketByRef("drop table").Error);
        Assert.NotNull(store.FindTicketByRef("").Error);
    }

    [Fact]
    public void Only_one_desk_can_go_without_a_prefix()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.UpdateDesk(store.Desks[0].Id, "IT", "", "", true).Ok);
        Assert.False(store.AddDesk("Site", "", "").Ok);
        Assert.True(store.AddDesk("Site", "SITE", "").Ok);
    }
}
