using System.Text.Json;

namespace EduHelpdesk.Tests;

// What EduNotify does for the helpdesk beyond notices and the ping (HelpdeskStore.EduNotifyLink): technician calls raising
// and assigning tickets, the incoming key, and the requester pop-ups and technician alerts worked out each pass.
public class EduNotifyLinkTests
{
    private const string Domain = "school.org.uk";

    // A store with EduNotify connected and its catalogue fetched, staff at the school's domain, and one technician.
    private sealed class Site : IDisposable
    {
        public TestStore Test { get; } = new();
        public HelpdeskStore Store => Test.Store;
        public UserRecord Jones { get; }
        public TechnicianRecord Smith { get; }

        public Site()
        {
            Assert.True(Store.SetEduNotifySettings("http://edunotify.school.local:5080", "enk_SECRETKEY1234567890", false, true).Ok);
            Store.SetEduNotifyCatalogue(new EduNotify.Catalogue(DateTime.UtcNow,
                [new("7", "Helpdesk pop-up", "editable", false, true, true, "", "")], [new("1", "Technicians")]));
            Assert.True(Store.SetEduNotifyPersonMatching(Domain, 8).Ok);
            Assert.True(Store.SetSiteAddress("https://helpdesk.school.local").Ok);
            var jones = new UserRecord(Guid.NewGuid(), "Ms Jones", $"jones@{Domain}", "English", "B12");
            Store.AddUser(jones);
            Jones = jones;
            Smith = new TechnicianRecord(Guid.NewGuid(), "Mr Smith", $"smith@{Domain}", Store.TechnicianTeams[0]);
            Store.AddTechnician(Smith);
        }

        public void On(params string[] events) =>
            Assert.True(Store.SetEduNotifyEventRules(events.ToDictionary(x => x, _ => new HelpdeskStore.EduNotifyEventRule(true, "7", "", []))).Ok);

        public int Ticket(Guid? technician = null) =>
            Store.AddTicket(new TicketRecord(0, "Projector", "No picture", Jones.Id, [], technician, Store.Priorities[0], "Open", Store.Categories[0], DateTime.UtcNow, null));

        public TicketRecord Get(int number) => Store.Tickets.First(x => x.Number == number);

        // A pass, with sending on. The first one only learns how things stand.
        public HelpdeskStore.EduNotifyEventPlan Pass(DateTime? at = null) => Store.PlanEduNotifyEvents(true, at);

        public void Dispose() => Test.Dispose();
    }

    private static HelpdeskStore.EduNotifyCallSettingsInput Calls(bool accept = true, IReadOnlyList<HelpdeskStore.EduNotifyReasonMap>? reasons = null, string? category = null, string? priority = null) =>
        new(accept, true, true, reasons ?? [], category, priority, null);

    private static HelpdeskStore.CallTicketRequest Call(string id = "edunotify-oncall-1", string reason = "Projector", bool urgent = false, string? email = "jones@school.org.uk") =>
        new(id, "Projector: B12 (Ms Jones)", "No picture\n\nPC: B12", email, "Ms Jones", "Technician", reason, urgent, "B12");

    // ---- Technician calls ----

    [Fact]
    public void A_call_raises_one_ticket_logged_as_its_reason_says()
    {
        using var site = new Site();
        var categories = site.Store.Categories;
        var priorities = site.Store.Priorities;
        Assert.True(site.Store.SetEduNotifyCallSettings(Calls(reasons: [new("Technician", "Projector", categories[^1], priorities[^1], priorities[0])],
            category: categories[0], priority: priorities[0])).Ok);

        var raised = site.Store.RaiseCallTicket(Call());
        Assert.Equal(201, raised.Status);
        var ticket = site.Get(raised.Number!.Value);
        Assert.Equal(categories[^1], ticket.Category);
        Assert.Equal(priorities[^1], ticket.Priority);
        Assert.Equal(site.Jones.Id, ticket.RequesterId);
        Assert.Equal("Projector: B12 (Ms Jones)", ticket.Title);
        Assert.Contains("Raised from EduNotify by Ms Jones with Technician: Projector", ticket.History[0].Details);
        Assert.Equal(site.Store.Ref(ticket.Number), raised.Ref);

        // The same call again (EduNotify retrying) gets the same ticket.
        var again = site.Store.RaiseCallTicket(Call());
        Assert.Equal(200, again.Status);
        Assert.Equal(raised.Number, again.Number);

        // Urgent uses the reason's urgent priority.
        Assert.Equal(priorities[0], site.Get(site.Store.RaiseCallTicket(Call("edunotify-oncall-2", urgent: true)).Number!.Value).Priority);
    }

