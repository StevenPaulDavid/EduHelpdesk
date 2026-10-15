using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace EduHelpdesk.Services;

// Calls EduNotify's sending API (Settings → EduNotify): what the key may send and to which groups, sending a ticket's
// notice, and recalling it. In the background it takes down the notice of any ticket that has been closed or deleted, and
// keeps the list of templates and groups fresh for the ticket page. Every answer comes back as words the page can show,
// with the API key kept out of them; nothing here throws except when cancelled, and the helpdesk never depends on
// EduNotify being there.
public sealed class EduNotifyClient(HelpdeskStore store, IHttpClientFactory clients, ILogger<EduNotifyClient> logger, TimeSpan? recallInterval = null) : BackgroundService
{
    public const string ClientName = "edunotify";
    private static readonly TimeSpan CatalogueInterval = TimeSpan.FromMinutes(10);
    // How soon a closed ticket's notice comes down. Looking is cheap: it only reads the few notices showing.
    private readonly TimeSpan _recallInterval = recallInterval ?? TimeSpan.FromSeconds(15);

    // One call's outcome: whether it worked, the words to show, and for a recall whether EduNotify said there was nothing
    // live to take down (which counts as done - someone may have recalled it from the console).
    public sealed record Outcome(bool Ok, string Message, bool NothingLive = false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCatalogue = DateTime.MinValue;
        using var timer = new PeriodicTimer(_recallInterval);
        do
        {
            try
            {
                if (store.EduNotifySettings.Ready)
                {
                    await RecallClosedAsync(stoppingToken);
                    if (DateTime.UtcNow >= nextCatalogue)
                    {
                        await RefreshAsync(stoppingToken);
                        nextCatalogue = DateTime.UtcNow + CatalogueInterval;
                    }
                }
                else nextCatalogue = DateTime.MinValue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // Never let one bad pass stop the checks - the next one tries again.
                logger.LogError(ex, "The EduNotify check hit an error");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }

    // Takes down the notice of every ticket that has been closed or deleted. One that can't be recalled now (EduNotify
    // down) is left for the next pass.
    public async Task RecallClosedAsync(CancellationToken cancel)
    {
        foreach (var number in store.StaffNoticesToRecall())
        {
            var outcome = await RecallAsync(number, cancel);
            if (outcome.Ok) store.RecordStaffNoticeRecalled(number, outcome.NothingLive ? "after the ticket was closed (it had already gone from EduNotify)" : "when the ticket was closed");
            else logger.LogWarning("The EduNotify notice for closed ticket #{Number} couldn't be recalled yet: {Message}", number, outcome.Message);
        }
    }

    // Asks EduNotify what the key may send and the groups it can go to, keeps the answer for the ticket page, and says how
    // it went. The settings page's Test button.
    public async Task<Outcome> RefreshAsync(CancellationToken cancel)
    {
        var templates = await GetAsync("templates", cancel);
        if (!templates.Ok)
        {
            store.RecordEduNotifyAttempt(false, templates.Message);
            return new(false, templates.Message);
        }
        var groups = await GetAsync("groups", cancel);
        if (!groups.Ok)
        {
            store.RecordEduNotifyAttempt(false, groups.Message);
            return new(false, groups.Message);
        }
        if (EduNotify.ParseTemplates(templates.Body) is not { } templateList || EduNotify.ParseGroups(groups.Body) is not { } groupList)
        {
            const string odd = "EduNotify answered, but not in the way expected. Check the address is EduNotify's and that it's up to date.";
            store.RecordEduNotifyAttempt(false, odd);
            return new(false, odd);
        }
        var catalogue = new EduNotify.Catalogue(DateTime.UtcNow, templateList, groupList);
        store.SetEduNotifyCatalogue(catalogue);
        var sendable = catalogue.Sendable.Count;
        var message = sendable == 0
            ? "Connected, but the key isn't allowed any templates the helpdesk can use. In EduNotify, give the key an outage template under Settings → Integrations."
            : $"Connected. The key can send {sendable} template{(sendable == 1 ? "" : "s")} to {groupList.Count} group{(groupList.Count == 1 ? "" : "s")}.";
        store.RecordEduNotifyAttempt(true, message);
        return new(sendable > 0, message);
    }

    // Sends (or, with the same reference, replaces) a notice. The answer's id and PC counts come back with it.
    public async Task<(Outcome Outcome, EduNotify.Sent? Sent)> SendAsync(EduNotify.Send send, CancellationToken cancel)
    {
        var (status, body, problem) = await CallAsync(HttpMethod.Post, "messages", EduNotify.SendJson(send), cancel);
        if (problem is not null) return Fail(problem);
        if (status != HttpStatusCode.Created && status != HttpStatusCode.OK) return Fail(Refusal(status, body));
        if (EduNotify.ParseSent(body) is not { } sent) return Fail("EduNotify answered, but not in the way expected, so it isn't clear whether the notice went.");
        store.RecordEduNotifyAttempt(true, $"Sent \"{send.Reference}\".");
        return (new(true, $"Shown on {sent.Online} of {sent.Targeted} PCs straight away."), sent);

        (Outcome, EduNotify.Sent?) Fail(string why)
        {
            store.RecordEduNotifyAttempt(false, why);
            return (new(false, why), null);
        }
    }

    // Takes down whatever the ticket has showing. EduNotify finding nothing live counts as done.
    public async Task<Outcome> RecallAsync(int ticketNumber, CancellationToken cancel)
    {
        var reference = Uri.EscapeDataString(EduNotify.ReferenceFor(ticketNumber));
        var (status, body, problem) = await CallAsync(HttpMethod.Post, $"messages/recall?reference={reference}", null, cancel);
        Outcome outcome = problem is not null ? new(false, problem)
            : status == HttpStatusCode.OK ? new(true, "Recalled.")
            : status == HttpStatusCode.NotFound ? new(true, "It had already gone from EduNotify.", NothingLive: true)
            : new(false, Refusal(status, body));
        store.RecordEduNotifyAttempt(outcome.Ok, outcome.Ok ? $"Recalled the notice for #{ticketNumber}." : outcome.Message);
        return outcome;
    }

    private async Task<(bool Ok, string Body, string Message)> GetAsync(string path, CancellationToken cancel)
    {
        var (status, body, problem) = await CallAsync(HttpMethod.Get, path, null, cancel);
        if (problem is not null) return (false, "", problem);
        return status == HttpStatusCode.OK ? (true, body, "") : (false, "", Refusal(status, body));
    }

    // One request to {address}/api/v1/{path} with the key. Returns EduNotify's status and body, or a problem in words when
    // it couldn't be asked at all.
    private async Task<(HttpStatusCode Status, string Body, string? Problem)> CallAsync(HttpMethod method, string path, string? json, CancellationToken cancel)
    {
        var settings = store.EduNotifySettings;
        var key = store.EduNotifyApiKey;
        if (!settings.HasAddress) return (0, "", "No EduNotify address is set.");
        if (key.Length == 0) return (0, "", "No EduNotify API key is set.");
        var client = clients.CreateClient(ClientName);
        try
        {
            using var request = new HttpRequestMessage(method, $"{settings.Address}/api/v1/{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, cancel);
            // EduNotify never echoes the key, but nothing it says can carry the key onwards to a page or a log.
            var body = (await response.Content.ReadAsStringAsync(cancel)).Replace(key, "[API key]", StringComparison.Ordinal);
            return (response.StatusCode, body, null);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { return (0, "", "EduNotify didn't answer in time."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return (0, "", $"EduNotify couldn't be reached at {settings.Address}: {Scrub(ex.Message, key)}");
        }
    }

    // EduNotify's own sentence where it gave one, which is the best clue there is, with what the status means around it.
    private static string Refusal(HttpStatusCode status, string body)
    {
        var said = Scrub(EduNotify.ParseError(body), null);
        var meaning = status switch
        {
            HttpStatusCode.Unauthorized => "EduNotify didn't accept the API key.",
            HttpStatusCode.Forbidden => "EduNotify refused: the key isn't allowed to do that.",
            HttpStatusCode.NotFound => "EduNotify couldn't find that.",
            HttpStatusCode.TooManyRequests => "Too many notices have been sent in the last minute. Try again shortly.",
            HttpStatusCode.BadRequest => "EduNotify didn't accept the notice.",
            _ => $"EduNotify answered {(int)status} {status}."
        };
        return said.Length == 0 || status == HttpStatusCode.TooManyRequests ? meaning : $"{meaning} It said: {said}";
    }

    // Takes the key out of anything about to be shown or logged.
    private static string Scrub(string text, string? key)
    {
        var line = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (!string.IsNullOrEmpty(key)) line = line.Replace(key, "[API key]", StringComparison.Ordinal);
        return line.Length > 300 ? line[..300] + "…" : line;
    }
}
