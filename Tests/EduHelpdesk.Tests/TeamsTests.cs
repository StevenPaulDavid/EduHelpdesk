using System.Text.Json;

namespace EduHelpdesk.Tests;

// Settings → Teams channel (HelpdeskStore.Teams, TeamsWebhook): which addresses are accepted, that the secret address stays
// out of the audit log and the raw data page, that only the events chosen post and only in generic words, and that a ticket
// is announced as overdue once.
public class TeamsTests
{
    private const string Address = "https://prod-12.westeurope.logic.azure.com:443/workflows/abc123/triggers/manual/paths/invoke?sig=SECRETSIGNATURE";

    private static List<HelpdeskStore.TeamsPost> Drain(HelpdeskStore store)
    {
        var posts = new List<HelpdeskStore.TeamsPost>();
        while (store.TeamsOutbox.TryRead(out var post)) posts.Add(post);
        return posts;
    }

    private static int AddOverdue(TestStore test, Guid requester, TimeSpan overdueBy, string title = "Projector has no signal") =>
        test.Store.AddTicket(new TicketRecord(0, title, "It flickers, then nothing.", requester, [], null, "Normal", "Open", test.Store.Categories[0],
            DateTime.UtcNow.AddDays(-5), null, null, DateTime.UtcNow - overdueBy));

    [Theory]
    [InlineData(Address, true)]
    [InlineData("  https://contoso.webhook.office.com/webhookb2/abc  ", true)]
    [InlineData("", false)]
    [InlineData("not a url", false)]
    [InlineData("http://prod-12.westeurope.logic.azure.com/workflows/abc", false)]
    [InlineData("https://192.168.1.20/hook", false)]
    [InlineData("https://[::1]/hook", false)]
    [InlineData("https://localhost/hook", false)]
    [InlineData("https://fileserver/hook", false)]
    [InlineData("https://printer.local/hook", false)]
    [InlineData("https://user:pass@example.com/hook", false)]
    [InlineData("ftp://example.com/hook", false)]
    public void Only_an_https_address_on_the_internet_is_accepted(string typed, bool accepted)
    {
        var (ok, url, problem) = TeamsWebhook.Check(typed);
        Assert.Equal(accepted, ok);
        if (accepted) Assert.Equal(typed.Trim(), url);
        else Assert.NotEmpty(problem);
    }

    [Fact]
    public void Posting_is_off_until_an_address_is_saved_and_the_choices_survive_a_restart()
    {
        using var test = new TestStore();
        Assert.False(test.Store.TeamsSettings.Ready);
        Assert.False(test.Store.TeamsSettings.HasUrl);

        Assert.False(test.Store.SetTeamsSettings("nonsense", false, true, true, true).Ok);
        Assert.False(test.Store.TeamsSettings.HasUrl);

        // Ticking "on" with nothing to post to leaves it off.
        test.Store.SetTeamsSettings(null, false, true, true, true);
        Assert.False(test.Store.TeamsSettings.Enabled);

        Assert.True(test.Store.SetTeamsSettings(Address, false, true, true, false).Ok);
        var store = test.Reopen();
        Assert.Equal(new HelpdeskStore.TeamsSettingsView(true, "prod-12.westeurope.logic.azure.com", true, true, false), store.TeamsSettings);
        Assert.Equal(Address, store.TeamsWebhookAddress);
        Assert.Null(store.CompareDatabaseWithMemory());

        // A blank address keeps the one saved.
        store.SetTeamsSettings("", false, true, true, true);
        Assert.Equal(Address, store.TeamsWebhookAddress);
        Assert.True(store.TeamsSettings.Overdue);

        store.SetTeamsSettings(null, remove: true, true, true, true);
        Assert.Equal("", test.Reopen().TeamsWebhookAddress);
        Assert.False(test.Store.TeamsSettings.Ready);
    }

