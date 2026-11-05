namespace EduHelpdesk.Tests;

// Phones paired with the EduHelpdesk app (HelpdeskStore.Phones), the rules for taking a ticket that the ticket page and
// the phone share (HelpdeskStore.TakeTicket), and the "Assigned to you" alert.
public class PhoneTests
{
    private static TechnicianRecord AddTechnician(TestStore test, string name = "Pat Phone", string role = "Technician")
    {
        var technician = new TechnicianRecord(Guid.NewGuid(), name, name.ToLowerInvariant().Replace(' ', '.') + "@test.example",
            test.Store.TechnicianTeams[0], role, PasswordHasher.Hash("Otter-Lantern-Maple-38"));
        test.Store.AddTechnician(technician);
        return test.Store.Technicians.First(x => x.Id == technician.Id);
    }

    private static string Pair(TestStore test, TechnicianRecord technician)
    {
        var (token, error) = test.Store.PairPhone(test.Store.CreatePairingCode(technician.Id), "Pixel 8", null);
        Assert.Null(error);
        return token!;
    }

    [Fact]
    public void A_pairing_code_gives_a_token_once_and_only_while_the_app_is_on()
    {
        using var test = new TestStore();
        var technician = AddTechnician(test);

        var refused = test.Store.PairPhone(test.Store.CreatePairingCode(technician.Id), "Pixel 8", null);
        Assert.Null(refused.Token);
        Assert.Contains("turned off", refused.Error);

        test.Store.SetAllowPhoneApp(true);
        var code = test.Store.CreatePairingCode(technician.Id);
        var (token, _) = test.Store.PairPhone(code, "Pixel 8", null);
        Assert.StartsWith(HelpdeskStore.PhoneTokenPrefix, token);
        Assert.Null(test.Store.PairPhone(code, "Someone else's phone", null).Token);
        Assert.Null(test.Store.PairPhone("made-up", "Pixel 8", null).Token);

        var check = test.Store.CheckPhoneToken(token, null);
        Assert.Equal(HelpdeskStore.PhoneAccess.Ok, check.Access);
        Assert.Equal(technician.Id, check.Technician!.Id);
        Assert.Equal("Pixel 8", Assert.Single(test.Store.PhonesOf(technician.Id)).Name);
    }

    [Fact]
    public void Only_a_hash_of_the_token_is_kept_and_phones_survive_a_restart()
    {
        using var test = new TestStore();
        test.Store.SetAllowPhoneApp(true);
        var technician = AddTechnician(test);
        var token = Pair(test, technician);

        var store = test.Reopen();
        Assert.Equal(HelpdeskStore.PhoneAccess.Ok, store.CheckPhoneToken(token, null).Access);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.DoesNotContain(token, File.ReadAllText(test.DatabasePath, System.Text.Encoding.Latin1));
    }

    [Fact]
    public void A_phone_stops_working_when_revoked_signed_out_everywhere_or_the_password_changes()
    {
        using var test = new TestStore();
        test.Store.SetAllowPhoneApp(true);
        var technician = AddTechnician(test);

        var revoked = Pair(test, technician);
        Assert.True(test.Store.RevokePhone(test.Store.PhonesOf(technician.Id).Single().Id, "Phone revoked"));
        Assert.Equal(HelpdeskStore.PhoneAccess.Unknown, test.Store.CheckPhoneToken(revoked, null).Access);

        var everywhere = Pair(test, technician);
        test.Store.EndSessionsFor(technician.Id);
        Assert.Equal(HelpdeskStore.PhoneAccess.Unknown, test.Store.CheckPhoneToken(everywhere, null).Access);

        var beforeReset = Pair(test, technician);
        test.Store.UpdateTechnician(technician with { PasswordHash = PasswordHasher.Hash("Kettle-Planet-Fox-27") });
        Assert.Equal(HelpdeskStore.PhoneAccess.Unknown, test.Store.CheckPhoneToken(beforeReset, null).Access);
        Assert.Empty(test.Store.PhonesOf(technician.Id));
    }

    [Fact]
    public void A_deactivated_account_or_a_code_from_before_a_password_change_gets_nothing()
    {
        using var test = new TestStore();
        test.Store.SetAllowPhoneApp(true);
        var technician = AddTechnician(test);
        var code = test.Store.CreatePairingCode(technician.Id);
        test.Store.UpdateTechnician(technician with { PasswordHash = PasswordHasher.Hash("Kettle-Planet-Fox-27") });
        Assert.Null(test.Store.PairPhone(code, "Pixel 8", null).Token);

        technician = test.Store.Technicians.First(x => x.Id == technician.Id);
        var token = Pair(test, technician);
        test.Store.UpdateTechnician(technician with { IsActive = false });
        Assert.Equal(HelpdeskStore.PhoneAccess.Unknown, test.Store.CheckPhoneToken(token, null).Access);
    }

