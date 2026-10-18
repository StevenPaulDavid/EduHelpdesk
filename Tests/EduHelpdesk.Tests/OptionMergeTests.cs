namespace EduHelpdesk.Tests;

// Moving everything on a list value to another, then taking it off (HelpdeskStore.MergeOption) - what Settings offers
// when the bin is pressed on a value that something still uses.
public class OptionMergeTests
{
    [Fact]
    public void A_category_with_tickets_moves_them_to_another_on_its_desk_and_goes()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Priya Shah").Id;
        store.AddTicketOption("Category", "Printers");
        var number = store.AddTicket(new TicketRecord(0, "Paper jam", "", requester, [], null, "Normal", "Open", "Printers", DateTime.UtcNow, null));
        Assert.Contains("ticket", store.DeleteTicketOption("Category", "Printers"));

        Assert.Equal(["1 ticket"], store.OptionUsage("Categories", "Printers"));
        var (ok, message) = store.MergeOption("Categories", "Printers", "Hardware");
        Assert.True(ok, message);
        Assert.Equal("1 ticket moved to Hardware, and Printers taken off the list.", message);
        Assert.DoesNotContain("Printers", store.Categories);
        Assert.Equal("Hardware", store.Tickets.Single(x => x.Number == number).Category);
    }

    [Fact]
    public void Only_a_category_on_the_same_desk_and_never_Closed_is_offered()
    {
        using var test = new TestStore(facilities: true);
        var store = test.Store;
        var facilities = store.BuiltInDesk(DeskKinds.Facilities)!;
        Assert.DoesNotContain("Plumbing & leaks", store.MergeTargets("Category", "Hardware"));
        Assert.All(store.MergeTargets("Category", "Plumbing & leaks"), x => Assert.Equal(facilities.Id, store.DeskForCategory(x).Id));
        Assert.False(store.MergeOption("Category", "Hardware", "Plumbing & leaks").Ok);
        Assert.DoesNotContain("Closed", store.MergeTargets("Status", "On Hold"));
        Assert.False(HelpdeskStore.CanMergeAway("Status", "Closed"));
    }

    [Fact]
    public void A_room_merges_everywhere_it_is_used_without_doubling_up_on_a_round()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.AddManagedOption("Location", "Room 1");
        store.AddManagedOption("Location", "Room 1A");
        var person = test.AddRequester("Sam Hughes");
        Assert.True(store.SaveCleaningRound(null, "Ground floor", ["Room 1", "Room 1A"], [DayOfWeek.Monday], null, true).Ok);

        Assert.True(store.MergeOption("Locations", "Room 1A", "Room 1").Ok);
        Assert.DoesNotContain("Room 1A", store.Locations);
        Assert.Equal(["Room 1"], store.CleaningRounds.Single().Rooms);
        Assert.Equal("Room 1", store.Users.Single(x => x.Id == person.Id).Location);
    }

    [Fact]
    public void The_asset_type_moved_to_keeps_its_own_lifespan()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.SetAssetTypeLifespan("Laptop", 4);
        store.SetAssetTypeLifespan("Tablet", 3);
        store.AddAsset(new AssetRecord(Guid.NewGuid(), "TAB-1", "Apple", "iPad", "Tablet", "SN", "Room 1", null));
        Assert.True(store.MergeOption("Asset type", "Tablet", "Laptop").Ok);
        Assert.Equal(4, store.AssetTypeLifespans["Laptop"]);
        Assert.False(store.AssetTypeLifespans.ContainsKey("Tablet"));
        Assert.Equal("Laptop", store.Assets.Single(x => x.AssetTag == "TAB-1").Type);
    }
}
