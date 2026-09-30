namespace EduHelpdesk.Tests;

// The health check on Settings → Database: a size reading a day for the growth figures, the warning limits, and the
// warnings Administrators see on the Overview.
public class DatabaseHealthTests
{
    [Fact]
    public void One_size_reading_is_kept_a_day_and_growth_is_measured_from_the_oldest_in_the_last_30_days()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.RecordSizeIfDue(DateTime.Now.AddDays(-45)));
        Assert.True(store.RecordSizeIfDue(DateTime.Now.AddDays(-20)));
        Assert.True(store.RecordSizeIfDue(DateTime.Now.AddDays(-5)));
        Assert.False(store.RecordSizeIfDue(DateTime.Now.AddDays(-5)));

        var health = test.Reopen().DatabaseHealth(DateTime.UtcNow);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(-20)), health.GrowthFrom?.Day);
        Assert.True(health.DatabaseBytes > 0);

        // A factory reset clears the school's data, not the history of the files.
        Assert.True(test.Store.ResetFactory("DELETE").Ok);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(-20)), test.Store.DatabaseHealth(DateTime.UtcNow).GrowthFrom?.Day);
    }

    [Fact]
    public void The_warning_limits_are_checked_saved_and_audited_and_a_low_disk_warns()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.DoesNotContain(store.HealthWarnings(DateTime.UtcNow), x => x.StartsWith("Saves are slow"));
        Assert.False(store.SetHealthLimits(10, 2).Ok);
        Assert.False(store.SetHealthLimits(500, -1).Ok);

        // Every real disk has less than the largest limit free.
        Assert.True(store.SetHealthLimits(750, HelpdeskStore.MaxLowDiskWarningGb).Ok);
        Assert.Contains(store.HealthWarnings(DateTime.UtcNow), x => x.Contains("free, under the"));
        Assert.Contains(store.GetAuditEntries(), x => x.Entity == "Database health warnings" && x.Action == "Updated");

        var reopened = test.Reopen();
        Assert.Equal(750, reopened.SlowSaveWarningMs);
        Assert.Equal(HelpdeskStore.MaxLowDiskWarningGb, reopened.LowDiskWarningGb);

        Assert.True(reopened.SetHealthLimits(750, 0).Ok);
        Assert.DoesNotContain(reopened.HealthWarnings(DateTime.UtcNow), x => x.Contains("free, under the"));
    }
}
