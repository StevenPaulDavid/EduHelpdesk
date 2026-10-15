using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduHelpdesk.Tests;

// Settings → EduNotify and the Tell staff panel (HelpdeskStore.EduNotify, EduNotify, EduNotifyClient) against a pretend
// EduNotify: which addresses and keys are accepted, that the key stays out of the audit log, the raw data page and every
// message shown, what a send looks like on the wire, and that a closed or deleted ticket's notice is recalled.
public class EduNotifyTests
{
    private const string Key = "enk_SECRETKEY1234567890";
    private const string Address = "http://edunotify.school.local:5080";

    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string? Auth, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel);
            Requests.Add((request.Method, request.RequestUri, request.Headers.Authorization?.ToString(), body));
            return answer(request, body);
        }
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private const string TemplatesJson = """
        [{"id":3,"name":"IT outage","kind":"editable","emergency":false,"canChangeTitle":true,"canChangeBody":true,"title":"IT problem","body":"We're on it."},
         {"id":1,"name":"LOCKDOWN","kind":"static","emergency":true,"canChangeTitle":false,"canChangeBody":false,"title":"LOCKDOWN","body":"Lock your door."}]
        """;
    private const string GroupsJson = """[{"id":1,"name":"Block A"},{"id":2,"name":"Staff room"}]""";

    // A pretend EduNotify that knows two templates and two groups, accepts sends and recalls, and refuses anything else.
    private static HttpResponseMessage Pretend(HttpRequestMessage request, string body)
    {
        var path = request.RequestUri!.PathAndQuery;
        if (request.Headers.Authorization?.Parameter != Key) return Json(HttpStatusCode.Unauthorized, """{"error":"That API key isn't recognised."}""");
        return path switch
        {
            "/api/v1/templates" => Json(HttpStatusCode.OK, TemplatesJson),
            "/api/v1/groups" => Json(HttpStatusCode.OK, GroupsJson),
            "/api/v1/messages" => Json(HttpStatusCode.Created, """{"id":812,"reference":"EH-1","online":37,"targeted":41}"""),
            _ when path.StartsWith("/api/v1/messages/recall") => Json(HttpStatusCode.OK, """{"recalled":[812]}"""),
            _ => Json(HttpStatusCode.NotFound, """{"error":"Nothing here."}""")
        };
    }

    private static (EduNotifyClient Client, Handler Handler, TestStore Test) Build(Func<HttpRequestMessage, string, HttpResponseMessage>? answer = null, bool setUp = true)
    {
        var test = new TestStore();
        if (setUp) Assert.True(test.Store.SetEduNotifySettings(Address, Key, false, true).Ok);
        var handler = new Handler(answer ?? Pretend);
        return (new EduNotifyClient(test.Store, new Factory(handler), NullLogger<EduNotifyClient>.Instance), handler, test);
    }

    private static HelpdeskStore.StaffNotice Notice(int number) =>
        new(number, 812, "3", "IT outage", false, ["Block A"], "Wi-Fi down in Block A", "Use a wired PC.", DateTime.UtcNow, "Tess Tech", 37, 41);

    [Theory]
    [InlineData("http://edunotify.school.local:5080", true, "http://edunotify.school.local:5080")]
    [InlineData(" https://edunotify.school.local:5443/ ", true, "https://edunotify.school.local:5443")]
    [InlineData("https://edunotify.school.local:5443/api/v1", true, "https://edunotify.school.local:5443")]
    [InlineData("192.168.1.20:5080", true, "http://192.168.1.20:5080")]
    [InlineData("https://school.example/edunotify/", true, "https://school.example/edunotify")]
    [InlineData("", false, "")]
    [InlineData("ftp://edunotify.school.local", false, "")]
    [InlineData("http://admin:pass@edunotify.school.local", false, "")]
    [InlineData("http://edunotify.school.local/?x=1", false, "")]
    public void The_address_is_a_web_address_on_the_school_network_or_beyond(string typed, bool accepted, string kept)
    {
        var (ok, address, problem) = EduNotify.CheckAddress(typed);
        Assert.Equal(accepted, ok);
        if (accepted) Assert.Equal(kept, address);
        else Assert.NotEmpty(problem);
    }

    [Theory]
    [InlineData(Key, true)]
    [InlineData("Bearer " + Key, true)]
    [InlineData("  " + Key + "  ", true)]
    [InlineData("", false)]
    [InlineData("SECRETKEY", false)]
    [InlineData("enk_with space", false)]
    public void Only_an_EduNotify_key_is_accepted(string typed, bool accepted)
    {
        var (ok, key, _) = EduNotify.CheckKey(typed);
        Assert.Equal(accepted, ok);
        if (accepted) Assert.Equal(Key, key);
    }

    [Fact]
    public void Sending_is_off_until_set_up_and_the_settings_and_notices_survive_a_restart()
    {
        using var test = new TestStore();
        Assert.False(test.Store.EduNotifySettings.Ready);

        // Ticking "on" with no key leaves it off.
        test.Store.SetEduNotifySettings(Address, null, false, true);
        Assert.False(test.Store.EduNotifySettings.Enabled);
        Assert.False(test.Store.SetEduNotifySettings(Address, "not a key", false, true).Ok);

        Assert.True(test.Store.SetEduNotifySettings(Address, Key, false, true).Ok);
        var requester = test.AddRequester("Ada Lovelace");
        var number = test.AddTicket(requester.Id, "Wi-Fi down in Block A");
        test.Store.RecordStaffNoticeSent(Notice(number));

        var store = test.Reopen();
        Assert.Equal(new HelpdeskStore.EduNotifySettingsView(Address, true, true), store.EduNotifySettings);
        Assert.Equal(Key, store.EduNotifyApiKey);
        var notice = store.StaffNoticeFor(number);
        Assert.NotNull(notice);
        Assert.Equal(["Block A"], notice.Groups);
        Assert.Equal(812, notice.MessageId);
        Assert.Contains(store.Tickets.Single(x => x.Number == number).History, x => x.Action == "Staff told through EduNotify");
        Assert.Null(store.CompareDatabaseWithMemory());

        // A blank key keeps the one saved; removing it switches sending off.
        store.SetEduNotifySettings(Address, "", false, true);
        Assert.Equal(Key, store.EduNotifyApiKey);
        store.SetEduNotifySettings(Address, null, removeKey: true, true);
        Assert.Equal("", test.Reopen().EduNotifyApiKey);
        Assert.False(test.Store.EduNotifySettings.Ready);
    }

    [Fact]
    public void The_key_is_never_written_to_the_audit_log_and_is_masked_in_the_raw_data_for_anyone_but_an_administrator()
    {
        using var test = new TestStore();
        test.Store.SetEduNotifySettings(Address, Key, false, true);

        var audit = test.Store.GetAuditEntries().Where(x => x.Entity == "EduNotify").ToList();
        Assert.NotEmpty(audit);
        Assert.DoesNotContain(audit, x => (x.Action + x.Details).Contains("SECRETKEY"));

        var raw = new RawDatabase(test.Store);
        var everything = new RawDatabase.Query(null, null, "equals", null, false);
        var hidden = raw.Read("Metadata", everything, showSecrets: false, page: 0)!;
        var key = hidden.Columns.ToList().FindIndex(x => x.Name == "Key");
        var value = hidden.Columns.ToList().FindIndex(x => x.Name == "Value");
        Assert.IsType<RawDatabase.Hidden>(hidden.Rows.Single(x => Equals(x[key], "EduNotifyApiKey"))[value]);
        Assert.Contains(hidden.Rows, x => Equals(x[key], "EduNotifyAddress") && Equals(x[value], Address));
        Assert.Equal(0, raw.Read("Metadata", everything with { Search = "SECRETKEY" }, showSecrets: false, page: 1)!.Total);
    }

    [Fact]
    public async Task Testing_the_connection_lists_what_the_key_may_send_and_never_offers_an_emergency_template()
    {
        var (client, handler, test) = Build();
        using var _ = test;
        var outcome = await client.RefreshAsync(CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Contains("1 template", outcome.Message);
        Assert.All(handler.Requests, x => Assert.Equal("Bearer " + Key, x.Auth));
        var catalogue = test.Store.EduNotifyCatalogue!;
        Assert.Equal(2, catalogue.Templates.Count);
        Assert.Equal(["IT outage"], catalogue.Sendable.Select(x => x.Name));
        Assert.Equal(["Block A", "Staff room"], catalogue.Groups.Select(x => x.Name));
        Assert.Equal("3", catalogue.Templates[0].Id);
    }

    [Fact]
    public async Task A_refused_key_is_reported_in_words_without_the_key()
    {
        var (client, _, test) = Build((_, _) => Json(HttpStatusCode.Unauthorized, $$"""{"error":"That API key ({{Key}}) isn't recognised."}"""));
        using var _ = test;
        var outcome = await client.RefreshAsync(CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.StartsWith("EduNotify didn't accept the API key.", outcome.Message);
        Assert.DoesNotContain("SECRETKEY", outcome.Message);
        Assert.Null(test.Store.EduNotifyCatalogue);
        Assert.False(test.Store.EduNotifyLastAttempt!.Ok);
    }

    [Fact]
    public async Task An_unreachable_EduNotify_is_reported_without_the_key()
    {
        var (client, _, test) = Build((_, _) => throw new HttpRequestException($"Connection refused sending {Key}"));
        using var _ = test;
        var outcome = await client.RecallAsync(5, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.StartsWith("EduNotify couldn't be reached", outcome.Message);
        Assert.DoesNotContain("SECRETKEY", outcome.Message);
    }

    [Fact]
    public async Task A_send_carries_the_ticket_reference_and_only_the_fields_filled_in()
    {
        var (client, handler, test) = Build();
        using var _ = test;
        var (outcome, sent) = await client.SendAsync(new EduNotify.Send("3", false, ["Block A"], "Wi-Fi down in Block A", "  ", EduNotify.ReferenceFor(1042)), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal(new EduNotify.Sent(812, 37, 41), sent);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://edunotify.school.local:5080/api/v1/messages"), request.Uri);
        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal(3, root.GetProperty("template").GetInt32());
        Assert.Equal("EH-1042", root.GetProperty("reference").GetString());
        Assert.Equal("Wi-Fi down in Block A", root.GetProperty("title").GetString());
        // A blank message is left out, so EduNotify keeps the template's rather than sending nothing.
        Assert.False(root.TryGetProperty("body", out var body));
        Assert.Equal("Block A", root.GetProperty("recipients").GetProperty("groups")[0].GetString());

        var everyone = EduNotify.SendJson(new EduNotify.Send("3", true, ["Block A"], null, null, "EH-1"));
        Assert.Contains("\"everyone\":true", everyone);
        Assert.DoesNotContain("groups", everyone);
    }

    [Fact]
    public async Task EduNotify_refusing_a_send_comes_back_with_its_own_words()
    {
        var (client, _, test) = Build((_, _) => Json(HttpStatusCode.NotFound, """{"error":"There's no group called \"Block Z\"."}"""));
        using var _ = test;
        var (outcome, sent) = await client.SendAsync(new EduNotify.Send("3", false, ["Block Z"], null, null, "EH-1"), CancellationToken.None);

        Assert.Null(sent);
        Assert.False(outcome.Ok);
        Assert.Contains("There's no group called \"Block Z\".", outcome.Message);
    }

    [Fact]
    public async Task Closing_or_deleting_a_ticket_recalls_its_notice_and_nothing_else()
    {
        var (client, handler, test) = Build();
        using var _ = test;
        var requester = test.AddRequester("Ada Lovelace");
        var open = test.AddTicket(requester.Id, "Printer offline");
        var closing = test.AddTicket(requester.Id, "Wi-Fi down");
        var deleting = test.AddTicket(requester.Id, "MIS down");
        foreach (var number in new[] { open, closing, deleting }) test.Store.RecordStaffNoticeSent(Notice(number));

        await client.RecallClosedAsync(CancellationToken.None);
        Assert.Empty(handler.Requests);

        var ticket = test.Store.Tickets.Single(x => x.Number == closing);
        test.Store.UpdateTicket(ticket with { Status = "Closed", ClosedAt = DateTime.UtcNow });
        test.Store.DeleteTicket(deleting);
        Assert.Equal([closing, deleting], test.Store.StaffNoticesToRecall().Order());

        await client.RecallClosedAsync(CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, x => x.Uri!.PathAndQuery == $"/api/v1/messages/recall?reference=EH-{closing}");
        Assert.Equal([open], test.Store.StaffNotices.Select(x => x.TicketNumber));
        Assert.Contains(test.Store.Tickets.Single(x => x.Number == closing).History, x => x.Action == "Staff notice recalled" && x.Details.Contains("when the ticket was closed"));
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public async Task A_notice_already_gone_from_EduNotify_counts_as_recalled_but_an_unreachable_one_is_kept_to_try_again()
    {
        var down = true;
        var (client, _, test) = Build((request, body) => down
            ? throw new HttpRequestException("down")
            : Json(HttpStatusCode.NotFound, """{"error":"This API key has no live message like that to recall."}"""));
        using var _ = test;
        var requester = test.AddRequester("Ada Lovelace");
        var number = test.AddTicket(requester.Id, "Wi-Fi down");
        test.Store.RecordStaffNoticeSent(Notice(number));
        var ticket = test.Store.Tickets.Single(x => x.Number == number);
        test.Store.UpdateTicket(ticket with { Status = "Closed", ClosedAt = DateTime.UtcNow });

        await client.RecallClosedAsync(CancellationToken.None);
        Assert.NotNull(test.Store.StaffNoticeFor(number));

        down = false;
        await client.RecallClosedAsync(CancellationToken.None);
        Assert.Null(test.Store.StaffNoticeFor(number));
    }

    // ---- The technician ping ----

    private static readonly EduNotify.Catalogue PingCatalogue = new(DateTime.UtcNow,
        [new("3", "IT outage", "editable", false, true, true, "IT problem", ""), new("4", "Technician ping", "editable", false, true, true, "New ticket", "")],
        [new("1", "Block A"), new("9", "Technicians")]);

    private static void SwitchPingOn(TestStore test)
    {
        test.Store.SetEduNotifyCatalogue(PingCatalogue);
        var (ok, message) = test.Store.SetTechnicianPingSettings(true, ["Urgent"], "4", ["Technicians"]);
        Assert.True(ok, message);
    }

    private static int AddUrgent(TestStore test, Guid requesterId, string title = "Ada's whiteboard is on fire") =>
        test.Store.AddTicket(new TicketRecord(0, title, "Smoke everywhere.", requesterId, [], null, "Urgent", "Open", test.Store.Categories[0], DateTime.UtcNow, null));

    private static void Change(TestStore test, int number, Func<TicketRecord, TicketRecord> change) =>
        Assert.True(test.Store.UpdateTicket(change(test.Store.Tickets.Single(x => x.Number == number))));

    private static List<(HttpMethod Method, Uri? Uri, string? Auth, string Body)> Pings(Handler handler) =>
        handler.Requests.Where(x => x.Body.Contains("-ping") || x.Uri!.Query.Contains("-ping")).ToList();

    [Fact]
    public void The_ping_needs_a_priority_a_template_and_a_group_the_key_can_use_and_survives_a_restart()
    {
        using var test = new TestStore();
        Assert.False(test.Store.TechnicianPingSettings.Active);
        // Nothing fetched from EduNotify yet, so there's no template to choose.
        Assert.False(test.Store.SetTechnicianPingSettings(true, ["Urgent"], "4", ["Technicians"]).Ok);

        test.Store.SetEduNotifyCatalogue(PingCatalogue);
        Assert.False(test.Store.SetTechnicianPingSettings(true, ["Urgent"], "4", []).Ok);
        Assert.False(test.Store.SetTechnicianPingSettings(true, [], "4", ["Technicians"]).Ok);
        Assert.False(test.Store.SetTechnicianPingSettings(true, ["Urgent"], "4", ["Nowhere"]).Ok);
        Assert.False(test.Store.SetTechnicianPingSettings(true, ["Urgent"], "99", ["Technicians"]).Ok);
        // A priority that isn't one of the helpdesk's is dropped; the casing comes from the list.
        Assert.True(test.Store.SetTechnicianPingSettings(true, ["urgent", "Made up"], "4", ["technicians"]).Ok);
        Assert.Contains(test.Store.GetAuditEntries(), x => x.Entity == "EduNotify" && x.Action == "Technician ping on");

        var store = test.Reopen();
        var settings = store.TechnicianPingSettings;
        Assert.True(settings.Active);
        Assert.Equal(["Urgent"], settings.Priorities);
        Assert.Equal(("4", "Technician ping"), (settings.TemplateId, settings.TemplateName));
        Assert.Equal(["Technicians"], settings.Groups);
        // After a restart nothing has been fetched, but what's saved can still be saved again.
        store.AddTicketOption("Priority", "Spare");
        Assert.True(store.SetTechnicianPingSettings(true, ["Urgent", "Spare"], "4", ["Technicians"]).Ok);

        // Renaming a priority carries the tick across; deleting one takes it away.
        store.UpdateTicketOption("Priority", "Urgent", "Critical");
        Assert.Equal(["Critical", "Spare"], store.TechnicianPingSettings.Priorities);
        Assert.Equal("Priority deleted.", store.DeleteTicketOption("Priority", "Spare"));
        Assert.Equal(["Critical"], test.Reopen().TechnicianPingSettings.Priorities);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public async Task A_new_urgent_ticket_pings_the_technicians_once_with_its_own_reference_and_nothing_personal()
    {
        var (client, handler, test) = Build();
        using var _ = test;
        var requester = test.AddRequester("Ada Lovelace");
        var waiting = AddUrgent(test, requester.Id, "Already waiting");
        SwitchPingOn(test);
        var normal = test.AddTicket(requester.Id, "Mouse sticky");

        // Tickets already waiting when the ping went on, and tickets at other priorities, don't ping.
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Empty(handler.Requests);

        var urgent = AddUrgent(test, requester.Id);
        test.Store.RecordStaffNoticeSent(Notice(urgent));
        await client.TechnicianPingsAsync(CancellationToken.None);
        await client.TechnicianPingsAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal(4, root.GetProperty("template").GetInt32());
        Assert.Equal($"EH-{urgent}-ping", root.GetProperty("reference").GetString());
        Assert.Equal($"Urgent ticket #{urgent}", root.GetProperty("title").GetString());
        Assert.Equal("Technicians", root.GetProperty("recipients").GetProperty("groups")[0].GetString());
        Assert.DoesNotContain("Ada", request.Body);
        Assert.DoesNotContain("whiteboard", request.Body);
        Assert.Equal([urgent], test.Store.TechnicianPings.Select(x => x.TicketNumber));
        Assert.Contains(test.Store.Tickets.Single(x => x.Number == urgent).History, x => x.Action == "Technicians pinged through EduNotify");
        // The ticket's staff notice is a different message and stays.
        Assert.NotNull(test.Store.StaffNoticeFor(urgent));

        // Moving a ticket up to a ticked priority pings it too.
        Change(test, normal, x => x with { Priority = "Urgent" });
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Equal($"EH-{normal}-ping", JsonDocument.Parse(handler.Requests[^1].Body).RootElement.GetProperty("reference").GetString());
        Assert.DoesNotContain(test.Store.TechnicianPings, x => x.TicketNumber == waiting);

        // A restart neither pings again nor forgets what is showing.
        var store = test.Reopen();
        Assert.Empty(store.PlanTechnicianPings(true).Send);
        Assert.Equal(2, store.TechnicianPings.Count);
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public async Task The_ping_comes_down_when_the_ticket_is_assigned_closed_deleted_or_moved_down_and_not_before()
    {
        var (client, handler, test) = Build();
        using var _ = test;
        var technician = new TechnicianRecord(Guid.NewGuid(), "Tess Tech", "tess@test.example", test.Store.TechnicianTeams[0]);
        test.Store.AddTechnician(technician);
        var requester = test.AddRequester("Ada Lovelace");
        SwitchPingOn(test);
        var assigned = AddUrgent(test, requester.Id);
        var closed = AddUrgent(test, requester.Id);
        var deleted = AddUrgent(test, requester.Id);
        var downgraded = AddUrgent(test, requester.Id);
        var teamOnly = AddUrgent(test, requester.Id);
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Equal(5, test.Store.TechnicianPings.Count);
        handler.Requests.Clear();

        // A team without a technician is still nobody picking it up.
        Change(test, teamOnly, x => x with { TeamName = test.Store.TechnicianTeams[0] });
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Empty(handler.Requests);

        Change(test, assigned, x => x with { TechnicianId = technician.Id });
        Change(test, closed, x => x with { Status = "Closed", ClosedAt = DateTime.UtcNow });
        test.Store.DeleteTicket(deleted);
        Change(test, downgraded, x => x with { Priority = "Normal" });
        await client.TechnicianPingsAsync(CancellationToken.None);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, x => Assert.StartsWith("/api/v1/messages/recall?reference=EH-", x.Uri!.PathAndQuery));
        Assert.Contains(handler.Requests, x => x.Uri!.Query == $"?reference=EH-{assigned}-ping");
        Assert.Equal([teamOnly], test.Store.TechnicianPings.Select(x => x.TicketNumber));
        string Recalled(int number) => test.Store.Tickets.Single(x => x.Number == number).History.Last(x => x.Action == "Technician ping recalled").Details;
        Assert.Contains("assigned to Tess Tech", Recalled(assigned));
        Assert.Contains("closed", Recalled(closed));
        Assert.Contains("priority changed to Normal", Recalled(downgraded));

        // Unassigned again while still urgent, it pings again.
        handler.Requests.Clear();
        Change(test, assigned, x => x with { TechnicianId = null });
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Equal($"EH-{assigned}-ping", JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement.GetProperty("reference").GetString());

        // Switching the ping off takes down what is showing.
        handler.Requests.Clear();
        Assert.True(test.Store.SetTechnicianPingSettings(false, ["Urgent"], "4", ["Technicians"]).Ok);
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Equal(2, Pings(handler).Count);
        Assert.Empty(test.Store.TechnicianPings);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public async Task Tickets_arriving_while_sending_is_off_are_not_pinged_later_and_a_failed_ping_is_tried_again()
    {
        var down = false;
        var (client, handler, test) = Build((request, body) => down ? throw new HttpRequestException("down") : Pretend(request, body));
        using var _ = test;
        var requester = test.AddRequester("Ada Lovelace");
        SwitchPingOn(test);

        Assert.True(test.Store.SetEduNotifySettings(Address, null, false, enabled: false).Ok);
        var whileOff = AddUrgent(test, requester.Id);
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.True(test.Store.SetEduNotifySettings(Address, null, false, enabled: true).Ok);
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Empty(handler.Requests);

        down = true;
        var later = AddUrgent(test, requester.Id);
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Empty(test.Store.TechnicianPings);
        down = false;
        await client.TechnicianPingsAsync(CancellationToken.None);
        Assert.Equal([later], test.Store.TechnicianPings.Select(x => x.TicketNumber));
        Assert.DoesNotContain(handler.Requests, x => x.Body.Contains($"EH-{whileOff}-ping"));
    }

    [Fact]
    public async Task With_nothing_set_up_nothing_is_called()
    {
        var (client, handler, test) = Build(setUp: false);
        using var _ = test;
        var outcome = await client.RefreshAsync(CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Empty(handler.Requests);
        Assert.Equal("No EduNotify address is set.", outcome.Message);
    }
}
