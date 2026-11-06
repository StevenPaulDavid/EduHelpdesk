namespace EduHelpdesk.Agent;

// The Windows service. It reports when it starts and then every day (the helpdesk says how often), and also soon after
// someone different signs in, so "Last signed in" on the asset is current. A report that can't be sent - the laptop is
// at home, the helpdesk is down - is tried again every 15 minutes with fresh details until it goes. After each report it
// tells the tray icon the asset tag (AgentStatus), and updates itself when the helpdesk has a newer agent (Updater).
public sealed class AgentService : BackgroundService
{
    // Often enough that a tray icon appears soon after someone signs in; a report is only sent when one is due.
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);
    // Give the network a moment after start-up before the first try.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        AgentLog.Write($"EduHelpdesk Agent {Collector.Version} started.");
        Installer.CleanUp();
        // Tray icons only when running as the service (as SYSTEM), not "run" from a prompt.
        var trays = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService();
        if (trays) Safely("restart the tray icons", TrayLauncher.StopOldTrays);
        try { await Task.Delay(StartDelay, stop); } catch (TaskCanceledException) { return; }
        // Remote support needs the tray to ask the person at the PC, so only as the service.
        var remote = trays ? Task.Run(() => new RemoteSupport().RunAsync(stop), stop) : Task.CompletedTask;
        var updater = new Updater();
        var nextReport = DateTime.MinValue;
        var retryAt = DateTime.MinValue;
        string? reportedUser = null;
        while (!stop.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var user = Collector.CurrentUser();
            var userChanged = user is not null && !string.Equals(user, reportedUser, StringComparison.OrdinalIgnoreCase);
            if ((now >= nextReport || userChanged) && now >= retryAt)
            {
                var config = AgentConfig.Load();
                if (config is null)
                {
                    AgentLog.Write($"No settings at {AgentConfig.FilePath}. Install the agent with the command from the Device agent page.");
                    retryAt = now + RetryAfter;
                }
                else
                {
                    var outcome = await new HelpdeskClient(config).SendAsync(Collector.Collect(), stop);
                    AgentLog.Write(outcome.Message);
                    if (outcome.Ok)
                    {
                        nextReport = now + TimeSpan.FromHours(Math.Clamp(outcome.Answer?.NextReportHours ?? 24, 1, 168));
                        reportedUser = user;
                        AgentStatus.Write(outcome.Answer?.AssetTag, outcome.Answer?.Status ?? "", outcome.Answer?.Message);
                        updater.Consider(outcome.Answer);
                    }
                    else retryAt = now + RetryAfter;
                }
            }
            // The update stops this service itself within moments. If it somehow doesn't, reporting carries on in an hour.
            if (await updater.RunIfDueAsync(stop)) nextReport = DateTime.UtcNow + TimeSpan.FromHours(1);
            if (trays) Safely("start the tray icon", TrayLauncher.EnsureTrays);
            try { await Task.Delay(Tick, stop); } catch (TaskCanceledException) { break; }
        }
        try { await remote; } catch (OperationCanceledException) { }
        AgentLog.Write("EduHelpdesk Agent stopped.");
    }

    // The tray is a nicety: nothing about it may stop the reports.
    private static void Safely(string what, Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            AgentLog.Write($"Couldn't {what}: {exception.Message}");
        }
    }
}
