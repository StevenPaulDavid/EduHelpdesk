using Microsoft.Extensions.Logging.Abstractions;

namespace EduHelpdesk.Tests;

// The installer's answers, applied on the first start (Services/InstallSettings.cs).
public class InstallSettingsTests
{
    [Fact]
    public void The_installers_answers_are_applied_once_and_bad_ones_ignored()
    {
        using var test = new TestStore();
        var data = Path.Combine(test.Root, "App_Data");
        var backups = Path.Combine(test.Root, "backups-elsewhere");
        File.WriteAllText(Path.Combine(data, InstallSettings.FileName),
            $$"""{ "SchoolName": "Test Academy", "PrimaryColor": "#1f4e79", "AccentColor": "not a colour", "LogoPath": "{{Path.Combine(test.Root, "missing.png").Replace("\\", "\\\\")}}", "BackupFolder": "{{backups.Replace("\\", "\\\\")}}", "BackupHour": 4 }""");

        InstallSettings.Apply(test.Store, data, NullLogger.Instance);

        Assert.Equal("Test Academy", test.Store.Branding.BrandName);
        Assert.Equal("#1F4E79", test.Store.Branding.PrimaryColor);
        Assert.Equal("#E8F0EF", test.Store.Branding.AccentColor);
        Assert.Equal(backups, test.Store.Backups.Folder);
        Assert.Equal(4, test.Store.Backups.Hour);
        Assert.False(File.Exists(Path.Combine(data, InstallSettings.FileName)));
        Assert.True(File.Exists(Path.Combine(data, "install-settings.applied.json")));

        // A later start finds nothing to apply, so a name changed in Settings since then stays.
        var branding = test.Store.Branding;
        branding.BrandName = "Renamed";
        test.Store.UpdateBranding(branding);
        InstallSettings.Apply(test.Reopen(), data, NullLogger.Instance);
        Assert.Equal("Renamed", test.Store.Branding.BrandName);
    }
}