    [Fact]
    public void Turning_the_app_off_or_unticking_the_role_pauses_a_phone_without_unpairing_it()
    {
        using var test = new TestStore();
        test.Store.SetAllowPhoneApp(true);
        var technician = AddTechnician(test);
        var token = Pair(test, technician);

        test.Store.SetAllowPhoneApp(false);
        Assert.Equal(HelpdeskStore.PhoneAccess.AppOff, test.Store.CheckPhoneToken(token, null).Access);
        test.Store.SetAllowPhoneApp(true);
        Assert.Equal(HelpdeskStore.PhoneAccess.Ok, test.Store.CheckPhoneToken(token, null).Access);

        var role = test.Store.Roles.First(x => x.Name == technician.Role);
        role.Flags.Remove(Modules.Flags.PhoneApp);
        Assert.Equal(HelpdeskStore.PhoneAccess.NotAllowed, test.Store.CheckPhoneToken(token, null).Access);
        Assert.Contains("Use the phone app", test.Store.PhoneRefusal(technician));
        Assert.Single(test.Store.PhonesOf(technician.Id));
    }

    [Fact]
    public void New_helpdesks_tick_the_phone_app_on_the_technician_roles_but_it_starts_switched_off()
    {
        using var test = new TestStore();
        Assert.False(test.Store.AllowPhoneApp);
        foreach (var role in new[] { "Senior Technician", "Technician", "Junior Technician" })
            Assert.True(test.Store.RoleHasFlag(role, Modules.Flags.PhoneApp), role);
        Assert.StartsWith("The phone app is on.", test.Store.SetAllowPhoneApp(true));
        Assert.True(test.Reopen().AllowPhoneApp);
    }

    [Fact]
    public void App_lock_is_each_technicians_choice_until_the_school_requires_it()
    {
        using var test = new TestStore();
        Assert.False(test.Store.RequirePhoneAppLock);
        Assert.StartsWith("App lock is now required.", test.Store.SetRequirePhoneAppLock(true));
        Assert.Equal("Nothing changed.", test.Store.SetRequirePhoneAppLock(true));
        Assert.True(test.Reopen().RequirePhoneAppLock);
    }