    [Fact]
    public void The_address_is_never_written_to_the_audit_log_and_is_masked_in_the_raw_data_for_anyone_but_an_administrator()
    {
        using var test = new TestStore();
        test.Store.SetTeamsSettings(Address, false, true, true, true);

        var audit = test.Store.GetAuditEntries().Where(x => x.Entity == "Teams channel").ToList();
        Assert.NotEmpty(audit);
        Assert.DoesNotContain(audit, x => (x.Action + x.Details).Contains("SECRETSIGNATURE") || (x.Action + x.Details).Contains("logic.azure.com"));

        var raw = new RawDatabase(test.Store);
        var everything = new RawDatabase.Query(null, null, "equals", null, false);
        var hidden = raw.Read("Metadata", everything, showSecrets: false, page: 0)!;
        var key = hidden.Columns.ToList().FindIndex(x => x.Name == "Key");
        var value = hidden.Columns.ToList().FindIndex(x => x.Name == "Value");
        var row = hidden.Rows.Single(x => Equals(x[key], "TeamsWebhookUrl"));
        Assert.IsType<RawDatabase.Hidden>(row[value]);
        // Other settings stay readable, and searching can't reach the secret.
        Assert.Contains(hidden.Rows, x => Equals(x[key], "TeamsEnabled") && Equals(x[value], "1"));
        Assert.Equal(0, raw.Read("Metadata", everything with { Search = "SECRETSIGNATURE" }, showSecrets: false, page: 1)!.Total);

        var shown = raw.Read("Metadata", everything with { Search = "SECRETSIGNATURE" }, showSecrets: true, page: 1)!;
        Assert.Equal(1, shown.Total);
    }

    [Fact]
    public void A_new_ticket_posts_only_when_posting_and_that_event_are_on_and_says_nothing_about_it()
    {
        using var test = new TestStore();
        test.AddRequester("Ada Lovelace");
        test.Store.QueueTeamsNewTicket(1042, "https://helpdesk.test/");
        Assert.Empty(Drain(test.Store));

        test.Store.SetTeamsSettings(Address, false, true, false, true);
        test.Store.QueueTeamsNewTicket(1042, "https://helpdesk.test/");
        Assert.Empty(Drain(test.Store));

        test.Store.SetTeamsSettings(null, false, true, true, true);
        test.Store.QueueTeamsNewTicket(1042, "https://helpdesk.test/");
        var post = Assert.Single(Drain(test.Store));
        Assert.Equal("New ticket #1042", post.Heading);
        Assert.Equal("https://helpdesk.test/Job?number=1042", post.LinkUrl);

        // Nothing but the number and a link: no title, no requester, and the card carries the same.
        var json = TeamsWebhook.Message(post);
        Assert.DoesNotContain("Ada Lovelace", json);
        Assert.DoesNotContain("Lovelace", json);
        using var card = JsonDocument.Parse(json);
        Assert.Equal("message", card.RootElement.GetProperty("type").GetString());
        var content = card.RootElement.GetProperty("attachments")[0].GetProperty("content");
        Assert.Equal("AdaptiveCard", content.GetProperty("type").GetString());
        Assert.Equal("https://helpdesk.test/Job?number=1042", content.GetProperty("actions")[0].GetProperty("url").GetString());
    }

    [Fact]
    public void The_helpdesk_address_set_in_sign_in_security_wins_over_the_one_the_request_came_in_on_and_no_address_means_no_button()
    {
        using var test = new TestStore();
        test.Store.SetTeamsSettings(Address, false, true, true, true);

        test.Store.QueueTeamsNewTicket(7, null);
        Assert.Null(Assert.Single(Drain(test.Store)).LinkUrl);

        test.Store.SetSiteAddress("https://helpdesk.school.org.uk");
        test.Store.QueueTeamsNewTicket(7, "https://elsewhere.test/");
        Assert.Equal("https://helpdesk.school.org.uk/Job?number=7", Assert.Single(Drain(test.Store)).LinkUrl);
    }

    [Fact]
    public void Tickets_already_overdue_when_posting_is_switched_on_are_not_announced_but_the_next_one_is_once_only()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Ada Lovelace");
        var already = AddOverdue(test, requester.Id, TimeSpan.FromHours(3));
        test.Store.SetTeamsSettings(Address, false, true, true, true);

        Assert.Equal(0, test.Store.AnnounceOverdue(DateTime.UtcNow));
        Assert.Empty(Drain(test.Store));

