using System.Security.Claims;

namespace EduHelpdesk.Tests;

// The top bar's desk switcher (HelpdeskStore.DeskSwitch): the desk, or desks, the ticket list, assets and daily sheet
// open on, kept against the account.
public class DeskSwitchTests
{
    private static ClaimsPrincipal Principal(TechnicianRecord account) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()), new Claim(ClaimTypes.Name, account.Name), new Claim(ClaimTypes.Role, account.Role)], "Test"));

    // IT and Site, an Administrator on IT only (who sees every desk), and a caretaker on Site only (who doesn't).
    private sealed class Setup : IDisposable
    {
        public TestStore Test { get; } = new();
        public HelpdeskStore Store => Test.Store;
        public Desk It => Store.Desks[0];
        public Desk Site => Store.Desks.Single(x => x.Name == "Site");
        public TechnicianRecord Admin => Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        public TechnicianRecord Caretaker => Store.Technicians.Single(x => x.Name == "Sam Hughes");

        public Setup()
        {
            Assert.True(Store.AddDesk("Site", "SITE", "").Ok);
            Store.AddTechnicianTeam("Caretakers", Site.Id);
            Store.AddTechnician(new TechnicianRecord(Guid.NewGuid(), "Sam Hughes", "sam@test.example", "Caretakers"));
            Assert.Equal([It.Id], Admin.DeskIds);
        }

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void With_one_desk_there_is_no_switcher_and_every_desk_is_worked_on()
    {
        using var test = new TestStore();
        var admin = Principal(test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator));
        Assert.Null(test.Store.SwitcherFor(admin));
        Assert.Empty(test.Store.WorkingDesks(admin));
    }

    [Fact]
    public void Someone_who_sees_one_desk_has_no_switcher_but_works_on_that_desk()
    {
        using var s = new Setup();
        var caretaker = Principal(s.Caretaker);
        Assert.Null(s.Store.SwitcherFor(caretaker));
        Assert.Equal([s.Site.Id], s.Store.WorkingDesks(caretaker).Select(x => x.Id));
        // Choosing a desk they can't see changes nothing.
        Assert.False(s.Store.ChooseWorkingDesk(caretaker, s.It.Name));
        Assert.False(s.Store.ChooseWorkingDesk(caretaker, HelpdeskStore.AllDesksChoice));
        Assert.Equal([s.Site.Id], s.Store.WorkingDesks(caretaker).Select(x => x.Id));
    }

    [Fact]
    public void The_switcher_opens_on_my_desks_and_remembers_a_choice_across_a_restart()
    {
        using var s = new Setup();
        var admin = Principal(s.Admin);

        var switcher = s.Store.SwitcherFor(admin)!;
        Assert.True(switcher.IsMine);
        Assert.True(switcher.OffersMine);
        Assert.True(switcher.OffersAll);
        Assert.Equal("My desks", switcher.Label);
        Assert.Equal([s.It.Id, s.Site.Id], switcher.Choices.Select(x => x.Id));
        Assert.Equal([s.It.Id], s.Store.WorkingDesks(admin).Select(x => x.Id));

        Assert.True(s.Store.ChooseWorkingDesk(admin, s.Site.Id.ToString()));
        Assert.Equal("Site", s.Store.SwitcherFor(admin)!.Label);
        Assert.Equal([s.Site.Id], s.Store.WorkingDesks(admin).Select(x => x.Id));
        // The asset list's blank desk choice follows it.
        Assert.Equal([s.Site.Id], s.Store.AssetDesksShown(admin, "").Select(x => x.Id));

        var store = s.Test.Reopen();
        Assert.Equal([s.Site.Id], store.WorkingDesks(admin).Select(x => x.Id));

        Assert.True(store.ChooseWorkingDesk(admin, HelpdeskStore.AllDesksChoice));
        Assert.True(store.SwitcherFor(admin)!.IsAll);
        Assert.Empty(store.WorkingDesks(admin));

        Assert.True(store.ChooseWorkingDesk(admin, ""));
        Assert.True(store.SwitcherFor(admin)!.IsMine);
        Assert.Equal([s.It.Id], store.WorkingDesks(admin).Select(x => x.Id));
    }

    [Fact]
    public void A_deleted_desk_falls_back_to_my_desks()
    {
        using var s = new Setup();
        var admin = Principal(s.Admin);
        Assert.True(s.Store.AddDesk("Cleaning", "CLEAN", "").Ok);
        var cleaning = s.Store.Desks.Single(x => x.Name == "Cleaning");
        Assert.True(s.Store.ChooseWorkingDesk(admin, "cleaning"));
        Assert.Equal([cleaning.Id], s.Store.WorkingDesks(admin).Select(x => x.Id));

        Assert.True(s.Store.DeleteDesk(cleaning.Id).Ok);

        Assert.True(s.Store.SwitcherFor(admin)!.IsMine);
        Assert.Equal([s.It.Id], s.Store.WorkingDesks(admin).Select(x => x.Id));
    }

    [Fact]
    public void On_every_desk_my_desks_is_every_desk_and_all_desks_is_not_offered()
    {
        using var s = new Setup();
        Assert.True(s.Store.UpdateTechnician(s.Admin with { DeskIds = [s.It.Id, s.Site.Id] }));
        var admin = Principal(s.Admin);

        var switcher = s.Store.SwitcherFor(admin)!;
        Assert.True(switcher.OffersMine);
        Assert.False(switcher.OffersAll);
        Assert.Empty(s.Store.WorkingDesks(admin));
        Assert.False(s.Store.ChooseWorkingDesk(admin, HelpdeskStore.AllDesksChoice));
    }

    [Fact]
    public void A_desk_keeps_the_top_bar_areas_it_uses_across_a_restart()
    {
        using var s = new Setup();
        Assert.All(DeskAreas.All, area => Assert.True(HelpdeskStore.DeskUses(s.Site, area.Key)));

        Assert.True(s.Store.SetDeskAreas(s.Site.Id, [DeskAreas.Assets, DeskAreas.Rounds, "no-such-area"]).Ok);

        var site = s.Test.Reopen().Desks.Single(x => x.Name == "Site");
        Assert.True(HelpdeskStore.DeskUses(site, DeskAreas.Assets));
        Assert.True(HelpdeskStore.DeskUses(site, DeskAreas.Rounds));
        Assert.False(HelpdeskStore.DeskUses(site, DeskAreas.Projects));
        Assert.False(HelpdeskStore.DeskUses(site, DeskAreas.Kits));
        Assert.Null(s.Test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void The_bar_shows_an_area_when_any_desk_being_worked_on_uses_it()
    {
        using var s = new Setup();
        var admin = Principal(s.Admin);
        var caretaker = Principal(s.Caretaker);
        Assert.True(s.Store.SetDeskAreas(s.Site.Id, [DeskAreas.Assets, DeskAreas.Rounds]).Ok);
        Assert.True(s.Store.SetDeskAreas(s.It.Id, DeskAreas.All.Select(x => x.Key).Where(x => x != DeskAreas.Rounds)).Ok);

        // The caretaker works on Site only.
        Assert.False(s.Store.AreaOn(caretaker, DeskAreas.Projects));
        Assert.True(s.Store.AreaOn(caretaker, DeskAreas.Rounds));

        // My desks is IT for the Administrator.
        Assert.True(s.Store.AreaOn(admin, DeskAreas.Projects));
        Assert.False(s.Store.AreaOn(admin, DeskAreas.Rounds));

        Assert.True(s.Store.ChooseWorkingDesk(admin, "Site"));
        Assert.False(s.Store.AreaOn(admin, DeskAreas.Projects));
        Assert.True(s.Store.AreaOn(admin, DeskAreas.Rounds));

        // All desks: what any of them uses.
        Assert.True(s.Store.ChooseWorkingDesk(admin, HelpdeskStore.AllDesksChoice));
        Assert.True(s.Store.AreaOn(admin, DeskAreas.Projects));
        Assert.True(s.Store.AreaOn(admin, DeskAreas.Rounds));
    }

    [Fact]
    public void With_one_desk_every_area_shows_whatever_the_desk_says()
    {
        using var test = new TestStore();
        Assert.True(test.Store.SetDeskAreas(test.Store.Desks[0].Id, []).Ok);
        var admin = Principal(test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator));
        Assert.All(DeskAreas.All, area => Assert.True(test.Store.AreaOn(admin, area.Key)));
    }
}