    [Fact]
    public void The_server_hands_out_the_newest_phone_app_it_has()
    {
        var folder = Path.Combine(Path.GetTempPath(), "eduhelpdesk-tests", Guid.NewGuid().ToString("N"));
        Assert.Null(PhoneEndpoints.LatestApp(folder));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var name in new[] { "EduHelpdesk-Android-1.2.0.apk", "EduHelpdesk-Android-1.10.0.apk", "EduHelpdesk-Android-9.9.apk", "notes.txt" })
                File.WriteAllText(Path.Combine(folder, name), "");
            var latest = PhoneEndpoints.LatestApp(folder);
            Assert.Equal(new Version(1, 10, 0), latest?.Version);
            Assert.EndsWith("EduHelpdesk-Android-1.10.0.apk", latest?.Path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Assign_to_me_and_on_my_way_give_the_ticket_pages_answers()
    {
        using var test = new TestStore();
        var technician = AddTechnician(test);
        var requester = test.AddRequester("Rita Requester");
        var number = test.AddTicket(requester.Id);

        var assigned = test.Store.AssignToMe(number, technician.Id);
        Assert.True(assigned.Ok);
        Assert.Equal("Assigned to you.", assigned.Message);
        Assert.Equal("This ticket is already assigned to you.", test.Store.AssignToMe(number, technician.Id).Message);

        var other = test.AddTicket(requester.Id, "Printer jammed");
        var onMyWay = test.Store.SayOnMyWay(other, technician.Id);
        Assert.True(onMyWay.Ok);
        Assert.True(onMyWay.Assigned);
        Assert.StartsWith("Assigned to you. ", onMyWay.Message);
        var ticket = test.Store.Tickets.First(x => x.Number == other);
        Assert.Equal(technician.Id, ticket.TechnicianId);
        Assert.Equal("Pat Phone is on their way.", ticket.Comments.Last().Text);
        Assert.False(test.Store.SayOnMyWay(other, technician.Id).Assigned);

        Assert.Equal(HelpdeskStore.NotATechnicianMessage, test.Store.AssignToMe(number, Guid.NewGuid()).Message);
    }

    [Fact]
    public void Nobody_is_on_their_way_to_a_closed_ticket_or_one_outside_their_team()
    {
        using var test = new TestStore();
        var technician = AddTechnician(test);
        var requester = test.AddRequester("Rita Requester");
        var closed = test.AddTicket(requester.Id, status: "Closed", closedAt: DateTime.UtcNow);
        Assert.Equal("This ticket is closed. Reopen it first if you're going back to it.", test.Store.SayOnMyWay(closed, technician.Id).Message);

        test.Store.AddTechnicianTeam("Network");
        var number = test.AddTicket(requester.Id);
        test.Store.UpdateTicket(test.Store.Tickets.First(x => x.Number == number) with { TeamName = "Network" });
        var refused = test.Store.AssignToMe(number, technician.Id);
        Assert.False(refused.Ok);
        Assert.Equal("This ticket belongs to the Network team, which you're not in. Change the team first if you're taking it on.", refused.Message);
    }

    [Fact]
    public void Being_given_a_ticket_is_an_alert_for_that_technician_alone()
    {
        using var test = new TestStore();
        var technician = AddTechnician(test);
        var colleague = AddTechnician(test, "Colin Colleague");
        var number = test.AddTicket(test.AddRequester("Rita Requester").Id);

        test.Store.UpdateTicket(test.Store.Tickets.First(x => x.Number == number) with { TechnicianId = technician.Id });

        var alert = Assert.Single(test.Store.InboxFor(technician.Id, broadcast: false, take: 10).Entries).Notification;
        Assert.Equal(HelpdeskStore.NotificationKinds.Assigned, alert.Kind);
        Assert.Equal(number, alert.TicketNumber);
        Assert.Empty(test.Store.InboxFor(colleague.Id, broadcast: false, take: 10).Entries);
    }

    [Fact]
    public void A_reply_can_tell_the_requester_and_move_the_status_but_a_note_tells_nobody()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Rita Requester");
        var number = test.AddTicket(requester.Id);

        Assert.Equal("Enter a reply before saving.", test.Store.AddTicketReply(number, " ", internalNote: false, notifyRequester: true, status: null).Message);
        Assert.Equal("Select a valid status.", test.Store.AddTicketReply(number, "Fixed", internalNote: false, notifyRequester: true, status: "Nowhere").Message);
        Assert.Empty(test.Store.Tickets.First(x => x.Number == number).Comments);

        var closed = test.Store.AddTicketReply(number, "Swapped the cable.", internalNote: false, notifyRequester: true, status: "Closed");
        Assert.True(closed.Ok);
        Assert.True(closed.Notified);
        Assert.Equal("Reply added and ticket closed. The requester has been notified.", closed.Message);
        Assert.Equal("Closed", test.Store.Tickets.First(x => x.Number == number).Status);

        var note = test.Store.AddTicketReply(number, "Cable was chewed.", internalNote: true, notifyRequester: true, status: "Open");
        Assert.False(note.Notified);
        Assert.Equal("Internal note added and ticket reopened.", note.Message);
        var ticket = test.Store.Tickets.First(x => x.Number == number);
        Assert.Equal("Open", ticket.Status);
        Assert.Null(ticket.ClosedAt);
        Assert.True(ticket.Comments.Last().IsInternal);
    }

    [Fact]
    public void Closing_asks_for_a_message_where_settings_want_one_and_status_and_priority_take_only_listed_values()
    {
        using var test = new TestStore();
        var number = test.AddTicket(test.AddRequester("Rita Requester").Id);
        var priority = test.Store.Tickets.First(x => x.Number == number).Priority;
        test.Store.SetCloseMessageRequirements([priority], []);

        var refused = test.Store.CloseTicket(number, " ");
        Assert.False(refused.Ok);
        Assert.StartsWith($"A closing message is required for {priority} priority", refused.Message);
        Assert.True(test.Store.CloseTicket(number, "Replaced the lamp.").Ok);
        var ticket = test.Store.Tickets.First(x => x.Number == number);
        Assert.Equal("Closed", ticket.Status);
        Assert.Equal("Replaced the lamp.", ticket.Comments.Last().Text);
        Assert.Equal("Ticket is already closed.", test.Store.CloseTicket(number, "Again").Message);

        Assert.Equal("Select a valid value.", test.Store.SetTicketStatus(number, "Nowhere").Message);
        Assert.Equal("Select a value.", test.Store.SetTicketPriority(number, "").Message);
        var other = test.Store.Priorities.First(x => x != priority);
        Assert.Equal("Ticket updated.", test.Store.SetTicketPriority(number, other.ToUpperInvariant()).Message);
        Assert.Equal(other, test.Store.Tickets.First(x => x.Number == number).Priority);
        Assert.True(test.Store.SetTicketStatus(number, "open").Ok);
        Assert.Equal("Open", test.Store.Tickets.First(x => x.Number == number).Status);
    }
}
