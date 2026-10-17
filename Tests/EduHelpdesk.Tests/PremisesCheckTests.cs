namespace EduHelpdesk.Tests;

// Stage 4 of desks: checklists with readings on recurring tickets, what happens when they fail, and plant belonging to
// a desk.
public class PremisesCheckTests
{
    private static Guid AddSchedule(TestStore test, string name = "Monthly water temperatures")
    {
        var requester = test.AddRequester("Site Office");
        var input = new HelpdeskStore.RecurringTicketInput(name, true, name, "", TicketTypes.Request, test.Store.Categories[0], test.Store.Priorities[0],
            requester.Id, null, null, null, null, Recurrence.Monthly, 1, new DateOnly(2026, 9, 1), null, new TimeOnly(7, 0), [], 1, 0, DayOfWeek.Monday, 9, false);
        Assert.True(test.Store.SaveRecurringTicket(null, input, DateTime.Now).Ok);
        return test.Store.RecurringTickets.Single(x => x.Name == name).Id;
    }

    private static void AddExtinguishers(TestStore test, params string[] tags)
    {
        test.Store.AddManagedOption("AssetTypes", "Fire extinguisher");
        foreach (var tag in tags)
            test.Store.AddAsset(new AssetRecord(Guid.NewGuid(), tag, "Chubb", "CO2", "Fire extinguisher", "", "Main Building", null));
    }