        var fresh = AddOverdue(test, requester.Id, TimeSpan.FromMinutes(5));
        Assert.Equal(1, test.Store.AnnounceOverdue(DateTime.UtcNow));
        var post = Assert.Single(Drain(test.Store));
        Assert.Equal($"Ticket #{fresh} is overdue", post.Heading);
        Assert.DoesNotContain("Projector", TeamsWebhook.Message(post));

        // Looking again announces nothing, and neither does the ticket that was overdue to begin with.
        Assert.Equal(0, test.Store.AnnounceOverdue(DateTime.UtcNow.AddMinutes(5)));
        Assert.Empty(Drain(test.Store));
        Assert.NotEqual(already, fresh);
    }

    [Fact]
    public void A_ticket_given_a_new_due_date_that_also_passes_is_announced_again_and_a_closed_one_is_forgotten()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Ada Lovelace");
        test.Store.SetTeamsSettings(Address, false, true, true, true);
        test.Store.AnnounceOverdue(DateTime.UtcNow);
        var number = AddOverdue(test, requester.Id, TimeSpan.FromMinutes(1));
        Assert.Equal(1, test.Store.AnnounceOverdue(DateTime.UtcNow));
        Drain(test.Store);

        var ticket = test.Store.Tickets.First(x => x.Number == number);
        test.Store.UpdateTicket(ticket with { DueDate = DateTime.UtcNow.AddHours(1) });
        Assert.Equal(0, test.Store.AnnounceOverdue(DateTime.UtcNow));
        Assert.Equal(1, test.Store.AnnounceOverdue(DateTime.UtcNow.AddHours(2)));
        Assert.Single(Drain(test.Store));

        // Closed, then reopened with a due date already past: it is news again.
        ticket = test.Store.Tickets.First(x => x.Number == number);
        test.Store.UpdateTicket(ticket with { Status = "Closed", ClosedAt = DateTime.UtcNow });
        Assert.Equal(0, test.Store.AnnounceOverdue(DateTime.UtcNow.AddHours(3)));
        ticket = test.Store.Tickets.First(x => x.Number == number);
        test.Store.UpdateTicket(ticket with { Status = "Open", ClosedAt = null });
        Assert.Equal(1, test.Store.AnnounceOverdue(DateTime.UtcNow.AddHours(3)));
    }

    [Fact]
    public void A_crowd_of_tickets_turning_overdue_at_once_is_one_message_with_a_link_to_the_list()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Ada Lovelace");
        test.Store.SetSiteAddress("https://helpdesk.school.org.uk/");
        test.Store.SetTeamsSettings(Address, false, true, true, true);
        test.Store.AnnounceOverdue(DateTime.UtcNow);

        for (var i = 0; i < 4; i++) AddOverdue(test, requester.Id, TimeSpan.FromMinutes(1 + i));
        Assert.Equal(4, test.Store.AnnounceOverdue(DateTime.UtcNow));
        var post = Assert.Single(Drain(test.Store));
        Assert.Equal("4 tickets are now overdue", post.Heading);
        Assert.Equal("https://helpdesk.school.org.uk/Jobs?view=overdue", post.LinkUrl);

        for (var i = 0; i < 3; i++) AddOverdue(test, requester.Id, TimeSpan.FromMinutes(1 + i));
        Assert.Equal(3, test.Store.AnnounceOverdue(DateTime.UtcNow));
        Assert.Equal(3, Drain(test.Store).Count);
    }

    [Fact]
    public void Overdue_posts_stay_quiet_while_that_event_or_posting_is_off_and_switching_back_on_does_not_replay_a_backlog()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Ada Lovelace");
        test.Store.SetTeamsSettings(Address, false, true, true, false);
        AddOverdue(test, requester.Id, TimeSpan.FromMinutes(1));
        Assert.Equal(0, test.Store.AnnounceOverdue(DateTime.UtcNow));
        Assert.Empty(Drain(test.Store));

        test.Store.SetTeamsSettings(null, false, true, true, true);
        Assert.Equal(0, test.Store.AnnounceOverdue(DateTime.UtcNow));
        Assert.Empty(Drain(test.Store));
    }
}
