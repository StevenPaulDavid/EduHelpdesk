using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;
using EduHelpdesk.AgentContract;

namespace EduHelpdesk.Agent;

// Sends a report to the helpdesk: enrols first if this PC has no token yet, and again if the helpdesk no longer knows
// its token (401 - removed on the Device agent page, or the helpdesk was reset). HTTPS only; with a fingerprint, the
// helpdesk's certificate must be exactly that one.
public sealed class HelpdeskClient(AgentConfig config)
{
    private const string Prefix = "/api/agent/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Outcome(bool Ok, string Message, CheckInAnswer? Answer = null);

    public async Task<Outcome> SendAsync(AgentReport report, CancellationToken cancel)
    {
        if (!Uri.TryCreate(config.Server, UriKind.Absolute, out var server) || server.Scheme != Uri.UriSchemeHttps)
            return new Outcome(false, $"The helpdesk address '{config.Server}' isn't an https:// address. Install the agent again with the command from the Device agent page.");
        using var http = Create(server);
        try
        {
            if (config.Token.Length > 0)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Prefix + "/report") { Content = JsonContent.Create(report, options: Json) };
                request.Headers.Authorization = new("Bearer", config.Token);
                using var response = await http.SendAsync(request, cancel);
                if (response.StatusCode != HttpStatusCode.Unauthorized) return await Read(response, cancel);
                AgentLog.Write("The helpdesk doesn't know this PC's token any more. Enrolling again.");
                config.Token = "";
                config.Save();
            }
            if (config.EnrolmentKey.Length == 0) return new Outcome(false, "This PC has no enrolment key. Install the agent again with the command from the Device agent page.");
            using var enrol = await http.PostAsJsonAsync(Prefix + "/enrol", new EnrolRequest(config.EnrolmentKey, report), Json, cancel);
            var outcome = await Read(enrol, cancel);
            if (outcome.Answer?.Token is { Length: > 0 } token)
            {
                config.Token = token;
                config.Save();
            }
            return outcome;
        }
        catch (HttpRequestException exception)
        {
            return new Outcome(false, Unreachable(server, exception));
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new Outcome(false, $"The helpdesk at {server.Authority} didn't answer in time.");
        }
    }

    private string Unreachable(Uri server, HttpRequestException exception) =>
        exception.InnerException is System.Security.Authentication.AuthenticationException
            ? AgentConfig.NormaliseFingerprint(config.Fingerprint).Length > 0
                ? $"The helpdesk at {server.Authority} has a different certificate from the one in the install command. If its certificate was renewed, install again with the command from the Device agent page."
                : $"The helpdesk at {server.Authority} has a certificate this PC doesn't trust. Install again with the command from the Device agent page, which includes its fingerprint."
            : $"Couldn't reach the helpdesk at {server.Authority}: {exception.Message}";

    // The agent the helpdesk hands out, saved to target only if its SHA-256 is the one the helpdesk's answer gave.
    public async Task<(bool Ok, string Message)> DownloadAgentAsync(string target, string sha256, CancellationToken cancel)
    {
        if (!Uri.TryCreate(config.Server, UriKind.Absolute, out var server) || server.Scheme != Uri.UriSchemeHttps) return (false, "The helpdesk address isn't https://.");
        if (config.Token.Length == 0) return (false, "This PC isn't enrolled yet.");
        using var http = Create(server);
        http.Timeout = TimeSpan.FromMinutes(10);
        var partial = target + ".part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Prefix + "/agent");
            request.Headers.Authorization = new("Bearer", config.Token);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode) return (false, $"the helpdesk answered {(int)response.StatusCode}: {await Message(response, cancel)}");
            await using (var file = File.Create(partial))
                await response.Content.CopyToAsync(file, cancel);
            string actual;
            await using (var file = File.OpenRead(partial))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));
            if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                return (false, "what arrived isn't the file the helpdesk described (its SHA-256 doesn't match), so it was thrown away.");
            }
            File.Move(partial, target, overwrite: true);
            return (true, "Downloaded.");
        }
        catch (HttpRequestException exception) { return (false, Unreachable(server, exception)); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { return (false, "the download didn't finish in time."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return (false, exception.Message); }
    }

    // Waits (the helpdesk holds it up to ~50 seconds) for a remote support request for this PC. Status 200 with the
    // offer, 204 with none; anything else (or 0 when unreachable) means try again later.
    public async Task<(int Status, RemoteOffer? Offer)> WaitRemoteAsync(CancellationToken cancel)
    {
        if (!Uri.TryCreate(config.Server, UriKind.Absolute, out var server) || server.Scheme != Uri.UriSchemeHttps || config.Token.Length == 0) return (0, null);
        using var http = Create(server);
        http.Timeout = TimeSpan.FromSeconds(75);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Prefix + "/remote");
            request.Headers.Authorization = new("Bearer", config.Token);
            using var response = await http.SendAsync(request, cancel);
            if (response.StatusCode == HttpStatusCode.OK) return (200, await response.Content.ReadFromJsonAsync<RemoteOffer>(Json, cancel));
            return ((int)response.StatusCode, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException || (exception is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return (0, null);
        }
    }

    public async Task ReportRemoteAsync(Guid id, string state, CancellationToken cancel)
    {
        if (!Uri.TryCreate(config.Server, UriKind.Absolute, out var server) || server.Scheme != Uri.UriSchemeHttps || config.Token.Length == 0) return;
        using var http = Create(server);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Prefix}/remote/{id}") { Content = JsonContent.Create(new RemoteStateReport(state), options: Json) };
            request.Headers.Authorization = new("Bearer", config.Token);
            using var response = await http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode) AgentLog.Write($"The helpdesk didn't take the remote support update '{state}': {await Message(response, cancel)}");
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            AgentLog.Write($"Couldn't tell the helpdesk '{state}' about remote support: {exception.Message}");
        }
    }

    // Whether the helpdesk can be reached and has the agent turned on, for the install command to say so straight away.
    public async Task<string> CheckAsync(CancellationToken cancel)
    {
        if (!Uri.TryCreate(config.Server, UriKind.Absolute, out var server) || server.Scheme != Uri.UriSchemeHttps)
            return "That isn't an https:// address.";
        using var http = Create(server);
        try
        {
            using var response = await http.GetAsync(Prefix + "/info", cancel);
            if (!response.IsSuccessStatusCode) return $"The helpdesk answered {(int)response.StatusCode}: {await Message(response, cancel)}";
            var info = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancel);
            return info.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True
                ? $"Reached EduHelpdesk {(info.TryGetProperty("serverVersion", out var version) ? version.GetString() : "")}; the device agent is on."
                : "Reached EduHelpdesk, but the device agent is turned off there. This PC will report once it is turned on.";
        }
        catch (HttpRequestException exception) { return Unreachable(server, exception); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { return "It didn't answer in time."; }
    }

    private HttpClient Create(Uri server)
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        var fingerprint = AgentConfig.NormaliseFingerprint(config.Fingerprint);
        if (fingerprint.Length > 0)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate is not null && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).Equals(fingerprint, StringComparison.OrdinalIgnoreCase);
        return new HttpClient(handler) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(60) };
    }

    private static async Task<Outcome> Read(HttpResponseMessage response, CancellationToken cancel)
    {
        if (!response.IsSuccessStatusCode) return new Outcome(false, $"The helpdesk refused the report ({(int)response.StatusCode}): {await Message(response, cancel)}");
        var answer = await response.Content.ReadFromJsonAsync<CheckInAnswer>(Json, cancel);
        if (answer is null) return new Outcome(false, "The helpdesk's answer couldn't be read.");
        var said = answer.Status == "linked" ? $"Reported into {answer.AssetTag}." : answer.Message ?? "Waiting for IT to confirm which asset this is.";
        return new Outcome(true, said, answer);
    }

    private static async Task<string> Message(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancel);
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("message", out var message)) return message.GetString() ?? "";
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) { }
        return response.ReasonPhrase ?? "";
    }
}
