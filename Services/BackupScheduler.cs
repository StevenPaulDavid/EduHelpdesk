namespace EduHelpdesk.Services;

// Makes the nightly backup, then applies the retention rules (Settings → Data retention) if any are on. Checks every ten minutes whether one is due (HelpdeskStore.BackupDue): once a day at the
// chosen hour, or on the next check after starting if the app was off then - which also means a brand-new install or
// an upgrade gets its first backup a minute or so after it starts.
public sealed class BackupScheduler(HelpdeskStore store, ILogger<BackupScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstCheck, stoppingToken); }
        catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                if (store.BackupDue(DateTime.Now))
                {
                    var (ok, message) = await Task.Run(() => store.CreateBackup(manual: false), stoppingToken);
                    if (ok) logger.LogInformation("Nightly backup: {Message}", message);
                    else logger.LogError("Nightly backup failed: {Message}", message);
                }
                // One size reading a day, for the growth figures on Settings → Database.
                await Task.Run(() => store.RecordSizeIfDue(DateTime.Now), stoppingToken);
                // Once a day from 7am: the bell rings for anything newly due on the DfE registers.
                await Task.Run(() => store.SendComplianceReminders(DateTime.Now), stoppingToken);
                // Retention runs after the backup, and only once one has worked (HelpdeskStore.RetentionDue), so nothing
                // is deleted that no backup holds.
                if (store.RetentionDue(DateTime.Now))
                {
                    var (ok, message) = await Task.Run(store.ApplyRetention, stoppingToken);
                    if (ok) logger.LogInformation("{Message}", message);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Never let a backup problem stop the scheduler - the next check tries again.
                logger.LogError(ex, "The backup scheduler hit an error");
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
