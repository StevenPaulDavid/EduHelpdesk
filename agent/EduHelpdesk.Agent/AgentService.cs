namespace EduHelpdesk.Agent;

// The Windows service. It reports when it starts and then every day (the helpdesk says how often), and also soon after
// someone different signs in, so "Last signed in" on the asset is current. A report that can't be sent - the laptop is
// at home, the helpdesk is down - is tried again every 15 minutes with fresh details until it goes.
public sealed class AgentService : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);
    // Give the network a moment after start-up before the first try.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        AgentLog.Write($"EduHelpdesk Agent {Collector.Version} started.");
        try { await Task.Delay(StartDelay, stop); } catch (TaskCanceledException) { return; }
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
                    }
                    else retryAt = now + RetryAfter;
                }
            }
            try { await Task.Delay(Tick, stop); } catch (TaskCanceledException) { break; }
        }
        AgentLog.Write("EduHelpdesk Agent stopped.");
    }
}
