namespace EduHelpdesk.Tests;

// The service catalogue: items under a ticket category, picked in the staff portal. The portal page itself is exercised
// by hand; these cover the rules that keep the data honest.
public class ServiceCatalogueTests
{
    [Fact]
    public void A_new_system_starts_with_a_catalogue_under_its_own_categories()
    {
        using var test = new TestStore();
        var items = test.Store.ServiceItems;

        Assert.NotEmpty(items);
        Assert.All(items, x => Assert.Contains(x.Category, test.Store.Categories, StringComparer.OrdinalIgnoreCase));
        Assert.All(items.Where(x => x.DefaultPriority.Length > 0), x => Assert.Contains(x.DefaultPriority, test.Store.Priorities));
    }

    [Fact]
    public void Items_are_checked_before_they_are_saved()
    {
        using var test = new TestStore();
        var store = test.Store;
        var before = store.ServiceItems.Count;

        Assert.Equal("Select a valid category.", store.AddServiceItem("Nonsense", "Thing", ""));
        Assert.Equal("Enter a name for the catalogue item.", store.AddServiceItem("Hardware", "   ", ""));
        Assert.Equal("Select a valid priority.", store.AddServiceItem("Hardware", "Thing", "Sooner"));
        Assert.Contains("too long", store.AddServiceItem("Hardware", new string('x', HelpdeskStore.MaxServiceItemNameLength + 1), ""));
        Assert.Equal(before, store.ServiceItems.Count);

        Assert.Equal("Catalogue item added.", store.AddServiceItem("hardware", "  Dead pixel  ", "low"));
        var added = store.ServiceItems.Single(x => x.Name == "Dead pixel");
        Assert.Equal("Hardware", added.Category);
        Assert.Equal("Low", added.DefaultPriority);

        // The same name is fine under another category, not under the same one.
        Assert.Contains("already has", store.AddServiceItem("Hardware", "DEAD PIXEL", ""));
        Assert.Equal("Catalogue item added.", store.AddServiceItem("Software", "Dead pixel", ""));
    }

    [Fact]
    public void FindServiceItem_only_matches_inside_the_category_it_was_posted_with()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.AddServiceItem("Hardware", "Dead pixel", "");

        Assert.NotNull(store.FindServiceItem("Hardware", "dead pixel"));
        Assert.Null(store.FindServiceItem("Software", "Dead pixel"));
        Assert.Null(store.FindServiceItem("Hardware", ""));
        Assert.Null(store.FindServiceItem(null, null));
    }

    [Fact]
    public void Renaming_an_item_follows_through_to_its_tickets_but_deleting_it_leaves_their_wording()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Priya Shah");
        store.AddServiceItem("Hardware", "Dead pixel", "");
        var item = store.ServiceItems.Single(x => x.Name == "Dead pixel");
        var number = store.AddTicket(new TicketRecord(0, "Dead pixel", "", requester.Id, [], null, "Normal", "Open", "Hardware", DateTime.UtcNow, null) { SubCategory = "Dead pixel" });

        Assert.Equal("Catalogue item saved.", store.UpdateServiceItem(item.Id, "Hardware", "Screen fault", "High"));
        Assert.Equal("Screen fault", store.Tickets.Single(x => x.Number == number).SubCategory);

        Assert.Equal("Catalogue item deleted.", store.DeleteServiceItem(item.Id));
        Assert.Equal("Screen fault", store.Tickets.Single(x => x.Number == number).SubCategory);
        Assert.Equal("Catalogue item was not found.", store.DeleteServiceItem(item.Id));
    }

    [Fact]
    public void A_category_with_catalogue_items_cannot_be_deleted_and_a_rename_moves_them()
    {
        using var test = new TestStore();
        var store = test.Store;

        Assert.Contains("service catalogue", store.DeleteTicketOption("Category", "Network"));
        Assert.Contains("Network", store.Categories);

        store.UpdateTicketOption("Category", "Network", "Connectivity");
        Assert.DoesNotContain(store.ServiceItems, x => x.Category == "Network");
        Assert.Contains(store.ServiceItems, x => x.Category == "Connectivity");
    }

    [Fact]
    public void Deleting_a_priority_clears_it_as_a_starting_priority_and_renaming_it_follows()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.AddServiceItem("Hardware", "Dead pixel", "Urgent");
        store.AddServiceItem("Hardware", "Cracked case", "Low");

        store.UpdateTicketOption("Priority", "Low", "Minor");
        Assert.Equal("Minor", store.ServiceItems.Single(x => x.Name == "Cracked case").DefaultPriority);

        store.DeleteTicketOption("Priority", "Urgent");
        Assert.Equal("", store.ServiceItems.Single(x => x.Name == "Dead pixel").DefaultPriority);
    }

    [Fact]
    public void Moving_a_ticket_to_another_category_drops_its_sub_category_but_the_same_category_keeps_it()
    {
        using var test = new TestStore();
        var store = test.Store;
        var ticket = new TicketRecord(0, "Dead pixel", "", Guid.NewGuid(), [], null, "Normal", "Open", "Hardware", DateTime.UtcNow, null) { SubCategory = "Dead pixel" };

        Assert.Equal("Dead pixel", store.WithCategory(ticket, "hardware").SubCategory);
        Assert.Equal("", store.WithCategory(ticket, "Software").SubCategory);
    }

    [Fact]
    public void The_catalogue_and_a_tickets_sub_category_survive_a_restart_and_save_only_the_difference()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Priya Shah");

        var before = store.RecentSaveTimings().Count;
        store.AddServiceItem("Hardware", "Dead pixel", "High");
        var number = store.AddTicket(new TicketRecord(0, "Dead pixel", "", requester.Id, [], null, "High", "Open", "Hardware", DateTime.UtcNow, null) { SubCategory = "Dead pixel" });
        Assert.All(store.RecentSaveTimings().Skip(before), x => Assert.False(x.FullRewrite));
        Assert.Null(store.CompareDatabaseWithMemory());

        var reopened = test.Reopen();
        Assert.Equal("High", reopened.ServiceItems.Single(x => x.Name == "Dead pixel").DefaultPriority);
        Assert.Equal("Dead pixel", reopened.Tickets.Single(x => x.Number == number).SubCategory);
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_database_from_before_the_catalogue_opens_with_blank_sub_categories_and_an_empty_catalogue()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Priya Shah");
        var number = test.AddTicket(requester.Id);
        foreach (var item in test.Store.ServiceItems.ToList()) test.Store.DeleteServiceItem(item.Id);

        var reopened = test.Reopen();
        Assert.Empty(reopened.ServiceItems);
        Assert.Equal("", reopened.Tickets.Single(x => x.Number == number).SubCategory);
    }
}