    [Fact]
    public void Unknown_reasons_and_requesters_still_get_a_ticket()
    {
        using var site = new Site();
        Assert.True(site.Store.SetEduNotifyCallSettings(Calls(category: site.Store.Categories[0], priority: site.Store.Priorities[0])).Ok);

        var raised = site.Store.RaiseCallTicket(Call(reason: "Something new", email: "visitor@elsewhere.example"));

        var ticket = site.Get(raised.Number!.Value);
        Assert.Equal(site.Store.Categories[0], ticket.Category);
        Assert.Equal(HelpdeskStore.CallRequesterName, site.Store.Users.First(x => x.Id == ticket.RequesterId).Name);
        Assert.Contains("Requester not matched to anyone here: Ms Jones (visitor@elsewhere.example)", ticket.Description);
    }

    [Fact]
    public void Calls_are_refused_while_not_accepted()
    {
        using var site = new Site();
        Assert.Equal(503, site.Store.RaiseCallTicket(Call()).Status);
        // Accepting needs somewhere for unknown reasons to go.
        Assert.False(site.Store.SetEduNotifyCallSettings(Calls()).Ok);
        Assert.Equal(400, site.Store.RaiseCallTicket(Call(id: "")).Status);
    }

    [Fact]
    public void Call_tickets_never_ping_or_say_logged()
    {
        using var site = new Site();
        Assert.True(site.Store.SetTechnicianPingSettings(true, [site.Store.Priorities[0]], "7", ["Technicians"]).Ok);
        Assert.True(site.Store.SetEduNotifyCallSettings(Calls(category: site.Store.Categories[0], priority: site.Store.Priorities[0])).Ok);
        site.On(HelpdeskStore.EduNotifyEvents.Logged);
        site.Pass();

        var number = site.Store.RaiseCallTicket(Call()).Number!.Value;

        Assert.DoesNotContain(site.Store.PlanTechnicianPings(true).Send, x => x.TicketNumber == number);
        Assert.Empty(site.Pass().Send);
    }

    [Fact]
    public void On_my_way_assigns_the_first_technician()
    {
        using var site = new Site();
        Assert.True(site.Store.SetEduNotifyCallSettings(Calls(category: site.Store.Categories[0], priority: site.Store.Priorities[0])).Ok);
        var raised = site.Store.RaiseCallTicket(Call());
        var ordinary = site.Ticket();

        Assert.Equal(404, site.Store.AssignCallTicket(site.Store.Ref(ordinary), $"smith@{Domain}").Status);
        Assert.Equal(409, site.Store.AssignCallTicket(raised.Ref, "nobody@school.org.uk").Status);
        var assigned = site.Store.AssignCallTicket(raised.Ref, $"SMITH@{Domain}");
        Assert.Equal(200, assigned.Status);
        Assert.Equal("Mr Smith", assigned.AssignedTo);
        Assert.Equal(site.Smith.Id, site.Get(raised.Number!.Value).TechnicianId);
        Assert.Contains(site.Get(raised.Number!.Value).History, x => x.Action == "On my way in EduNotify");

        // Already assigned: it stays with them.
        Assert.Equal("Mr Smith", site.Store.AssignCallTicket(raised.Ref, "nobody@school.org.uk").AssignedTo);
    }

    [Fact]
    public void Resolving_a_call_ticket_tells_EduNotify_once()
    {
        using var site = new Site();
        Assert.True(site.Store.SetEduNotifyCallSettings(Calls(category: site.Store.Categories[0], priority: site.Store.Priorities[0])).Ok);
        var raised = site.Store.RaiseCallTicket(Call());
        site.Pass();
        Assert.Empty(site.Pass().CloseCalls);

        site.Store.UpdateTicket(site.Get(raised.Number!.Value) with { Status = "Closed", ClosedAt = DateTime.UtcNow });

        var close = Assert.Single(site.Pass().CloseCalls);
        Assert.Equal(raised.Ref, close.Ref);
        site.Store.RecordEduNotifyCallClosed(close.Number);
        Assert.Empty(site.Pass().CloseCalls);
    }

    [Fact]
    public void The_incoming_key_is_shown_once_and_only_the_latest_works()
    {
        using var site = new Site();
        Assert.False(site.Store.CheckEduNotifyIncomingKey("ehk_anything"));
        var first = site.Store.NewEduNotifyIncomingKey();
        Assert.StartsWith("ehk_", first);
        Assert.True(site.Store.CheckEduNotifyIncomingKey(first));
        var second = site.Store.NewEduNotifyIncomingKey();
        Assert.False(site.Store.CheckEduNotifyIncomingKey(first));
        Assert.True(site.Store.CheckEduNotifyIncomingKey(second));
        Assert.DoesNotContain(site.Store.GetAuditEntries(), x => x.Details.Contains(second));
        site.Store.RemoveEduNotifyIncomingKey();
        Assert.False(site.Store.CheckEduNotifyIncomingKey(second));
    }

