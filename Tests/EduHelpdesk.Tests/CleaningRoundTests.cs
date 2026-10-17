namespace EduHelpdesk.Tests;

// Stage 5 of desks: cleaning rounds - rooms on chosen weekdays, ticked off by the supervisor, with what counts as missed
// and the lists kept in step.
public class CleaningRoundTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly DayOfWeek[] EveryDay = Enum.GetValues<DayOfWeek>();

    private static Guid AddRound(TestStore test, string name = "Ground floor", DateOnly? startsOn = null, params string[] rooms)
    {
        var store = test.Store;
        foreach (var room in rooms.Length == 0 ? ["G1", "G2", "G10"] : rooms)
            if (!store.Locations.Contains(room)) store.AddManagedOption("Locations", room);
        foreach (var cleaner in new[] { "Pat", "Sam" })
            if (!store.Cleaners.Contains(cleaner)) store.AddManagedOption("Cleaners", cleaner);
        var result = store.SaveCleaningRound(null, name, rooms.Length == 0 ? ["G10", "G2", "G1"] : rooms, EveryDay, "pat", true, startsOn ?? Today.AddDays(-6));
        Assert.True(result.Ok, result.Message);
        return store.CleaningRounds.Single(x => x.Name == name).Id;
    }

    [Fact]
    public void A_round_is_checked_and_saved()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.AddManagedOption("Locations", "Hall");
        store.AddManagedOption("Cleaners", "Pat");
        Assert.False(store.SaveCleaningRound(null, " ", ["Hall"], [DayOfWeek.Monday], "", true).Ok);
        Assert.False(store.SaveCleaningRound(null, "Hall", [], [DayOfWeek.Monday], "", true).Ok);
        Assert.False(store.SaveCleaningRound(null, "Hall", ["Hall"], [], "", true).Ok);
        Assert.False(store.SaveCleaningRound(null, "Hall", ["Nowhere"], [DayOfWeek.Monday], "", true).Ok);
        Assert.False(store.SaveCleaningRound(null, "Hall", ["Hall"], [DayOfWeek.Monday], "Nobody", true).Ok);
        Assert.True(store.SaveCleaningRound(null, "Hall", ["hall"], [DayOfWeek.Friday, DayOfWeek.Monday], "pat", true).Ok);
        Assert.False(store.SaveCleaningRound(null, "HALL", ["Hall"], [DayOfWeek.Monday], "", true).Ok);

        var round = store.CleaningRounds.Single();
        // Names as the lists hold them, Monday first, starting today.
        Assert.Equal(["Hall"], round.Rooms);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], round.Days);
        Assert.Equal("Pat", round.Cleaner);
        Assert.Equal(Today, round.StartedOn);
        Assert.Null(store.CompareDatabaseWithMemory());
        var reopened = test.Reopen();
        var back = reopened.CleaningRounds.Single();
        Assert.Equal((round.Id, round.Name, round.Cleaner, round.StartedOn, round.IsActive), (back.Id, back.Name, back.Cleaner, back.StartedOn, back.IsActive));
        Assert.Equal(round.Rooms, back.Rooms);
        Assert.Equal(round.Days, back.Days);
        Assert.Equal(["Pat"], reopened.Cleaners);
    }

    [Fact]
    public void Ticking_off_a_round_records_each_room_and_keeps_unchanged_ones()
    {
        using var test = new TestStore();
        var store = test.Store;
        var id = AddRound(test);
        Assert.Contains(store.RoundsDueOn(Today), x => x.Id == id);

        Assert.False(store.RecordRound(id, Today.AddDays(1), [new("G1", true, "Pat", null, null, null)]).Ok);
        Assert.False(store.RecordRound(id, Today.AddDays(-30), [new("G1", true, "Pat", null, null, null)]).Ok);
        Assert.False(store.RecordRound(id, Today, [new("G1", true, "Pat", null, 6, null)]).Ok);
        Assert.False(store.RecordRound(id, Today, [new("G1", true, "Nobody", null, null, null)]).Ok);

        var result = store.RecordRound(id, Today, [
            new("G1", true, "sam", null, 4, "Bins not emptied"),
            new("G2", false, "Pat", "Room locked", null, null),
            new("G10", false, "Pat", null, null, null),
            new("Not on the round", true, "Pat", null, null, null)]);
        Assert.True(result.Ok, result.Message);
        Assert.Contains("1 of 3", result.Message);
        var visits = store.VisitsFor(id, Today);
        // G10, neither done nor noted, isn't recorded; not done keeps no cleaner.
        Assert.Equal(["G1", "G2"], visits.Select(x => x.Room).Order());
        Assert.Equal("Sam", visits.Single(x => x.Room == "G1").Cleaner);
        Assert.Equal("", visits.Single(x => x.Room == "G2").Cleaner);
        var first = visits.Single(x => x.Room == "G1");

        // Saving again with G1 the same keeps who recorded it and when; G2 now done is new.
        Assert.True(store.RecordRound(id, Today, [new("G1", true, "Sam", "", 4, "Bins not emptied"), new("G2", true, "Pat", "", null, null)]).Ok);
        visits = store.VisitsFor(id, Today);
        Assert.Equal(first, visits.Single(x => x.Room == "G1"));
        Assert.True(visits.Single(x => x.Room == "G2").Done);
        Assert.Equal("Nothing changed.", store.RecordRound(id, Today, [new("G1", true, "Sam", "", 4, "Bins not emptied"), new("G2", true, "Pat", "", null, null)]).Message);

        Assert.Null(store.CompareDatabaseWithMemory());
        var reopened = test.Reopen();
        Assert.Equal(visits.OrderBy(x => x.Room), reopened.VisitsFor(id, Today).OrderBy(x => x.Room));
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Missed_rooms_count_only_on_recorded_days()
    {
        using var test = new TestStore();
        var store = test.Store;
        var id = AddRound(test, startsOn: Today.AddDays(-3));
        store.RecordRound(id, Today.AddDays(-3), [new("G1", true, "Pat", null, 2, null), new("G2", true, "Pat", null, null, null), new("G10", true, "Pat", null, null, null)]);
        store.RecordRound(id, Today.AddDays(-1), [new("G1", true, "Pat", null, 5, null), new("G2", false, "", "Locked", null, null)]);

        var days = store.RoundDays(Today.AddDays(-10), Today.AddDays(5));
        // From the day it was counted to today, never before or after.
        Assert.Equal([Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1), Today], days.Select(x => x.Date));
        Assert.Equal(2, days.Count(x => !x.Recorded));
        Assert.Empty(days[0].Missed);
        Assert.Equal(["G2", "G10"], days[2].Missed);
        // A day with nothing recorded isn't every room missed.
        Assert.Empty(days[1].Missed);

        // Days it isn't done aren't due.
        store.SaveCleaningRound(id, "Ground floor", ["G1", "G2", "G10"], [Today.AddDays(-1).DayOfWeek], "Pat", true);
        Assert.Equal([Today.AddDays(-1)], store.RoundDays(Today.AddDays(-6), Today).Select(x => x.Date));
        Assert.Equal(Today.AddDays(-3), store.GetCleaningRound(id)!.StartedOn);
        // Out of use, it isn't on the day's list, but its record stays.
        store.SaveCleaningRound(id, "Ground floor", ["G1", "G2", "G10"], EveryDay, "Pat", false);
        Assert.DoesNotContain(store.RoundsDueOn(Today), x => x.Id == id);
        Assert.Equal(2, store.VisitsFor(id, Today.AddDays(-1)).Count);
    }

    [Fact]
    public void Renames_follow_and_deletes_are_guarded()
    {
        using var test = new TestStore();
        var store = test.Store;
        var id = AddRound(test);
        store.RecordRound(id, Today, [new("G1", true, "Pat", null, null, null)]);

        store.UpdateManagedOption("Locations", "G1", "G01");
        store.UpdateManagedOption("Cleaners", "Pat", "Patricia");
        var round = store.GetCleaningRound(id)!;
        Assert.Contains("G01", round.Rooms);
        Assert.Equal("Patricia", round.Cleaner);
        Assert.Equal(("G01", "Patricia"), store.VisitsFor(id, Today).Select(x => (x.Room, x.Cleaner)).Single());

        Assert.Contains("cleaning round", store.DeleteManagedOption("Locations", "G01"));
        Assert.Contains("usual cleaner", store.DeleteManagedOption("Cleaners", "Patricia"));
        Assert.Equal("Cleaner deleted.", store.DeleteManagedOption("Cleaners", "Sam"));

        // Deleting the round takes what was recorded on it, in the database too.
        Assert.True(store.DeleteCleaningRound(id).Ok);
        Assert.Empty(store.RoundVisits);
        Assert.Null(store.CompareDatabaseWithMemory());
        var reopened = test.Reopen();
        Assert.Empty(reopened.CleaningRounds);
        Assert.Empty(reopened.RoundVisits);
        Assert.Equal("Location deleted.", reopened.DeleteManagedOption("Locations", "G01"));
    }

    [Fact]
    public void Rounds_and_cleaners_are_audited()
    {
        using var test = new TestStore();
        var id = AddRound(test);
        test.Store.SaveCleaningRound(id, "Ground floor east", ["G1"], [DayOfWeek.Monday], "Sam", true);
        var entries = test.Store.GetAuditEntries();
        Assert.Contains(entries, x => x.Entity == "Cleaner" && x.Action == "Added");
        Assert.Contains(entries, x => x.EntityType == "Cleaning round" && x.Action == "Created");
        Assert.Contains(entries, x => x.EntityType == "Cleaning round" && x.Action == "Updated");
    }
}
