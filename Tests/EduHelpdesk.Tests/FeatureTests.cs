using System.Security.Claims;
using EduHelpdesk.Pages;

namespace EduHelpdesk.Tests;

// Switching parts of the helpdesk off (Services/Features.cs, Settings → Features): what belongs to a switched-off
// feature is refused to everyone, Administrators included, and comes back as it was when it is switched on.
public class FeatureTests
{
    private static ClaimsPrincipal Admin(HelpdeskStore store)
    {
        var account = store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()), new Claim(ClaimTypes.Role, account.Role)], "Test"));
    }

    [Fact]
    public void A_switched_off_feature_is_refused_even_to_an_administrator_and_comes_back_after_a_restart()
    {
        using var test = new TestStore();
        var admin = Admin(test.Store);
        Assert.True(test.Store.SetFeature(Features.Assets, false).Ok);
        Assert.False(test.Store.UserCan(admin, Modules.Assets, ModulePermission.Access));
        Assert.False(test.Store.UserHasFlag(admin, Modules.Flags.ReportAssets));
        Assert.True(test.Store.UserCan(admin, Modules.Tickets, ModulePermission.Access));

        var store = test.Reopen();
        Assert.False(store.IsFeatureOn(Features.Assets));
        Assert.True(store.SetFeature(Features.Assets, true).Ok);
        Assert.True(store.UserCan(Admin(store), Modules.Assets, ModulePermission.Access));
    }

    [Fact]
    public void Loans_go_off_with_assets_and_bring_them_back_on()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.Contains("Kits and loans", store.SetFeature(Features.Assets, false).Message);
        Assert.False(store.IsFeatureOn(Features.Loans));
        Assert.True(store.SetFeature(Features.Loans, true).Ok);
        Assert.True(store.IsFeatureOn(Features.Assets));
    }

    [Fact]
    public void A_switched_off_features_addresses_and_settings_are_found()
    {
        var off = new HashSet<string> { Features.Assets };
        Assert.Equal(Features.Assets, Features.ForPath("/Reports", off)?.Key);
        Assert.Equal(Features.Assets, Features.ForPath("/Asset", off)?.Key);
        Assert.Equal(Features.Assets, Features.ForPath("/Assets/Import", off)?.Key);
        Assert.Null(Features.ForPath("/Reports/Tickets", off));
        Assert.Null(Features.ForPath("/AssetsElsewhere", off));

        using var test = new TestStore();
        test.Store.SetFeature(Features.Assets, false);
        bool Shown(string page) => SettingsMenu.Shown(test.Store, SettingsMenu.Sections.SelectMany(x => x.Items).Single(x => x.Page == page));
        Assert.False(Shown("/Settings/AssetTypes"));
        // Parts are still on, so their rules and lists stay.
        Assert.True(Shown("/Settings/InventoryRules"));
        Assert.Equal(["PartCategories", "PartLocations"], SettingsMenu.ListsShown(test.Store, "assets").Select(x => x.Kind));
        test.Store.SetFeature(Features.Parts, false);
        Assert.False(Shown("/Settings/InventoryRules"));
        Assert.False(Shown(SettingsMenu.ListPage("assets")));
    }

    [Fact]
    public void A_leaver_isnt_held_up_by_what_a_switched_off_feature_holds()
    {
        using var test = new TestStore();
        var store = test.Store;
        var person = test.AddRequester("Priya Shah");
        store.AddAsset(new AssetRecord(Guid.NewGuid(), "LAP-001", "Dell", "Latitude", "Laptop", "SN1", "Room 1", person.Id));
        Assert.Single(store.GetHoldings(person.Id)!.Assets);
        store.SetFeature(Features.Assets, false);
        Assert.Empty(store.GetHoldings(person.Id)!.Assets);
    }

    [Fact]
    public void With_checks_off_a_schedule_logs_a_plain_ticket_that_can_be_closed()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Sam Hughes");
        var input = new HelpdeskStore.RecurringTicketInput("Fire alarm test", true, "Fire alarm test", "", TicketTypes.Request, store.Categories[0], store.Priorities[0],
            requester.Id, null, null, null, null, Recurrence.Weekly, 1, DateOnly.FromDateTime(DateTime.Today), null, new TimeOnly(7, 0), [DayOfWeek.Monday], 1, 0, DayOfWeek.Monday, 9, false);
        Assert.True(store.SaveRecurringTicket(null, input, DateTime.Now).Ok);
        var schedule = store.RecurringTickets.Single();
        Assert.True(store.SaveCheckItem(schedule.Id, null, new(CheckKinds.PassFail, "Call point tested", "", "", "", "")).Ok);

        var withChecks = store.LogRecurringTicketNow(schedule.Id).Number!.Value;
        Assert.Equal(1, store.UnansweredChecks(withChecks));
        store.SetFeature(Features.Checks, false);
        Assert.Equal(0, store.UnansweredChecks(withChecks));
        var plain = store.LogRecurringTicketNow(schedule.Id).Number!.Value;
        Assert.Empty(store.ChecklistFor(plain));
    }
}
