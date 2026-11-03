using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace EduHelpdesk.Services;

// Calls EduNotify's sending API (Settings → EduNotify): what the key may send and to which groups, sending a ticket's
// notice, and recalling it. In the background it takes down the notice of any ticket that has been closed or deleted,
// sends and takes down the technician ping, and keeps the list of templates and groups fresh for the ticket page. Every answer comes back as words the page can show,
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
                await TechnicianPingsAsync(stoppingToken);
                await EventsAsync(stoppingToken);
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
        await RefreshReasonsAsync(cancel);
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
    public Task<Outcome> RecallAsync(int ticketNumber, CancellationToken cancel) =>
        RecallReferenceAsync(EduNotify.ReferenceFor(ticketNumber), $"the notice for #{ticketNumber}", cancel);

    // The technician ping (HelpdeskStore.TechnicianPing): pings tickets that have come in at a chosen priority with nobody
    // assigned, and takes down the pings of tickets that have since been picked up, closed, deleted or moved down. Pings
    // that fail are tried again next pass. Called every pass whether or not sending is on, so that while it is off the
    // tickets arriving are only noted rather than saved up to ping later.
    public async Task TechnicianPingsAsync(CancellationToken cancel)
    {
        var sending = store.EduNotifySettings.Ready;
        var plan = store.PlanTechnicianPings(sending);
        if (!sending) return;
        foreach (var number in plan.Recall)
        {
            var outcome = await RecallReferenceAsync(EduNotify.PingReferenceFor(number), $"the technician ping for #{number}", cancel);
            if (outcome.Ok) store.RecordTechnicianPingRecalled(number, outcome.NothingLive);
            else logger.LogWarning("The EduNotify technician ping for ticket #{Number} couldn't be recalled yet: {Message}", number, outcome.Message);
        }
        foreach (var (number, send) in plan.Send)
        {
            var (outcome, sent) = await SendAsync(send, cancel);
            if (sent is not null) store.RecordTechnicianPingSent(number, send, sent);
            else logger.LogWarning("The EduNotify technician ping for ticket #{Number} couldn't be sent yet: {Message}", number, outcome.Message);
        }
    }

    // The reasons of EduNotify's on-call templates that raise tickets, for the reason → category table. A key that isn't
    // allowed to manage technician calls gets 403, which leaves the list as it was.
    private async Task RefreshReasonsAsync(CancellationToken cancel)
    {
        var (status, body, problem) = await CallAsync(HttpMethod.Get, "oncall/reasons", null, cancel);
        if (problem is not null || status != HttpStatusCode.OK) return;
        if (EduNotify.ParseOnCallReasons(body) is { } reasons) store.SetEduNotifyKnownReasons(reasons);
    }

    // Requester pop-ups and technician alerts (HelpdeskStore.EduNotifyLink), taking down the ones past their time, and
    // telling EduNotify when a technician call's ticket is resolved. A pop-up that fails isn't tried again (a late "replied"
    // is worse than none); a take-down or a close that fails is, next pass.
    public async Task EventsAsync(CancellationToken cancel)
    {
        var sending = store.EduNotifySettings.Ready;
        var plan = store.PlanEduNotifyEvents(sending);
        if (!sending) return;
        foreach (var reference in plan.Recall)
        {
            var outcome = await RecallReferenceAsync(reference, $"the pop-up {reference}", cancel);
            if (outcome.Ok) store.RecordEduNotifyPopupGone(reference);
        }
        foreach (var popup in plan.Send)
        {
            var (outcome, sent) = await SendAsync(popup.Send, cancel);
            if (sent is not null) store.RecordEduNotifyPopupSent(popup, sent);
            else logger.LogWarning("The EduNotify pop-up {Reference} wasn't sent: {Message}", popup.Reference, outcome.Message);
        }
        foreach (var (number, reference) in plan.CloseCalls)
        {
            var (status, body, problem) = await CallAsync(HttpMethod.Post, $"oncalls/close?ticket={Uri.EscapeDataString(reference)}", null, cancel);
            // Not found means the call had already closed in EduNotify (attended and timed out, or closed on the console).
            if (problem is null && status is HttpStatusCode.OK or HttpStatusCode.NotFound) store.RecordEduNotifyCallClosed(number);
            else logger.LogWarning("EduNotify wasn't told technician call ticket {Ref} is resolved yet: {Message}", reference, problem ?? Refusal(status, body));
        }
    }

    // ---- Pairing and the link's tests (the EduNotify project's docs/EASY-LINK.md) ----

    // Sends EduNotify the code it showed, with where it reaches us and a fresh incoming key. EduNotify checks back with
    // that key before it answers, so a success means both directions work.
    public async Task<Outcome> PairAsync(string? address, string? code, string? emailDomain, string? reachUsAt, CancellationToken cancel)
    {
        var target = EduNotify.CheckAddress(address);
        if (!target.Ok) return new(false, target.Problem);
        if (string.IsNullOrWhiteSpace(code)) return new(false, "Enter the pairing code EduNotify is showing.");
        if (HelpdeskStore.CleanEmailDomain(emailDomain) is not { } domain) return new(false, "That isn't an email domain. Enter the part after the @ in staff emails, e.g. school.org.uk.");
        var us = EduNotify.CheckAddress(reachUsAt);
        if (!us.Ok) return new(false, "Enter the address EduNotify reaches EduHelpdesk at, e.g. https://helpdesk.school.local.");

        var incoming = store.PrepareEduNotifyIncomingKey();
        var json = System.Text.Json.JsonSerializer.Serialize(new { code = code.Trim(), helpdeskAddress = us.Address, incomingKey = incoming, emailDomain = domain });
        var (status, body, problem) = await SendRawAsync(target.Address, null, HttpMethod.Post, "pair", json, cancel);
        if (problem is not null) return new(false, problem);
        if (status != HttpStatusCode.OK)
        {
            var said = EduNotify.ParseError(body);
            return new(false, said.Length > 0 ? said : status == HttpStatusCode.NotFound
                ? "That address doesn't look like EduNotify, or its version is too old to pair (it needs 1.2 or later)."
                : $"EduNotify answered {(int)status}.");
        }
        if (EduNotify.ParsePaired(body) is not { } paired) return new(false, "EduNotify answered, but not in the way expected.");
        store.ApplyEduNotifyPairing(new(target.Address, paired.ApiKey, incoming, domain, paired.Notice, paired.Ping, paired.Popup, paired.Groups));
        await RefreshAsync(cancel);
        return new(true, $"Paired with EduNotify at {target.Address}. Technician calls are accepted; switch on the pop-ups you want below.");
    }

    // Ends the link on both sides: EduNotify deletes our key (if it can be reached), then we forget it.
    public async Task<Outcome> UnlinkAsync(CancellationToken cancel)
    {
        var (status, _, problem) = await CallAsync(HttpMethod.Delete, "pair", null, cancel);
        var told = problem is null && status == HttpStatusCode.OK;
        store.UnlinkEduNotify(told ? "Unlinked here and in EduNotify." : $"Unlinked here; EduNotify couldn't be told ({problem ?? $"it answered {(int)status}"}), so unlink there too.");
        return new(true, told ? "Unlinked from EduNotify, on both sides." : "Unlinked here. EduNotify couldn't be told, so press Unlink on its Settings → EduHelpdesk page too.");
    }

    // A pop-up to the signed-in technician's login, proving login matching, the pop-up template and the Open button.
    public async Task<Outcome> TestPopupAsync(string? login, string? settingsLink, CancellationToken cancel)
    {
        if (login is null) return new(false, "Your email isn't at the staff email domain, so EduNotify can't tell which login is yours.");
        var templateId = store.EduNotifyLinkSettings.PopupTemplateId;
        var (outcome, sent) = await SendAsync(new EduNotify.Send(templateId, false, [], "Test pop-up from EduHelpdesk", "If you can see this, pop-ups reach you.",
            $"EH-test-to-{login}", [login], settingsLink is null ? null : "Open the settings", settingsLink), cancel);
        if (sent is null) return outcome;
        return sent.Online > 0
            ? new(true, $"Shown on {sent.Online} PC{(sent.Online == 1 ? "" : "s")} logged on as {login}.")
            : new(false, $"Sent, but no PC is logged on as {login} with EduNotify running. Check you're logged on to a PC with EduNotify, and that {login} is your Windows login.");
    }

    // The end-to-end test: EduNotify makes a TEST ticket exactly as a technician call would and assigns it to whoever
    // asked; then it is closed, EduNotify told, and the ticket removed. Each step comes back for the page.
    public async Task<(bool Ok, IReadOnlyList<(string Step, bool Ok, string Detail)> Steps)> TestCallAsync(string? technicianEmail, CancellationToken cancel)
    {
        var steps = new List<(string, bool, string)>();
        var (status, body, problem) = await CallAsync(HttpMethod.Post, "oncall/test",
            System.Text.Json.JsonSerializer.Serialize(new { technicianEmail }), cancel);
        if (problem is not null || status != HttpStatusCode.OK || EduNotify.ParseTestCall(body) is not { } test)
        {
            steps.Add(("EduNotify ran the test", false, problem ?? Refusal(status, body)));
            return (false, steps);
        }
        steps.AddRange(test.Steps);
        var ok = test.Ok;
        if (test.Ticket.Length > 0 && store.FindTicketByRef(test.Ticket).Number is { } number)
        {
            var (closeStatus, closeBody, closeProblem) = await CallAsync(HttpMethod.Post, $"oncalls/close?ticket={Uri.EscapeDataString(test.Ticket)}", null, cancel);
            var closed = closeProblem is null && closeStatus == HttpStatusCode.OK;
            steps.Add(("EduNotify told it's resolved", closed, closed ? "" : closeProblem ?? Refusal(closeStatus, closeBody)));
            store.RemoveEduNotifyTestTicket(number);
            steps.Add(("Test ticket removed", true, ""));
            ok &= closed;
        }
        return (ok, steps);
    }

    // A silent test (EduNotify shows nothing) to each desk's PCs, then how many said they got it.
    public async Task<IReadOnlyList<(string Desk, Outcome Outcome)>> TestDeskPcsAsync(CancellationToken cancel)
    {
        var ping = store.TechnicianPingSettings;
        var results = new List<(string, Outcome)>();
        var sent = new List<(string Desk, long Id, int Targeted)>();
        foreach (var desk in store.Desks)
        {
            var groups = ping.DeskGroups.GetValueOrDefault(desk.Id) ?? [];
            if (groups.Count == 0) { results.Add((desk.Name, new(false, "No PCs chosen for this desk."))); continue; }
            var (outcome, message) = await SendAsync(new EduNotify.Send(ping.TemplateId, false, groups.ToList(), "Desk PCs test", null,
                $"EH-test-desk-{desk.Id:N}", Test: true), cancel);
            if (message is null) results.Add((desk.Name, outcome));
            else sent.Add((desk.Name, message.Id, message.Targeted));
        }
        if (sent.Count > 0) await Task.Delay(TimeSpan.FromSeconds(5), cancel); // PCs answer within a second or two
        foreach (var (desk, id, targeted) in sent)
        {
            var (status, body, problem) = await CallAsync(HttpMethod.Get, $"messages/{id}", null, cancel);
            var delivery = problem is null && status == HttpStatusCode.OK ? EduNotify.ParseDelivery(body) : null;
            results.Add((desk, delivery is null ? new(false, problem ?? Refusal(status, body))
                : new(delivery.Received == targeted && targeted > 0, $"{delivery.Received} of {targeted} PCs received it{(delivery.Received < targeted ? " (the rest are switched off, or need EduNotify's latest client)" : "")}.")));
        }
        return results;
    }

    private async Task<Outcome> RecallReferenceAsync(string reference, string what, CancellationToken cancel)
    {
        var (status, body, problem) = await CallAsync(HttpMethod.Post, $"messages/recall?reference={Uri.EscapeDataString(reference)}", null, cancel);
        Outcome outcome = problem is not null ? new(false, problem)
            : status == HttpStatusCode.OK ? new(true, "Recalled.")
            : status == HttpStatusCode.NotFound ? new(true, "It had already gone from EduNotify.", NothingLive: true)
            : new(false, Refusal(status, body));
        store.RecordEduNotifyAttempt(outcome.Ok, outcome.Ok ? $"Recalled {what}." : outcome.Message);
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
        if (key.Length == 0) return (0, "", "Not linked to EduNotify: pair the two under Settings → EduNotify.");
        return await SendRawAsync(settings.Address, key, method, path, json, cancel);
    }

    // One request to {address}/api/v1/{path}, with the key when there is one (pairing has none yet).
    private async Task<(HttpStatusCode Status, string Body, string? Problem)> SendRawAsync(string address, string? key, HttpMethod method, string path, string? json, CancellationToken cancel)
    {
        var settings = new { Address = address };
        var client = clients.CreateClient(ClientName);
        try
        {
            using var request = new HttpRequestMessage(method, $"{settings.Address}/api/v1/{path}");
            if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, cancel);
            // EduNotify never echoes the key, but nothing it says can carry the key onwards to a page or a log.
            var body = await response.Content.ReadAsStringAsync(cancel);
            if (!string.IsNullOrEmpty(key)) body = body.Replace(key, "[API key]", StringComparison.Ordinal);
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