    [Fact]
    public void A_checklist_is_checked_and_copied_onto_each_ticket_it_logs()
    {
        using var test = new TestStore();
        var store = test.Store;
        var id = AddSchedule(test);
        AddExtinguishers(test, "FE-2", "FE-1");

        Assert.False(store.SaveCheckItem(id, null, new(CheckKinds.Reading, "Hot water", "", "", "°C", "")).Ok);
        Assert.False(store.SaveCheckItem(id, null, new(CheckKinds.Reading, "Hot water", "60", "50", "°C", "")).Ok);
        Assert.False(store.SaveCheckItem(id, null, new(CheckKinds.Assets, "", "", "", "", "Nonsense")).Ok);
        Assert.False(store.SaveCheckItem(id, null, new(CheckKinds.PassFail, "  ", "", "", "", "")).Ok);
        Assert.True(store.SaveCheckItem(id, null, new(CheckKinds.Reading, "Hot water, hall kitchen", "50", "", "°C", "")).Ok);
        Assert.True(store.SaveCheckItem(id, null, new(CheckKinds.Assets, "", "", "", "", "fire extinguisher")).Ok);
        Assert.True(store.SaveCheckItem(id, null, new(CheckKinds.Note, "Outlets flushed", "", "", "", "")).Ok);

        var (ok, _, number) = store.LogRecurringTicketNow(id);
        Assert.True(ok);
        var lines = store.ChecklistFor(number!.Value);
        Assert.Equal(["Hot water, hall kitchen", "Fire extinguisher: FE-1 (Main Building)", "Fire extinguisher: FE-2 (Main Building)", "Outlets flushed"], lines.Select(x => x.Label));
        Assert.Equal(3, store.UnansweredChecks(number.Value));

        // Changing the checklist later doesn't change what that ticket asks for.
        store.DeleteCheckItem(id, store.GetRecurringTicket(id)!.Checklist[0].Id);
        Assert.Equal(4, store.ChecklistFor(number.Value).Count);
        Assert.Null(store.CompareDatabaseWithMemory());
        var reopened = test.Reopen();
        Assert.Equal(2, reopened.GetRecurringTicket(id)!.Checklist.Count);
        Assert.Equal(4, reopened.ChecklistFor(number.Value).Count);
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_check_is_closed_only_when_done_and_a_failure_raises_one_follow_up()
    {
        using var test = new TestStore();
        var store = test.Store;
        var id = AddSchedule(test);
        store.SaveCheckItem(id, null, new(CheckKinds.Reading, "Cold water, hall kitchen", "", "20", "°C", ""));
        store.SaveCheckItem(id, null, new(CheckKinds.PassFail, "Call point 3 sounded", "", "", "", ""));
        var number = store.LogRecurringTicketNow(id).Number!.Value;
        var lines = store.ChecklistFor(number);

        // Can't be closed yet, by the page or in bulk.
        Assert.NotNull(store.CloseBlockedByChecklist(number));
        var bulk = store.BulkUpdateTickets([number], new("close"));
        Assert.Equal(1, bulk.Skipped);

        // A reading's result follows its limits, whatever is ticked.
        Assert.False(store.SaveChecklist(number, new Dictionary<Guid, HelpdeskStore.CheckAnswer> { [lines[0].Id] = new("pass", "warm", null) }).Ok);
        Assert.True(store.SaveChecklist(number, new Dictionary<Guid, HelpdeskStore.CheckAnswer>
        {
            [lines[0].Id] = new("pass", "23.5", "Tap ran warm"),
            [lines[1].Id] = new("pass", null, null)
        }).Ok);
        lines = store.ChecklistFor(number);
        Assert.True(lines[0].Failed);
        Assert.Equal(23.5m, lines[0].Reading);
        Assert.Null(store.CloseBlockedByChecklist(number));

        // Closing raises one follow-up, linked, listing the failure; closing again after a reopen doesn't raise another.
        var before = store.Tickets.Count;
        store.UpdateTicket(store.Tickets.Single(x => x.Number == number) with { Status = "Closed", ClosedAt = DateTime.UtcNow });
        Assert.Equal(before + 1, store.Tickets.Count);
        var followUp = store.Tickets.First(x => x.Title == "Failed check: Monthly water temperatures");
        Assert.Contains("Cold water, hall kitchen: 23.5 °C (should be 20 °C or less) - Tap ran warm", followUp.Description);
        Assert.Contains(store.GetTicketRelations(number), x => x.Number == followUp.Number && x.Label == "Follow-up");
        store.UpdateTicket(store.Tickets.Single(x => x.Number == number) with { Status = "Open", ClosedAt = null });
        store.UpdateTicket(store.Tickets.Single(x => x.Number == number) with { Status = "Closed", ClosedAt = DateTime.UtcNow });
        Assert.Equal(before + 1, store.Tickets.Count);

        // Closed, it can't be changed, and it can't be merged away.
        Assert.False(store.SaveChecklist(number, new Dictionary<Guid, HelpdeskStore.CheckAnswer> { [lines[1].Id] = new("fail", null, null) }).Ok);
        Assert.NotNull(store.MergeTicket(number, followUp.Number));
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_check_that_all_passes_closes_quietly()
    {
        using var test = new TestStore();
        var store = test.Store;
        var id = AddSchedule(test, "Weekly fire alarm test");
        store.SaveCheckItem(id, null, new(CheckKinds.PassFail, "Call point 1", "", "", "", ""));
        var number = store.LogRecurringTicketNow(id).Number!.Value;
        var line = store.ChecklistFor(number)[0];
        store.SaveChecklist(number, new Dictionary<Guid, HelpdeskStore.CheckAnswer> { [line.Id] = new("pass", null, null) });
        var before = store.Tickets.Count;
        Assert.Equal(1, store.BulkUpdateTickets([number], new("close")).Updated);
        Assert.Equal(before, store.Tickets.Count);
    }

    [Fact]
    public void Plant_belongs_to_a_desk_and_its_checklists_follow_renames()
    {
        using var test = new TestStore();
        var store = test.Store;
        AddExtinguishers(test, "FE-1");
        Assert.Equal(store.Desks[0].Id, store.DeskForAssetType("Fire extinguisher").Id);
        Assert.True(store.AddDesk("Site", "SITE", "").Ok);
        var site = store.Desks.Single(x => x.Name == "Site");
        Assert.True(store.SetAssetTypeDesk("Fire extinguisher", site.Id).Ok);
        Assert.Equal(site.Id, store.AssetDeskLookup()("fire extinguisher").Id);

        var id = AddSchedule(test, "Extinguisher check");
        store.SaveCheckItem(id, null, new(CheckKinds.Assets, "", "", "", "", "Fire extinguisher"));
        store.UpdateManagedOption("AssetTypes", "Fire extinguisher", "Extinguisher");
        Assert.Equal("Extinguisher", store.GetRecurringTicket(id)!.Checklist[0].AssetType);
        Assert.Equal(site.Id, store.DeskForAssetType("Extinguisher").Id);
        store.DeleteAsset(store.Assets.Single(x => x.AssetTag == "FE-1").Id);
        Assert.Contains("checklist", store.DeleteManagedOption("AssetTypes", "Extinguisher"));

        var reopened = test.Reopen();
        Assert.Equal(site.Id, reopened.DeskForAssetType("Extinguisher").Id);
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }
}
