using System.Text.Json;
using System.Text.RegularExpressions;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The answers a school gave the installer (installer/Install.ps1) that live in the database rather than in
// appsettings: school name and colours, logo, backup folder and hour. The installer can't write the database itself,
// so it leaves them in install-settings.json in the data folder, and the first start applies them through the store -
// validated and audited like any change made in Settings - then renames the file so it never applies twice.
public static class InstallSettings
{
    public const string FileName = "install-settings.json";

    private sealed record Answers(string? SchoolName, string? PrimaryColor, string? AccentColor, string? LogoPath, string? BackupFolder, int? BackupHour);

    public static void Apply(HelpdeskStore store, string dataFolder, ILogger logger)
    {
        var path = Path.Combine(dataFolder, FileName);
        if (!File.Exists(path)) return;
        Answers? answers;
        try { answers = JsonSerializer.Deserialize<Answers>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The installer's settings file {Path} couldn't be read, so its answers weren't applied. Set them in Settings instead.", path);
            MarkApplied(path);
            return;
        }
        if (answers is null) { MarkApplied(path); return; }

        var branding = store.Branding;
        var colour = new Regex("^#[0-9A-Fa-f]{6}$");
        var updated = new BrandingSettings
        {
            BrandName = string.IsNullOrWhiteSpace(answers.SchoolName) ? branding.BrandName : answers.SchoolName.Trim()[..Math.Min(80, answers.SchoolName.Trim().Length)],
            DashboardEyebrow = branding.DashboardEyebrow,
            DashboardTitle = branding.DashboardTitle,
            DashboardDescription = branding.DashboardDescription,
            PrimaryColor = answers.PrimaryColor is { } primary && colour.IsMatch(primary) ? primary.ToUpperInvariant() : branding.PrimaryColor,
            AccentColor = answers.AccentColor is { } accent && colour.IsMatch(accent) ? accent.ToUpperInvariant() : branding.AccentColor,
            BackgroundColor = branding.BackgroundColor,
            DefaultAppearance = branding.DefaultAppearance
        };
        store.UpdateBranding(updated);

        if (!string.IsNullOrWhiteSpace(answers.LogoPath))
        {
            try
            {
                using var logo = File.OpenRead(answers.LogoPath);
                var (ok, message) = store.SaveLogo(logo, logo.Length);
                if (!ok) logger.LogWarning("The logo from the installer ({Path}) wasn't used: {Message}", answers.LogoPath, message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The logo from the installer ({Path}) couldn't be read. Upload it in Settings → Branding & logo.", answers.LogoPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(answers.BackupFolder) || answers.BackupHour is not null)
        {
            var backups = store.Backups;
            var (ok, message) = store.SetBackupSettings(true, string.IsNullOrWhiteSpace(answers.BackupFolder) ? null : answers.BackupFolder.Trim(),
                backups.KeepDays, answers.BackupHour is >= 0 and <= 23 ? answers.BackupHour.Value : backups.Hour);
            if (!ok) logger.LogWarning("The backup settings from the installer weren't used: {Message}", message);
        }

        MarkApplied(path);
        logger.LogInformation("Applied the installer's settings from {Path}.", path);
    }

    // Kept rather than deleted, as a record of what the installer was told.
    private static void MarkApplied(string path)
    {
        var applied = Path.Combine(Path.GetDirectoryName(path)!, "install-settings.applied.json");
        File.Move(path, applied, overwrite: true);
    }
}
