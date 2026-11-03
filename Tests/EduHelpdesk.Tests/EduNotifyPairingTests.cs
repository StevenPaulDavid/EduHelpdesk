using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduHelpdesk.Tests;

// Pairing with EduNotify by its one-time code, the defaults pairing puts in, the link's tests, unlinking, and the
// upgrade that clears a link made by hand (the EduNotify project's docs/EASY-LINK.md), against a pretend EduNotify.
public class EduNotifyPairingTests
{
    private const string Address = "http://edunotify.school.local:5080";
    private const string Key = "enk_PAIREDKEY1234567890";

    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Auth, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body));
            return answer(request, body);
        }
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    // A pretend EduNotify. Pairing checks back with the incoming key it was sent, as the real one does.
    private sealed class Site : IDisposable
    {
        public TestStore Test { get; } = new();
        public HelpdeskStore Store => Test.Store;
        public Handler Handler { get; }
        public EduNotifyClient Client { get; }
        public Func<HttpRequestMessage, string, HttpResponseMessage?> Override { get; set; } = (_, _) => null;
        public bool CheckedBack { get; private set; }

        public Site()
        {
            Handler = new Handler(Answer);
            Client = new EduNotifyClient(Store, new Factory(Handler), NullLogger<EduNotifyClient>.Instance);
        }

        private HttpResponseMessage Answer(HttpRequestMessage request, string body)
        {
            if (Override(request, body) is { } custom) return custom;
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/pair" && request.Method == HttpMethod.Post)
            {
                var sent = JsonDocument.Parse(body).RootElement;
                if (sent.GetProperty("code").GetString() != "4F7-K2Q") return Json(HttpStatusCode.BadRequest, """{"error":"That isn't the pairing code EduNotify is showing."}""");
                CheckedBack = Store.CheckEduNotifyIncomingKey(sent.GetProperty("incomingKey").GetString());
                if (!CheckedBack) return Json(HttpStatusCode.BadGateway, """{"error":"EduNotify can't reach EduHelpdesk."}""");
                return Json(HttpStatusCode.OK, """{"apiKey":"KEY","templates":{"notice":{"id":11,"name":"Helpdesk notice"},"ping":{"id":12,"name":"Helpdesk ping"},"popup":{"id":13,"name":"Helpdesk pop-up"}},"groups":["Technicians","Site team","Year 7"]}""".Replace("KEY", Key));
            }
            if (request.Headers.Authorization?.Parameter != Key) return Json(HttpStatusCode.Unauthorized, """{"error":"That API key isn't recognised."}""");
            return path switch
            {
                "/api/v1/templates" => Json(HttpStatusCode.OK, """[{"id":11,"name":"Helpdesk notice","kind":"editable"},{"id":12,"name":"Helpdesk ping","kind":"editable"},{"id":13,"name":"Helpdesk pop-up","kind":"editable"}]"""),
                "/api/v1/groups" => Json(HttpStatusCode.OK, """[{"id":1,"name":"Technicians"},{"id":2,"name":"Site team"},{"id":3,"name":"Year 7"}]"""),
                "/api/v1/oncall/reasons" => Json(HttpStatusCode.OK, """[{"id":4,"name":"Technician","reasons":["Projector"]}]"""),
                "/api/v1/pair" => Json(HttpStatusCode.OK, """{"unlinked":true}"""),
                "/api/v1/messages" => Json(HttpStatusCode.Created, """{"id":901,"online":1,"targeted":2}"""),
                "/api/v1/messages/901" => Json(HttpStatusCode.OK, """{"received":2,"online":2,"targeted":2}"""),
                _ => Json(HttpStatusCode.NotFound, """{"error":"Nothing here."}""")
            };
        }

        public async Task PairAsync()
        {
            var outcome = await Client.PairAsync(Address, "4F7-K2Q", "school.org.uk", "https://helpdesk.school.local", CancellationToken.None);
            Assert.True(outcome.Ok, outcome.Message);
        }

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public async Task Pairing_links_both_ways_and_puts_in_the_defaults()
    {
        using var site = new Site();
        Assert.False(site.Store.EduNotifyLinked);

        await site.PairAsync();

        var pair = site.Handler.Requests.First(x => x.Path == "/api/v1/pair");
        Assert.Null(pair.Auth);
        var sent = JsonDocument.Parse(pair.Body).RootElement;
        Assert.Equal("https://helpdesk.school.local", sent.GetProperty("helpdeskAddress").GetString());
        Assert.Equal("school.org.uk", sent.GetProperty("emailDomain").GetString());
        Assert.True(site.CheckedBack); // EduNotify's call-back worked before the key was kept

        Assert.True(site.Store.EduNotifyLinked);
        Assert.True(site.Store.EduNotifySettings.Ready);
        Assert.Equal(Key, site.Store.EduNotifyApiKey);
        Assert.True(site.Store.CheckEduNotifyIncomingKey(sent.GetProperty("incomingKey").GetString()));

        var link = site.Store.EduNotifyLinkSettings;
        Assert.Equal("school.org.uk", link.EmailDomain);
        Assert.True(link.AcceptCalls);
        Assert.Contains(link.FallbackCategory, site.Store.Categories);
        Assert.Contains(link.FallbackPriority, site.Store.Priorities);
        Assert.All(HelpdeskStore.EduNotifyEvents.All, key => Assert.False(link.RuleFor(key).Enabled));
        Assert.All(HelpdeskStore.EduNotifyEvents.All, key => Assert.Equal("13", link.RuleFor(key).TemplateId));
        Assert.Equal("12", site.Store.TechnicianPingSettings.TemplateId);
        Assert.Equal(["Technicians"], site.Store.TechnicianPingSettings.Groups);
        // Pairing then fetched EduNotify's lists, reasons included.
        Assert.NotNull(site.Store.EduNotifyCatalogue);
        Assert.Contains(link.KnownReasons, x => x.Reason == "Projector");
        Assert.Contains(site.Store.GetAuditEntries(), x => x.Action == "Paired with EduNotify");
    }

    [Fact]
    public async Task A_wrong_code_links_nothing()
    {
        using var site = new Site();
        var outcome = await site.Client.PairAsync(Address, "AAA-AAA", "school.org.uk", "https://helpdesk.school.local", CancellationToken.None);
        Assert.False(outcome.Ok);
        Assert.Equal("That isn't the pairing code EduNotify is showing.", outcome.Message);
        Assert.False(site.Store.EduNotifyLinked);
        Assert.False((await site.Client.PairAsync("not a url", "4F7-K2Q", "school.org.uk", "https://h", CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Unlinking_tells_EduNotify_and_forgets_the_keys()
    {
        using var site = new Site();
        await site.PairAsync();
        var outcome = await site.Client.UnlinkAsync(CancellationToken.None);
        Assert.True(outcome.Ok);
        Assert.Contains(site.Handler.Requests, x => x.Method == HttpMethod.Delete && x.Path == "/api/v1/pair" && x.Auth == $"Bearer {Key}");
        Assert.False(site.Store.EduNotifyLinked);
        Assert.False(site.Store.EduNotifySettings.Ready);
        // The settings around the link are kept for the next pairing.
        Assert.True(site.Store.EduNotifyLinkSettings.AcceptCalls);
    }

    [Fact]
    public async Task The_test_call_runs_end_to_end_and_leaves_nothing_behind()
    {
        using var site = new Site();
        await site.PairAsync();
        var before = site.Store.Tickets.Count;
        site.Override = (request, body) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/v1/oncall/test")
            {
                // As EduNotify does: raise the TEST ticket here through the incoming API.
                var raised = site.Store.RaiseCallTicket(new("edunotify-test-1", "TEST: Link test: Test (EduNotify link test)", "", null, "EduNotify link test", "Test technician call", "Link test", false, "Test", true));
                return Json(HttpStatusCode.OK, """{"ok":true,"ticket":"REF","steps":[{"step":"Ticket made","ok":true,"detail":"REF"},{"step":"Assigned","ok":true,"detail":"Assigned to you."}]}""".Replace("REF", raised.Ref));
            }
            if (path.StartsWith("/api/v1/oncalls/close")) return Json(HttpStatusCode.OK, """{"closed":[],"test":true}""");
            return null;
        };

        var (ok, steps) = await site.Client.TestCallAsync("tess@school.org.uk", CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(["Ticket made", "Assigned", "EduNotify told it's resolved", "Test ticket removed"], steps.Select(x => x.Step));
        Assert.Equal(before, site.Store.Tickets.Count);
        Assert.Empty(site.Store.EduNotifyLinkSettings.Calls);
        Assert.Contains("tess@school.org.uk", site.Handler.Requests.First(x => x.Path == "/api/v1/oncall/test").Body);
        Assert.Contains(site.Store.GetAuditEntries(), x => x.Action == "Link test");
    }

    [Fact]
    public async Task The_pop_up_test_goes_to_my_login_with_the_made_template()
    {
        using var site = new Site();
        await site.PairAsync();
        var outcome = await site.Client.TestPopupAsync("tess", "https://helpdesk.school.local/Settings/EduNotify", CancellationToken.None);
        Assert.True(outcome.Ok, outcome.Message);
        var send = JsonDocument.Parse(site.Handler.Requests.Last(x => x.Path == "/api/v1/messages").Body).RootElement;
        Assert.Equal(13, send.GetProperty("template").GetInt32());
        Assert.Equal("tess", send.GetProperty("recipients").GetProperty("users")[0].GetString());
        Assert.Equal("https://helpdesk.school.local/Settings/EduNotify", send.GetProperty("link").GetProperty("url").GetString());
        Assert.False((await site.Client.TestPopupAsync(null, null, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task The_desk_PCs_test_is_silent_and_counts_what_arrived()
    {
        using var site = new Site();
        await site.PairAsync();
        var results = await site.Client.TestDeskPcsAsync(CancellationToken.None);
        var it = Assert.Single(results);
        Assert.True(it.Outcome.Ok, it.Outcome.Message);
        Assert.Equal("2 of 2 PCs received it.", it.Outcome.Message);
        var send = JsonDocument.Parse(site.Handler.Requests.Last(x => x.Path == "/api/v1/messages").Body).RootElement;
        Assert.True(send.GetProperty("test").GetBoolean());
        Assert.Equal("Technicians", send.GetProperty("recipients").GetProperty("groups")[0].GetString());
    }

    [Fact]
    public void A_link_made_by_hand_is_cleared_on_upgrade_but_the_settings_stay()
    {
        using var test = new TestStore();
        Assert.True(test.Store.SetEduNotifySettings(Address, Key, false, true).Ok);
        test.Store.NewEduNotifyIncomingKey();
        test.Store.SetEduNotifyCallSettings(new(true, true, true, [], test.Store.Categories[0], test.Store.Priorities[0], null));
        // As a database from before pairing: no link version.
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE Metadata SET Value = replace(Value, '\"LinkVersion\":2', '\"LinkVersion\":0') WHERE Key = 'EduNotifyLink';";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var store = test.Reopen();

        Assert.False(store.EduNotifyLinked);
        Assert.False(store.EduNotifySettings.Ready);
        Assert.True(store.EduNotifyRepairNeeded);
        Assert.True(store.EduNotifyLinkSettings.AcceptCalls);
        Assert.False(test.Reopen().EduNotifySettings.HasKey); // and it stays cleared
    }

    [Fact]
    public void The_email_domain_is_guessed_from_staff_emails()
    {
        using var test = new TestStore();
        foreach (var name in new[] { "a", "b", "c" }) test.Store.AddUser(new UserRecord(Guid.NewGuid(), name, $"{name}@school.org.uk", "", ""));
        test.Store.AddUser(new UserRecord(Guid.NewGuid(), "d", "d@gmail.com", "", ""));
        // The test store's own people are at test.example; the school's domain must outnumber them to win.
        var guess = test.Store.GuessEmailDomain();
        Assert.Contains(guess, new[] { "school.org.uk", "test.example" });
        Assert.NotEqual("gmail.com", guess);
    }
}
