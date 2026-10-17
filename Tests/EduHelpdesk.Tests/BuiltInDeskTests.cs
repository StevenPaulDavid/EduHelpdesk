using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Tests;

// The two built-in desks (HelpdeskStore.BuiltInDesks): a new install has IT and Facilities, either can be switched off
// and on again with everything it had, and an install from before Facilities was built in is asked once.
public class BuiltInDeskTests
{
    private static Desk Facilities(HelpdeskStore store) => store.BuiltInDesk(DeskKinds.Facilities)!;

    [Fact]
    public void A_new_install_has_IT_and_Facilities_with_its_starter_set()
    {
        using var test = new TestStore(facilities: true);
        var store = test.Store;
        Assert.Equal([DeskKinds.It, DeskKinds.Facilities], store.Desks.Select(x => x.Kind));
        var facilities = Facilities(store);
        Assert.Equal("FAC", facilities.Prefix);
        Assert.Contains("Plumbing & leaks", store.CategoriesFor(facilities.Id));
        Assert.Contains(HelpdeskStore.FacilitiesTeam, store.TeamsFor(facilities.Id));
        Assert.Contains(store.ServiceItems, x => x.Category == "Heating & hot water");
        // Each top bar leaves out what the other desk is for.
        Assert.Contains(DeskAreas.Facilities, store.BuiltInDesk(DeskKinds.It)!.HiddenAreas);
        Assert.Contains(DeskAreas.Kits, facilities.HiddenAreas);
        Assert.False(store.FacilitiesChoicePending);
    }

    [Fact]
    public void Switching_off_sets_everything_aside_and_switching_on_brings_it_back_after_a_restart()
    {
        using var test = new TestStore(facilities: true);
        var id = Facilities(test.Store).Id;
        Assert.True(test.Store.UpdateDesk(id, "Site", "SITE", "Repairs", true).Ok);

        Assert.True(test.Store.SwitchDeskOff(id).Ok);
        Assert.False(test.Store.HasSeveralDesks);
        Assert.DoesNotContain("Plumbing & leaks", test.Store.Categories);
        Assert.DoesNotContain(HelpdeskStore.FacilitiesTeam, test.Store.TechnicianTeams);
        Assert.DoesNotContain(test.Store.ServiceItems, x => x.Category == "Plumbing & leaks");

        var store = test.Reopen();
        Assert.False(store.IsDeskOn(id));
        Assert.True(store.SwitchDeskOn(id).Ok);
        store = test.Reopen();
        var desk = store.Desks.Single(x => x.Id == id);
        Assert.Equal(("Site", "SITE", DeskKinds.Facilities), (desk.Name, desk.Prefix, desk.Kind));
        Assert.Contains("Plumbing & leaks", store.CategoriesFor(id));
        Assert.Contains(HelpdeskStore.FacilitiesTeam, store.TeamsFor(id));
        Assert.Contains(store.ServiceItems, x => x.Category == "Plumbing & leaks");
    }

    [Fact]
    public void A_desk_with_tickets_stays_on_built_in_desks_are_never_deleted_and_one_stays_on()
    {
        using var test = new TestStore(facilities: true);
        var store = test.Store;
        var facilities = Facilities(store);
        var it = store.BuiltInDesk(DeskKinds.It)!;
        Assert.False(store.DeleteDesk(facilities.Id).Ok);

        store.AddTicket(new TicketRecord(0, "Radiator cold", "", test.AddRequester("Priya Shah").Id, [], null, "Normal", "Open", "Heating & hot water", DateTime.UtcNow, null));
        var refused = store.SwitchDeskOff(facilities.Id);
        Assert.False(refused.Ok);
        Assert.Contains("1 ticket", refused.Message);

        // IT has the demo ticket; once Facilities is off, IT is the only desk on and can't go either.
        using var other = new TestStore();
        Assert.False(other.Store.SwitchDeskOff(other.Store.BuiltInDesk(DeskKinds.It)!.Id).Ok);
        Assert.Contains("only one on", other.Store.SwitchDeskOff(other.Store.BuiltInDesk(DeskKinds.It)!.Id).Message);
        Assert.True(store.IsDeskOn(it.Id));
    }

    [Fact]
    public void An_older_install_is_asked_once_and_can_make_its_own_desk_Facilities()
    {
        using var test = new TestStore(facilities: true);
        AsBeforeFacilities(test);
        var store = test.Reopen();
        Assert.True(store.FacilitiesChoicePending);
        Assert.Equal(DeskKinds.It, store.Desks[0].Kind);

        Assert.True(store.AddDesk("Site", "SITE", "").Ok);
        var site = store.Desks.Single(x => x.Name == "Site");
        Assert.True(store.MakeFacilitiesDesk(site.Id).Ok);
        Assert.False(store.FacilitiesChoicePending);
        Assert.Equal(site.Id, store.BuiltInDesk(DeskKinds.Facilities)!.Id);
        Assert.Contains(DeskAreas.Facilities, store.BuiltInDesk(DeskKinds.It)!.HiddenAreas);
        Assert.False(store.MakeFacilitiesDesk(site.Id).Ok);
    }

    [Fact]
    public void Not_now_leaves_Facilities_off_and_switching_it_on_later_starts_the_starter_set()
    {
        using var test = new TestStore(facilities: true);
        AsBeforeFacilities(test);
        var store = test.Reopen();
        Assert.True(store.DeclineFacilitiesDesk().Ok);
        Assert.False(store.FacilitiesChoicePending);
        Assert.False(store.HasSeveralDesks);

        var off = store.AllDesks.Single(x => x.Kind == DeskKinds.Facilities);
        Assert.True(store.SwitchDeskOn(off.Id).Ok);
        Assert.Contains("Heating & hot water", store.CategoriesFor(off.Id));
        Assert.Contains(HelpdeskStore.FacilitiesTeam, store.TeamsFor(off.Id));
    }

    [Fact]
    public void An_older_desk_without_cleaning_rounds_in_its_bar_has_no_Facilities_menu()
    {
        using var test = new TestStore(facilities: true);
        AsBeforeFacilities(test, "UPDATE Desks SET HiddenAreas = 'kits,rounds';");
        var store = test.Reopen();
        Assert.Equal([DeskAreas.Kits, DeskAreas.Facilities], store.Desks[0].HiddenAreas);
    }

    // The database as an install from before the built-in desks had it: one desk, IT, unmarked, and no Facilities.
    private static void AsBeforeFacilities(TestStore test, string extra = "")
    {
        var facilities = Facilities(test.Store);
        Assert.True(test.Store.SwitchDeskOff(facilities.Id).Ok);
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={test.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM ParkedDeskValues; DELETE FROM Desks WHERE IsOff = 1; UPDATE Desks SET Kind = '', HiddenAreas = ''; {extra}";
        command.ExecuteNonQuery();
    }
}