    // ---- Pop-ups and alerts ----

    [Fact]
    public void Requester_pop_ups_go_to_their_login_with_the_portal_link()
    {
        using var site = new Site();
        site.On(HelpdeskStore.EduNotifyEvents.Logged, HelpdeskStore.EduNotifyEvents.Assigned, HelpdeskStore.EduNotifyEvents.Resolved, HelpdeskStore.EduNotifyEvents.AssignedToYou);
        Assert.Empty(site.Pass().Send); // learns how things stand

        var number = site.Ticket();
        var logged = Assert.Single(site.Pass().Send);
        Assert.Equal(HelpdeskStore.EduNotifyEvents.Logged, logged.EventKey);
        Assert.Equal(["jones"], logged.Send.Users);
        Assert.Equal($"EH-{number}-to-jones", logged.Send.Reference);
        Assert.Equal($"{site.Store.Ref(number)}: logged", logged.Send.Title);
        Assert.Equal($"https://helpdesk.school.local/Portal/Ticket?number={number}", logged.Send.LinkUrl);

        site.Store.UpdateTicket(site.Get(number) with { TechnicianId = site.Smith.Id });
        var assigned = site.Pass().Send;
        Assert.Contains(assigned, x => x.EventKey == HelpdeskStore.EduNotifyEvents.Assigned && x.Send.Title!.EndsWith("Mr Smith is dealing with it") && x.Send.Users!.SequenceEqual(["jones"]));
        var toTech = Assert.Single(assigned, x => x.EventKey == HelpdeskStore.EduNotifyEvents.AssignedToYou);
        Assert.Equal(["smith"], toTech.Send.Users);
        Assert.Equal($"https://helpdesk.school.local/Job?number={number}", toTech.Send.LinkUrl);

        site.Store.UpdateTicket(site.Get(number) with { Status = "Closed", ClosedAt = DateTime.UtcNow });
        var resolved = site.Pass();
        Assert.Contains(resolved.Send, x => x.EventKey == HelpdeskStore.EduNotifyEvents.Resolved && x.Send.Title == $"{site.Store.Ref(number)}: resolved");
        Assert.Contains(HelpdeskStore.DeskReference(number), resolved.Recall);
        Assert.Empty(site.Pass().Send);
    }

    [Fact]
    public void Replies_reach_the_other_side()
    {
        using var site = new Site();
        site.On(HelpdeskStore.EduNotifyEvents.StaffReplied, HelpdeskStore.EduNotifyEvents.RequesterReplied);
        var number = site.Ticket(site.Smith.Id);
        site.Pass();

        site.Store.AddNotification(HelpdeskStore.PortalAudience, site.Jones.Id, HelpdeskStore.NotificationKinds.StaffComment, number);
        site.Store.AddNotification(HelpdeskStore.StaffAudience, null, HelpdeskStore.NotificationKinds.Reply, number);
        var sends = site.Pass().Send;

        Assert.Contains(sends, x => x.EventKey == HelpdeskStore.EduNotifyEvents.StaffReplied && x.Send.Users!.SequenceEqual(["jones"]) && x.Send.Title!.EndsWith("Mr Smith replied"));
        Assert.Contains(sends, x => x.EventKey == HelpdeskStore.EduNotifyEvents.RequesterReplied && x.Send.Users!.SequenceEqual(["smith"]));
    }

    [Fact]
    public void Unassigned_tickets_alert_the_desks_PCs()
    {
        using var site = new Site();
        Assert.True(site.Store.SetTechnicianPingSettings(false, [], "", ["Technicians"]).Ok); // the desk's PCs
        site.On(HelpdeskStore.EduNotifyEvents.Reopened);
        var number = site.Ticket();
        site.Store.UpdateTicket(site.Get(number) with { Status = "Closed", ClosedAt = DateTime.UtcNow });
        site.Pass();

        site.Store.AddRequesterComment(number, "Still broken");

        var reopened = Assert.Single(site.Pass().Send);
        Assert.Equal(["Technicians"], reopened.Send.Groups);
        Assert.Equal(HelpdeskStore.DeskReference(number), reopened.Send.Reference);
    }

