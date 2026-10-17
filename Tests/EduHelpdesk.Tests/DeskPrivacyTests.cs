using System.Security.Claims;

namespace EduHelpdesk.Tests;

// Stage 2 of desks: each desk's tickets are seen only by people on that desk (or whose role has "See all desks"), and
// pings, the bell, Teams posts and the EduNotify technician ping go only to the ticket's desk.
public class DeskPrivacyTests
{
    private static ClaimsPrincipal Principal(Guid id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "Test"), new Claim(ClaimTypes.Role, role)], "Test"));

    private static readonly EduNotify.Catalogue PingCatalogue = new(DateTime.UtcNow,
        [new("4", "Technician ping", "editable", false, true, true, "New ticket", "")],
        [new("1", "Site office"), new("9", "Technicians")]);

    // A store with a Site desk, a Heating category on it, a caretaker on Site only, and one ticket on each desk.
    private sealed class Setup : IDisposable
    {
        public TestStore Test { get; } = new();
        public HelpdeskStore Store => Test.Store;
        public Desk It => Store.Desks[0];
        public Desk Site { get; }
        public TechnicianRecord Caretaker { get; }
        public int ItTicket { get; }
        public int SiteTicket { get; }

        public Setup()
        {
            Assert.True(Store.AddDesk("Site", "SITE", "").Ok);
            Site = Store.Desks.Single(x => x.Name == "Site");
            Store.AddTicketOption("Category", "Heating", Site.Id);
            Store.AddTechnicianTeam("Caretakers", Site.Id);
            Store.AddTechnician(new TechnicianRecord(Guid.NewGuid(), "Sam Hughes", "sam@test.example", "Caretakers"));
            Caretaker = Store.Technicians.Single(x => x.Name == "Sam Hughes");
            var requester = Test.AddRequester("Priya Shah");
            ItTicket = Test.AddTicket(requester.Id);
            SiteTicket = Store.AddTicket(new TicketRecord(0, "Radiator cold", "", requester.Id, [], null, "Urgent", "Open", "Heating", DateTime.UtcNow, null));
        }

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void People_see_only_their_own_desks_tickets_unless_their_role_sees_all()
    {
        using var s = new Setup();
        var caretaker = Principal(s.Caretaker.Id, s.Caretaker.Role);
        var itTech = s.Store.Technicians.First(x => x.Name.Contains("(demo)"));
        var technician = Principal(itTech.Id, itTech.Role);

        Assert.False(s.Store.SeesAllDesks(caretaker));
        Assert.Equal([s.SiteTicket], s.Store.TicketsFor(caretaker).Select(x => x.Number));
        Assert.True(s.Store.CanSeeTicket(caretaker, s.SiteTicket));
        Assert.False(s.Store.CanSeeTicket(caretaker, s.ItTicket));
        Assert.DoesNotContain(s.SiteTicket, s.Store.TicketsFor(technician).Select(x => x.Number));
        Assert.Equal([s.Site.Id], s.Store.DesksVisibleTo(caretaker).Select(x => x.Id));

        // Typed numbers behave as if another desk's ticket didn't exist.
        Assert.Null(s.Store.FindTicketByRef(s.Store.Ref(s.ItTicket), s.Store.TicketVisibility(caretaker)).Number);
        Assert.Equal(s.SiteTicket, s.Store.FindTicketByRef("SITE-1", s.Store.TicketVisibility(caretaker)).Number);

        // Administrator, and a role with "See all desks", see everything.
        var admin = s.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        Assert.True(s.Store.SeesAllDesks(Principal(admin.Id, StaffRoles.Administrator)));
        var lead = new RoleRecord("Site manager");
        lead.Flags.Add(Modules.Flags.AllDesks);
        Assert.True(s.Store.AddRole(lead).Ok);
        Assert.True(s.Store.SeesAllDesks(Principal(s.Caretaker.Id, "Site manager")));
        Assert.Equal(2, s.Store.TicketsFor(Principal(s.Caretaker.Id, "Site manager")).Count(x => x.Number == s.ItTicket || x.Number == s.SiteTicket));
    }

    [Fact]
    public void With_one_desk_everyone_sees_everything()
    {
        using var test = new TestStore();
        var technician = test.Store.Technicians.First(x => x.Name.Contains("(demo)"));
        Assert.True(test.Store.SeesAllDesks(Principal(technician.Id, technician.Role)));
        Assert.Null(test.Store.VisibleDeskIds(Principal(technician.Id, technician.Role)));
    }

    [Fact]
    public void A_ticket_notification_reaches_only_the_desk_it_is_on()
    {
        using var s = new Setup();
        var account = s.Caretaker.Id;
        var cursor = s.Store.NotificationsFor(HelpdeskStore.StaffAudience, account, -1).Cursor;
        // A first look marks where this account starts, so what follows counts as unread.
        s.Store.UnreadNotificationCount(account, broadcast: true);
        s.Store.AddNotification(HelpdeskStore.StaffAudience, null, HelpdeskStore.NotificationKinds.NewTicket, s.ItTicket);
        s.Store.AddNotification(HelpdeskStore.StaffAudience, null, HelpdeskStore.NotificationKinds.NewTicket, s.SiteTicket);
        var siteOnly = new HashSet<Guid> { s.Site.Id };
        var itOnly = new HashSet<Guid> { s.It.Id };

        Assert.Equal([s.SiteTicket], s.Store.NotificationsFor(HelpdeskStore.StaffAudience, account, cursor, desks: siteOnly).Items.Select(x => x.TicketNumber));
        Assert.Equal([s.ItTicket], s.Store.NotificationsFor(HelpdeskStore.StaffAudience, account, cursor, desks: itOnly).Items.Select(x => x.TicketNumber));
        Assert.Equal(2, s.Store.NotificationsFor(HelpdeskStore.StaffAudience, account, cursor, desks: null).Items.Count);
        Assert.Equal([s.SiteTicket], s.Store.InboxFor(account, broadcast: true, take: 10, desks: siteOnly).Entries.Select(x => x.Notification.TicketNumber));
        Assert.Equal(1, s.Store.UnreadNotificationCount(account, broadcast: true, desks: siteOnly));

        // The desk is kept with the message, so it still reaches the right people after a restart.
        var store = s.Test.Reopen();
        Assert.Equal([s.SiteTicket], store.NotificationsFor(HelpdeskStore.StaffAudience, account, cursor, desks: siteOnly).Items.Select(x => x.TicketNumber));
    }

    [Fact]
    public void Each_desk_posts_to_its_own_Teams_channel_and_the_old_address_becomes_the_first_desks()
    {
        using var s = new Setup();
        Assert.True(s.Store.SetTeamsSettings("https://school.webhook.office.com/it", false, true, true, true).Ok);
        Assert.Equal("https://school.webhook.office.com/it", s.Store.TeamsWebhookFor(s.It.Id));
        Assert.Equal("", s.Store.TeamsWebhookFor(s.Site.Id));
        while (s.Store.TeamsOutbox.TryRead(out _)) { }

        // The Site desk has no channel, so its ticket doesn't post; IT's does, to IT's channel.
        s.Store.QueueTeamsNewTicket(s.SiteTicket, "https://helpdesk.test");
        Assert.False(s.Store.TeamsOutbox.TryRead(out _));
        s.Store.QueueTeamsNewTicket(s.ItTicket, "https://helpdesk.test");
        Assert.True(s.Store.TeamsOutbox.TryRead(out var post));
        Assert.Equal(s.It.Id, post!.DeskId);

        Assert.True(s.Store.SetTeamsSettings("https://school.webhook.office.com/site", false, true, true, true, s.Site.Id).Ok);
        s.Store.QueueTeamsNewTicket(s.SiteTicket, "https://helpdesk.test");
        Assert.True(s.Store.TeamsOutbox.TryRead(out var sitePost));
        Assert.Equal("https://school.webhook.office.com/site", s.Store.TeamsWebhookFor(sitePost!.DeskId));

        // Removing one desk's address leaves the other posting.
        Assert.True(s.Store.SetTeamsSettings(null, true, true, true, true, s.Site.Id).Ok);
        Assert.True(s.Store.TeamsSettings.Ready);
        var store = s.Test.Reopen();
        Assert.Equal("https://school.webhook.office.com/it", store.TeamsWebhookFor(store.Desks[0].Id));
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void The_technician_ping_goes_to_each_desks_own_PCs()
    {
        using var s = new Setup();
        s.Store.SetEduNotifyCatalogue(PingCatalogue);
        // Groups for IT only: the urgent Site ticket doesn't ping anyone.
        Assert.True(s.Store.SetTechnicianPingSettings(true, ["Urgent"], "4", ["Technicians"]).Ok);
        var urgentIt = s.Store.AddTicket(new TicketRecord(0, "Server down", "", s.Store.Users[0].Id, [], null, "Urgent", "Open", "Hardware", DateTime.UtcNow, null));
        var urgentSite = s.Store.AddTicket(new TicketRecord(0, "Boiler out", "", s.Store.Users[0].Id, [], null, "Urgent", "Open", "Heating", DateTime.UtcNow, null));
        var plan = s.Store.PlanTechnicianPings(sending: true);
        Assert.Contains(plan.Send, x => x.TicketNumber == urgentIt && x.Send.Groups.SequenceEqual(["Technicians"]));
        Assert.DoesNotContain(plan.Send, x => x.TicketNumber == urgentSite);

        Assert.True(s.Store.SetTechnicianPingSettings(true, ["Urgent"], "4", null, new Dictionary<Guid, IEnumerable<string>> { [s.Site.Id] = ["Site office"] }).Ok);
        Assert.Equal(["Technicians"], s.Store.TechnicianPingSettings.DeskGroups[s.It.Id]);
        var another = s.Store.AddTicket(new TicketRecord(0, "Flood", "", s.Store.Users[0].Id, [], null, "Urgent", "Open", "Heating", DateTime.UtcNow, null));
        plan = s.Store.PlanTechnicianPings(sending: true);
        Assert.Contains(plan.Send, x => x.TicketNumber == another && x.Send.Groups.SequenceEqual(["Site office"]));

        var store = s.Test.Reopen();
        Assert.Equal(["Site office"], store.TechnicianPingSettings.DeskGroups[s.Site.Id]);
        Assert.Null(store.CompareDatabaseWithMemory());
    }
}
