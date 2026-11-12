namespace EduHelpdesk.Tests;

// The school's own ticket types (HelpdeskStore.TicketTypes, Settings → Ticket types): one desk each, beside the three
// built in, kept through renames, moves, removals and restarts.
public class TicketTypeTests
{
    private static (TestStore Test, Desk It, Desk Site) TwoDesks()
    {
        var test = new TestStore(facilities: true);
        var desks = test.Store.Desks;
        return (test, desks.Single(x => x.Kind == DeskKinds.It), desks.Single(x => x.Kind == DeskKinds.Facilities));
    }

    private static string CategoryOn(TestStore test, Desk desk) => test.Store.CategoriesFor(desk.Id)[0];

    private static int Ticket(TestStore test, string category, string type = TicketTypes.Incident)
    {
        var requester = test.Store.Users.FirstOrDefault() ?? test.AddRequester("Ada Lovelace");
        return test.Store.AddTicket(new TicketRecord(0, "Leaking tap", "", requester.Id, [], null, test.Store.Priorities[0], "Open", category, DateTime.UtcNow, null) { Type = type });
    }

    [Fact]
    public void Each_desk_sees_the_built_in_types_and_its_own()
    {
        var (test, it, site) = TwoDesks();
        using var _ = test;
        Assert.True(test.Store.AddTicketType("Planned", site.Id).Ok);
        Assert.True(test.Store.AddTicketType("Reactive", site.Id).Ok);
        Assert.False(test.Store.AddTicketType("planned", it.Id).Ok);
        Assert.False(test.Store.AddTicketType("Request", it.Id).Ok);
        Assert.False(test.Store.AddTicketType("Project", null).Ok); // which desk?

        Assert.Equal([.. TicketTypes.All, "Planned", "Reactive"], test.Store.TicketTypesFor(site.Id));
        Assert.Equal(TicketTypes.All, test.Store.TicketTypesFor(it.Id));
        Assert.Null(test.Store.TicketTypeProblem("Planned", CategoryOn(test, site)));
        Assert.NotNull(test.Store.TicketTypeProblem("Planned", CategoryOn(test, it)));

        Assert.True(test.Store.MoveTicketType("Reactive", up: true).Ok);
        Assert.Equal(["Reactive", "Planned"], test.Store.CustomTicketTypes.Select(x => x.Name));
    }

    [Fact]
    public void A_type_survives_a_restart_and_a_rename_follows_it_everywhere()
    {
        var (test, _, site) = TwoDesks();
        using var __ = test;
        Assert.True(test.Store.AddTicketType("Planned", site.Id).Ok);
        var number = Ticket(test, CategoryOn(test, site), "Planned");
        Assert.Equal("Planned", test.Store.Tickets.Single(x => x.Number == number).Type);

        var store = test.Reopen();
        Assert.Equal("Planned", store.Tickets.Single(x => x.Number == number).Type);
        Assert.Equal(site.Id, store.CustomTicketTypes.Single().DeskId);

        Assert.True(store.RenameTicketType("Planned", "Planned maintenance").Ok);
        Assert.Equal("Planned maintenance", store.Tickets.Single(x => x.Number == number).Type);
        Assert.Null(store.CompareDatabaseWithMemory());
        Assert.Equal("Planned maintenance", test.Reopen().Tickets.Single(x => x.Number == number).Type);
    }

    [Fact]
    public void Removing_a_type_in_use_moves_its_tickets_first()
    {
        var (test, it, site) = TwoDesks();
        using var _ = test;
        Assert.True(test.Store.AddTicketType("Planned", site.Id).Ok);
        Assert.True(test.Store.AddTicketType("Reactive", site.Id).Ok);
        Assert.True(test.Store.AddTicketType("Project", it.Id).Ok);
        var number = Ticket(test, CategoryOn(test, site), "Planned");

        Assert.False(test.Store.DeleteTicketType("Planned", null).Ok);
        Assert.False(test.Store.DeleteTicketType("Planned", TicketTypes.Disposal).Ok);
        Assert.False(test.Store.DeleteTicketType("Planned", "Project").Ok); // another desk's
        Assert.False(test.Store.DeleteTicketType(TicketTypes.Request, null).Ok);
        Assert.True(test.Store.DeleteTicketType("Planned", "Reactive").Ok);
        var ticket = test.Store.Tickets.Single(x => x.Number == number);
        Assert.Equal("Reactive", ticket.Type);
        Assert.Contains(ticket.History, h => h.Action == "Type changed" && h.Details.StartsWith("Planned -> Reactive"));
        Assert.True(test.Store.DeleteTicketType("Project", null).Ok); // unused
    }

    [Fact]
    public void Moving_a_ticket_to_another_desk_drops_a_type_it_does_not_have()
    {
        var (test, it, site) = TwoDesks();
        using var _ = test;
        Assert.True(test.Store.AddTicketType("Planned", site.Id).Ok);
        var number = Ticket(test, CategoryOn(test, site), "Planned");

        // The type can't move desk while an open ticket elsewhere would be left with it.
        Assert.False(test.Store.SetTicketTypeDesk("Planned", it.Id).Ok);

        var (ok, message) = test.Store.ChangeTicketCategory(number, CategoryOn(test, it));
        Assert.True(ok, message);
        Assert.Contains("type went back to Incident", message);
        Assert.Equal(TicketTypes.Incident, test.Store.Tickets.Single(x => x.Number == number).Type);
        Assert.True(test.Store.SetTicketTypeDesk("Planned", it.Id).Ok);
    }

    [Fact]
    public void Bulk_type_changes_skip_tickets_on_other_desks()
    {
        var (test, it, site) = TwoDesks();
        using var _ = test;
        Assert.True(test.Store.AddTicketType("Planned", site.Id).Ok);
        var onSite = Ticket(test, CategoryOn(test, site));
        var onIt = Ticket(test, CategoryOn(test, it));
        var result = test.Store.BulkUpdateTickets([onSite, onIt], new("type", "planned"));
        Assert.Equal((1, 1), (result.Updated, result.Skipped));
        Assert.Equal("Planned", test.Store.Tickets.Single(x => x.Number == onSite).Type);
        Assert.Equal(TicketTypes.Incident, test.Store.Tickets.Single(x => x.Number == onIt).Type);
    }

    [Fact]
    public void Templates_keep_an_own_type()
    {
        var (test, _, site) = TwoDesks();
        using var __ = test;
        Assert.True(test.Store.AddTicketType("Planned", site.Id).Ok);
        Assert.Contains("Planned", test.Store.AllTicketTypes);
        Assert.Equal("Planned", TicketTypes.Stored("Planned"));
        Assert.Equal(TicketTypes.Request, TicketTypes.Stored("request"));
        Assert.Equal(TicketTypes.Incident, TicketTypes.Stored(" "));
    }
}
