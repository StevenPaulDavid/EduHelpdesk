namespace EduHelpdesk.Services;

// Logs the recurring tickets (Settings → Recurring tickets) as they come due. Checks once a minute, so a ticket set for
// 07:30 is there by 07:31; the work is in HelpdeskStore.RunRecurringTickets.
public sealed class RecurringTicketScheduler(HelpdeskStore store, ILogger<RecurringTicketScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstCheck, stoppingToken); }
        catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var logged = await Task.Run(() => store.RunRecurringTickets(DateTime.Now), stoppingToken);
                if (logged > 0) logger.LogInformation("Recurring tickets: logged {Count}", logged);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Never let one bad run stop the schedule - the next minute tries again.
                logger.LogError(ex, "The recurring ticket scheduler hit an error");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }
}
