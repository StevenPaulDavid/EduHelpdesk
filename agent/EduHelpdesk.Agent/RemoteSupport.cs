using System.Diagnostics;
using EduHelpdesk.AgentContract;

namespace EduHelpdesk.Agent;

// The service's side of remote support (the helpdesk's is Services/HelpdeskStore.RemoteSupport.cs). It keeps a long poll
// open to the helpdesk; when a technician sends a Quick Assist code from a ticket, it hands the request to the tray icon
// (AgentStatus, HKLM), which asks the person at the PC. Then it watches: their answer (from their own HKCU), Quick Assist
// opening, and Quick Assist closing - and tells the helpdesk each step, which puts it in the ticket's history. A newer
// request (the technician sent a new code) replaces the one in hand.
public sealed class RemoteSupport
{
    private static readonly TimeSpan Watch = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WatchOpen = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Unreachable = TimeSpan.FromMinutes(1);

    public async Task RunAsync(CancellationToken stop)
    {
        CancellationTokenSource? current = null;
        // Nothing left over from before a restart.
        AgentStatus.WriteRemoteRequest(null);
        while (!stop.IsCancellationRequested)
        {
            var config = AgentConfig.Load();
            if (config is null || config.Token.Length == 0)
            {
                await Pause(Unreachable, stop);
                continue;
            }
            var (status, offer) = await new HelpdeskClient(config).WaitRemoteAsync(stop);
            if (offer is not null)
            {
                current?.Cancel();
                current = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var token = current.Token;
                _ = Task.Run(() => HandleAsync(config, offer, token), token);
            }
            // 204: nobody asked; wait again straight away. Anything else: the helpdesk is away, off, or doesn't know us.
            else if (status != 204) await Pause(Unreachable, stop);
        }
    }

    private static async Task HandleAsync(AgentConfig config, RemoteOffer offer, CancellationToken cancel)
    {
        var client = new HelpdeskClient(config);
        AgentLog.Write($"Remote support requested by {offer.TechnicianName} for {offer.TicketRef}.");
        try
        {
            if (!TrayLauncher.AnyoneSignedIn())
            {
                await client.ReportRemoteAsync(offer.Id, RemoteStates.Nobody, cancel);
                return;
            }
            var openBefore = QuickAssistOpen();
            AgentStatus.WriteRemoteRequest(offer);
            TrayLauncher.EnsureTrays();
            await client.ReportRemoteAsync(offer.Id, RemoteStates.Shown, cancel);

            var accepted = false;
            DateTime? startedAt = null;
            while (!cancel.IsCancellationRequested)
            {
                await Task.Delay(startedAt is null ? Watch : WatchOpen, cancel);
                switch (AgentStatus.TakeRemoteAnswer(offer.Id, TrayLauncher.ActiveUserSids()))
                {
                    case "accepted" when !accepted:
                        accepted = true;
                        await client.ReportRemoteAsync(offer.Id, RemoteStates.Accepted, cancel);
                        break;
                    case "declined":
                        await client.ReportRemoteAsync(offer.Id, RemoteStates.Declined, cancel);
                        return;
                    case "unavailable":
                        await client.ReportRemoteAsync(offer.Id, RemoteStates.Unavailable, cancel);
                        return;
                }
                var open = QuickAssistOpen();
                // Quick Assist already open before the request isn't this session starting.
                if (startedAt is null && open && !openBefore)
                {
                    startedAt = DateTime.UtcNow;
                    await client.ReportRemoteAsync(offer.Id, RemoteStates.Started, cancel);
                }
                else if (startedAt is null && !open) openBefore = false;
                else if (startedAt is not null && !open)
                {
                    await client.ReportRemoteAsync(offer.Id, RemoteStates.Ended, cancel);
                    return;
                }
                if (startedAt is null && DateTime.UtcNow >= offer.ExpiresAt)
                {
                    await client.ReportRemoteAsync(offer.Id, RemoteStates.Expired, cancel);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Unless a newer request has taken its place, the tray's window can go.
            if (AgentStatus.ReadRemoteRequest()?.Id == offer.Id) AgentStatus.WriteRemoteRequest(null);
        }
    }

    // Quick Assist (QuickAssist.exe, from the Store or built in) running for someone signed in.
    private static bool QuickAssistOpen()
    {
        var open = false;
        foreach (var process in Process.GetProcessesByName("QuickAssist"))
            using (process) open |= process.SessionId != 0;
        return open;
    }

    private static async Task Pause(TimeSpan span, CancellationToken stop)
    {
        try { await Task.Delay(span, stop); } catch (TaskCanceledException) { }
    }
}
