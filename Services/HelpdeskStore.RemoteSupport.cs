using EduHelpdesk.AgentContract;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Remote support through Quick Assist, from a ticket. Quick Assist can't be driven from outside - the technician makes a
// security code on their own PC, and someone at the user's PC types it in and presses Allow - so this carries the code
// to the right PC and keeps the ticket informed:
//   1. The technician types the code on the ticket, for a PC linked to it that reports through the device agent.
//   2. That PC's agent is waiting on /api/agent/v1/remote (a long poll, like the bell's) and collects it within a second.
//   3. Its tray icon shows "IT would like to connect" with the code, and opens Quick Assist on Open.
//   4. The agent reports what happens, and each step goes in the ticket's history: accepted or declined, Quick Assist
//      opened, and closed again with how long it was open - or that nobody was signed in, or no answer in 10 minutes.
// Requests are held in memory: they last minutes, and the ticket's history is the record. One per PC at a time; sending
// again (a mistyped code) replaces it.
public sealed partial class HelpdeskStore
{
    public static readonly TimeSpan RemoteCodeLifetime = TimeSpan.FromMinutes(10);
    // A session that never reported its end (the PC switched off) is forgotten after this.
    private static readonly TimeSpan RemoteForgetAfter = TimeSpan.FromHours(8);
    public const int MinRemoteCode = 4;
    public const int MaxRemoteCode = 12;

    public sealed record RemoteRequest(Guid Id, Guid DeviceId, int TicketNumber, string TechnicianName, string Code, DateTime CreatedAt, DateTime ExpiresAt)
    {
        public string State { get; init; } = RemoteStates.Requested;
        public DateTime UpdatedAt { get; init; } = CreatedAt;
        public DateTime? StartedAt { get; init; }
    }

    public sealed record RemoteTarget(AgentDevice Device, AssetRecord Asset);
    public sealed record RemoteWait(int Status, RemoteOffer? Offer = null, string? Error = null);

    private readonly Dictionary<Guid, RemoteRequest> _remoteRequests = [];
    // Completed when a request arrives for that PC, to wake its waiting agent; then replaced.
    private readonly Dictionary<Guid, TaskCompletionSource> _remoteSignals = [];

    // The PCs remote support can go to from this ticket: its linked assets that a PC reports into.
    public IReadOnlyList<RemoteTarget> RemoteSupportTargets(int ticketNumber)
    {
        lock (_sync)
        {
            if (_data.Tickets.FirstOrDefault(x => x.Number == ticketNumber) is not { } ticket) return [];
            return ticket.AssetIds
                .Select(id => (Asset: _data.Assets.FirstOrDefault(x => x.Id == id), Device: _agents.Values.Select(x => x.Device).Where(x => x.AssetId == id).OrderByDescending(x => x.LastSeenAt).FirstOrDefault()))
                .Where(x => x.Asset is not null && x.Device is not null)
                .Select(x => new RemoteTarget(x.Device!, x.Asset!))
                .ToList();
        }
    }

