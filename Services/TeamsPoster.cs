using System.Net;
using System.Text;

namespace EduHelpdesk.Services;

// Sends what HelpdeskStore.Teams queues to the Teams webhook, and every few minutes asks it whether any ticket has become
// overdue. Sending is on a background thread of its own so that a slow or unreachable Teams can never hold up the person
// whose ticket caused the post. A failure is recorded for the settings page and the error log and otherwise ignored:
// the helpdesk never depends on Teams being there.
public sealed class TeamsPoster(HelpdeskStore store, IHttpClientFactory clients, ILogger<TeamsPoster> logger, TimeSpan[]? retryDelays = null) : BackgroundService
{
    public const string ClientName = "teams";
    private static readonly TimeSpan FirstOverdueCheck = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan OverdueInterval = TimeSpan.FromMinutes(5);
    // Pauses before each retry of a post that failed for a reason worth retrying (Teams busy, or not reachable just now).
    // Only the tests pass their own, so a retry doesn't make them wait.
    private readonly TimeSpan[] _retryDelays = retryDelays ?? [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15)];

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(SendLoop(stoppingToken), OverdueLoop(stoppingToken));

    private async Task SendLoop(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var post in store.TeamsOutbox.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var (ok, message) = await SendAsync(post, retry: true, stoppingToken);
                    store.RecordTeamsAttempt(ok, message);
                    if (!ok) logger.LogWarning("A Teams post didn't go: {Message}", message);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The Teams poster hit an error");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task OverdueLoop(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstOverdueCheck, stoppingToken); }
        catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(OverdueInterval);
        do
        {
            try { store.AnnounceOverdue(DateTime.UtcNow); }
            catch (Exception ex)
            {
                // Never let one bad pass stop the checks - the next one tries again.
                logger.LogError(ex, "The overdue check for Teams hit an error");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }

    // One post, now. The Test button on the settings page uses it with no retries so the answer comes back at once; queued
    // posts retry a couple of times when Teams is busy or can't be reached. Never throws except when cancelled; the reply
    // says what happened in words the page can show, with the secret address kept out of it.
    public async Task<(bool Ok, string Message)> SendAsync(HelpdeskStore.TeamsPost post, bool retry, CancellationToken cancel)
    {
        var url = store.TeamsWebhookAddress;
        if (url.Length == 0) return (false, "No webhook address is set.");
        var json = TeamsWebhook.Message(post);
        var client = clients.CreateClient(ClientName);
        var attempts = retry ? _retryDelays.Length + 1 : 1;
        var last = "Nothing was sent.";
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0) await Task.Delay(_retryDelays[attempt - 1], cancel);
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(url, content, cancel);
                if (response.IsSuccessStatusCode) return (true, "Teams accepted the message.");
                var body = await response.Content.ReadAsStringAsync(cancel);
                last = $"Teams answered {(int)response.StatusCode} {response.ReasonPhrase}.{Detail(body, url)}";
                // A refusal that asking again won't change - a wrong address, a removed workflow - is not retried.
                if (!Transient(response.StatusCode)) return (false, last);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                last = ex is TaskCanceledException ? "Teams didn't answer in time." : $"Teams couldn't be reached: {Scrub(ex.Message, url)}";
            }
        }
        return (false, last);
    }

    private static bool Transient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    // The start of what Teams said, on one line, for the status text. Teams' own error text is the best clue there is.
    private static string Detail(string body, string url)
    {
        var text = string.Join(' ', Scrub(body, url).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return text.Length == 0 ? "" : " " + (text.Length > 200 ? text[..200] + "…" : text);
    }

    // Takes the address (and the signature in it) out of anything that is about to be shown or logged.
    private static string Scrub(string text, string url) => text.Replace(url, "[webhook address]", StringComparison.OrdinalIgnoreCase);
}