    [Fact]
    public void SLA_alerts_fire_once_when_the_time_is_crossed()
    {
        using var site = new Site();
        site.On(HelpdeskStore.EduNotifyEvents.SlaWarning, HelpdeskStore.EduNotifyEvents.SlaBreached);
        var now = DateTime.UtcNow;
        var number = site.Ticket(site.Smith.Id);
        site.Store.UpdateTicket(site.Get(number) with { DueDate = now.AddMinutes(90), DueDateOverridden = true });
        site.Pass(now);

        Assert.Empty(site.Pass(now.AddMinutes(20)).Send);
        var warning = Assert.Single(site.Pass(now.AddMinutes(35)).Send); // 60 minutes before 90
        Assert.EndsWith("due in 1 hour", warning.Send.Title);
        Assert.Empty(site.Pass(now.AddMinutes(40)).Send);
        var breached = Assert.Single(site.Pass(now.AddMinutes(91)).Send);
        Assert.EndsWith("overdue", breached.Send.Title);
        Assert.Empty(site.Pass(now.AddMinutes(95)).Send);
    }

    [Fact]
    public void Pop_ups_come_down_after_the_set_time()
    {
        using var site = new Site();
        site.On(HelpdeskStore.EduNotifyEvents.Logged);
        site.Pass();
        site.Ticket();
        var sent = Assert.Single(site.Pass().Send);
        site.Store.RecordEduNotifyPopupSent(sent, new EduNotify.Sent(9, 1, 1));

        Assert.DoesNotContain(sent.Reference, site.Pass(DateTime.UtcNow.AddHours(7)).Recall);
        Assert.Contains(sent.Reference, site.Pass(DateTime.UtcNow.AddHours(9)).Recall);
        site.Store.RecordEduNotifyPopupGone(sent.Reference);
        Assert.Empty(site.Pass(DateTime.UtcNow.AddHours(10)).Recall);
    }

    [Fact]
    public void Nothing_is_saved_up_while_sending_is_off_and_people_off_the_domain_get_nothing()
    {
        using var site = new Site();
        site.On(HelpdeskStore.EduNotifyEvents.Logged);
        site.Pass();
        site.Ticket();
        Assert.Empty(site.Store.PlanEduNotifyEvents(false).Send);
        Assert.Empty(site.Pass().Send);

        var unmatched = site.Store.EduNotifyUnmatchedPeople;
        var visitor = site.Test.AddRequester("Val Visitor"); // val.visitor@test.example
        site.Store.AddTicket(new TicketRecord(0, "Wi-Fi", "", visitor.Id, [], null, site.Store.Priorities[0], "Open", site.Store.Categories[0], DateTime.UtcNow, null));
        Assert.Empty(site.Pass().Send);
        Assert.Equal(unmatched + 1, site.Store.EduNotifyUnmatchedPeople);
    }

    [Theory]
    [InlineData("jsmith@school.org.uk", "jsmith")]
    [InlineData("J.Smith@SCHOOL.org.uk", "j.smith")]
    [InlineData("jsmith@other.example", null)]
    [InlineData("", null)]
    public void Emails_at_the_domain_are_logins(string email, string? login) => Assert.Equal(login, HelpdeskStore.LoginFor(email, Domain));

    [Fact]
    public void A_send_carries_users_and_the_link()
    {
        var json = JsonDocument.Parse(EduNotify.SendJson(new("7", false, [], "IT-1: resolved", "Open it.", "EH-1-to-jones", ["jones"], "Open ticket", "https://helpdesk.school.local/Portal/Ticket?number=1"))).RootElement;
        Assert.Equal("jones", json.GetProperty("recipients").GetProperty("users")[0].GetString());
        Assert.Equal("Open ticket", json.GetProperty("link").GetProperty("label").GetString());
        Assert.Equal("https://helpdesk.school.local/Portal/Ticket?number=1", json.GetProperty("link").GetProperty("url").GetString());
    }

    [Fact]
    public void The_settings_survive_a_restart()
    {
        using var site = new Site();
        site.On(HelpdeskStore.EduNotifyEvents.Resolved);
        Assert.True(site.Store.SetEduNotifyCallSettings(Calls(category: site.Store.Categories[0], priority: site.Store.Priorities[0])).Ok);
        var key = site.Store.NewEduNotifyIncomingKey();

        var reopened = site.Test.Reopen();

        Assert.Equal(Domain, reopened.EduNotifyLinkSettings.EmailDomain);
        Assert.True(reopened.EduNotifyLinkSettings.RuleFor(HelpdeskStore.EduNotifyEvents.Resolved).Enabled);
        Assert.True(reopened.EduNotifyAcceptsCalls);
        Assert.True(reopened.CheckEduNotifyIncomingKey(key));
    }
}