    // The newest request from this ticket, for its live status.
    public RemoteRequest? RemoteSupportFor(int ticketNumber)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            ForgetOldRemoteRequests(now);
            if (ExpireRemoteRequests(now)) Save();
            return _remoteRequests.Values.Where(x => x.TicketNumber == ticketNumber).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        }
    }

    // A request whose code has run out without Quick Assist opening is finished, whether or not the PC said so: it may
    // be off, or away from school, and never collected it. A minute's grace lets the PC's own report arrive first.
    private bool ExpireRemoteRequests(DateTime now)
    {
        var changed = false;
        foreach (var (deviceId, request) in _remoteRequests.ToList())
        {
            if (RemoteStates.IsFinished(request.State) || request.State == RemoteStates.Started || now < request.ExpiresAt + TimeSpan.FromMinutes(1)) continue;
            _remoteRequests[deviceId] = request with { State = RemoteStates.Expired, UpdatedAt = now };
            var pc = _agents.Values.Select(x => x.Device).FirstOrDefault(x => x.Id == deviceId) is { } device ? Named(device) : "the PC";
            AddRemoteHistory(request.TicketNumber, "Remote support not taken up", request.State == RemoteStates.Requested
                ? $"{pc} didn't collect the code within {(int)RemoteCodeLifetime.TotalMinutes} minutes. It may be switched off or away from school."
                : $"Quick Assist wasn't opened on {pc} within {(int)RemoteCodeLifetime.TotalMinutes} minutes, so the code has run out.");
            changed = true;
        }
        return changed;
    }

    public (bool Ok, string Message) RequestRemoteSupport(int ticketNumber, Guid deviceId, string? code)
    {
        var clean = new string((code ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        if (clean.Length is < MinRemoteCode or > MaxRemoteCode)
            return (false, "Type the security code Quick Assist shows on your PC under Help someone.");
        lock (_sync)
        {
            if (AgentRefusal() is { } refusal) return (false, refusal);
            if (_data.Tickets.FirstOrDefault(x => x.Number == ticketNumber) is not { } ticket) return (false, "That ticket no longer exists.");
            if (TicketInsights.IsClosed(ticket)) return (false, "This ticket is closed. Reopen it to offer remote support.");
            if (RemoteSupportTargets(ticketNumber).FirstOrDefault(x => x.Device.Id == deviceId) is not { } target)
                return (false, "That PC isn't linked to this ticket, or no longer reports through the device agent.");
            var now = DateTime.UtcNow;
            var actor = CurrentActor();
            var request = new RemoteRequest(Guid.NewGuid(), deviceId, ticketNumber, actor.Name, clean, now, now + RemoteCodeLifetime);
            _remoteRequests[deviceId] = request;
            AddRemoteHistory(ticketNumber, "Remote support requested",
                $"{actor.Name} sent a Quick Assist code to {Named(target.Device)} ({target.Asset.AssetTag}).");
            Save();
            // Wake the PC's agent if it is waiting.
            if (_remoteSignals.Remove(deviceId, out var signal)) signal.TrySetResult();
            return (true, $"Sent to {Named(target.Device)}. Whoever is at it will see the code and a button to open Quick Assist.");
        }
    }

    // The agent's long poll: the request waiting for this PC, as soon as there is one, or nothing after `wait`.
    public async Task<RemoteWait> WaitForRemoteRequestAsync(string? token, TimeSpan wait, CancellationToken cancel)
    {
        Task signal;
        lock (_sync)
        {
            var (check, device) = CheckAgentToken(token);
            if (check is not null) return check;
            if (TakeRemoteOffer(device!.Id) is { } ready) return new RemoteWait(StatusCodes.Status200OK, ready);
            if (!_remoteSignals.TryGetValue(device.Id, out var waiting)) _remoteSignals[device.Id] = waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = waiting.Task;
        }
        // Not on the caller's context, as WaitForNotificationsAsync.
        try { await Task.WhenAny(signal, Task.Delay(wait, cancel)).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        lock (_sync)
        {
            var (check, device) = CheckAgentToken(token);
            if (check is not null) return check;
            return TakeRemoteOffer(device!.Id) is { } offer ? new RemoteWait(StatusCodes.Status200OK, offer) : new RemoteWait(StatusCodes.Status204NoContent);
        }
    }

    // What the agent says happened. Each step goes in the ticket's history once.
    public (int Status, string? Error) ReportRemoteState(string? token, Guid requestId, string? state)
    {
        lock (_sync)
        {
            var (check, device) = CheckAgentToken(token);
            if (check is not null) return (check.Status, check.Error);
            if (!_remoteRequests.TryGetValue(device!.Id, out var request) || request.Id != requestId)
                return (StatusCodes.Status404NotFound, "That request has been replaced or has finished.");
            if (state is null || !RemoteStates.FromAgent.Contains(state)) return (StatusCodes.Status400BadRequest, "Unknown state.");
            if (request.State == state || RemoteStates.IsFinished(request.State)) return (StatusCodes.Status200OK, null);
            var now = DateTime.UtcNow;
            var pc = Named(device);
            (string Action, string Details)? line = state switch
            {
                RemoteStates.Accepted => ("Remote support accepted", $"Someone at {pc} chose to open Quick Assist."),
                RemoteStates.Declined => ("Remote support declined", $"Someone at {pc} chose No thanks."),
                RemoteStates.Nobody => ("Remote support not delivered", $"Nobody was signed in to {pc}."),
                RemoteStates.Unavailable => ("Remote support not possible", $"Quick Assist isn't installed on {pc}. It's free from the Microsoft Store."),
                RemoteStates.Started => ("Quick Assist opened", $"Quick Assist opened on {pc}."),
                RemoteStates.Ended => ("Quick Assist closed", request.StartedAt is { } started
                    ? $"Quick Assist closed on {pc} after {Minutes(now - started)}."
                    : $"Quick Assist closed on {pc}."),
                RemoteStates.Expired => ("Remote support not taken up", $"Quick Assist wasn't opened on {pc} within {(int)RemoteCodeLifetime.TotalMinutes} minutes, so the code has run out."),
                _ => null
            };
            _remoteRequests[device.Id] = request with { State = state, UpdatedAt = now, StartedAt = state == RemoteStates.Started ? now : request.StartedAt };
            if (line is { } history)
            {
                AddRemoteHistory(request.TicketNumber, history.Action, history.Details);
                Save();
            }
            return (StatusCodes.Status200OK, null);
        }
    }

    // Where it has got to, in words for the technician watching the ticket. Active: still worth watching.
    public static (string Text, bool Active) DescribeRemote(RemoteRequest request, DateTime utcNow)
    {
        var minutesLeft = Math.Max(0, (int)Math.Ceiling((request.ExpiresAt - utcNow).TotalMinutes));
        return request.State switch
        {
            RemoteStates.Requested => ($"Waiting for the PC to collect it. The code lasts another {minutesLeft} minutes.", true),
            RemoteStates.Delivered => ("The PC has it and is about to show it.", true),
            RemoteStates.Shown => ("On the screen, waiting for an answer.", true),
            RemoteStates.Accepted => ("Accepted: Quick Assist is opening. They type your code, then you see their screen once they press Allow.", true),
            RemoteStates.Started => ($"Quick Assist is open on the PC (since {request.StartedAt?.ToLocalTime():HH:mm}).", true),
            RemoteStates.Declined => ("They chose No thanks.", false),
            RemoteStates.Nobody => ("Nobody is signed in to that PC.", false),
            RemoteStates.Unavailable => ("Quick Assist isn't installed on that PC.", false),
            RemoteStates.Ended => ("Quick Assist was closed.", false),
            RemoteStates.Expired => ("Not taken up in time; the code has run out.", false),
            _ => (request.State, false)
        };
    }

    // ---- Helpers, called with the lock held ----

    private (RemoteWait? Refusal, AgentDevice? Device) CheckAgentToken(string? token)
    {
        if (AgentRefusal() is { } refusal) return (new RemoteWait(StatusCodes.Status403Forbidden, Error: refusal), null);
        if (string.IsNullOrEmpty(token) || !token.StartsWith(AgentTokenPrefix, StringComparison.Ordinal) || !_agents.TryGetValue(HashToken(token), out var row))
            return (new RemoteWait(StatusCodes.Status401Unauthorized, Error: "This PC isn't enrolled."), null);
        return (null, row.Device);
    }

    // The request for this PC if it hasn't been taken up yet, marked delivered. Given again until the PC says it is
    // showing it, in case the answer to the first long poll was lost.
    private RemoteOffer? TakeRemoteOffer(Guid deviceId)
    {
        var now = DateTime.UtcNow;
        ForgetOldRemoteRequests(now);
        if (!_remoteRequests.TryGetValue(deviceId, out var request) || request.State is not (RemoteStates.Requested or RemoteStates.Delivered) || request.ExpiresAt <= now)
            return null;
        if (request.State == RemoteStates.Requested) _remoteRequests[deviceId] = request with { State = RemoteStates.Delivered, UpdatedAt = now };
        return new RemoteOffer(request.Id, request.Code, request.TechnicianName, Ref(request.TicketNumber), request.ExpiresAt);
    }

    private void ForgetOldRemoteRequests(DateTime now)
    {
        foreach (var (device, request) in _remoteRequests.ToList())
            if (now - request.UpdatedAt > RemoteForgetAfter) _remoteRequests.Remove(device);
    }

    private void AddRemoteHistory(int ticketNumber, string action, string details)
    {
        var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
        if (index < 0) return;
        var ticket = _data.Tickets[index];
        _data.Tickets[index] = ticket with { History = [.. ticket.History, new TicketActivity(action, details, DateTime.UtcNow) { By = CurrentActor() }] };
    }

    private static string Minutes(TimeSpan span) => span.TotalMinutes < 1.5 ? "about a minute" : $"{(int)Math.Round(span.TotalMinutes)} minutes";
}
