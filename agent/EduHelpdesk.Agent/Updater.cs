using System.Diagnostics;
using EduHelpdesk.AgentContract;

namespace EduHelpdesk.Agent;

// The agent keeps itself up to date from the helpdesk it reports to. Each answer names the agent the helpdesk hands
// out; when that's newer, the service waits a random 0-2 hours (so a school's PCs don't all download at once after an
// upgrade), downloads it with its own token, checks its SHA-256 against the answer, and runs the new exe's "install",
// which stops this service, copies itself into Program Files and starts it again with the saved settings.
public sealed class Updater
{
    private static readonly TimeSpan Spread = TimeSpan.FromHours(2);
    private static readonly TimeSpan BackOff = TimeSpan.FromHours(24);

    private Version? _pending;
    private string? _pendingSha;
    private DateTime _due;
    // A version that failed to download or install isn't tried again for a day.
    private (Version Version, DateTime Until)? _failed;

    public static string UpdateFolder => Path.Combine(AgentConfig.Folder, "update");

    // The helpdesk's agent when it's newer than this one; otherwise null.
    public static Version? Newer(CheckInAnswer? answer) =>
        Version.TryParse(answer?.AgentVersion, out var offered) && Version.TryParse(Collector.Version, out var mine) && offered > mine ? offered : null;

    // After each successful report: note a newer agent and when to fetch it.
    public void Consider(CheckInAnswer? answer)
    {
        if (Newer(answer) is not { } offered || string.IsNullOrEmpty(answer!.AgentSha256)) return;
        if (_failed is { } failed && failed.Version == offered && DateTime.UtcNow < failed.Until) return;
        if (_pending == offered) { _pendingSha = answer.AgentSha256; return; }
        _pending = offered;
        _pendingSha = answer.AgentSha256;
        _due = DateTime.UtcNow + TimeSpan.FromMinutes(Random.Shared.Next((int)Spread.TotalMinutes));
        AgentLog.Write($"The helpdesk has agent {offered}. Updating at about {_due.ToLocalTime():HH:mm}.");
    }

    // On each tick of the service: true when an update was started, and this service is about to be stopped by it.
    public async Task<bool> RunIfDueAsync(CancellationToken cancel)
    {
        if (_pending is not { } version || DateTime.UtcNow < _due) return false;
        var sha = _pendingSha!;
        _pending = null;
        var config = AgentConfig.Load();
        if (config is null) return false;
        var (exe, message) = await DownloadAsync(config, sha, cancel);
        if (exe is null)
        {
            AgentLog.Write($"Couldn't update to agent {version}: {message}");
            _failed = (version, DateTime.UtcNow + BackOff);
            return false;
        }
        try
        {
            // Its own process, so it carries on when it stops this service. It logs to the same agent.log.
            Start(exe);
            AgentLog.Write($"Installing agent {version}.");
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            AgentLog.Write($"Couldn't start the update to agent {version}: {exception.Message}");
            _failed = (version, DateTime.UtcNow + BackOff);
            return false;
        }
    }

    // The new exe, downloaded and checked, in the update folder; or null and why not.
    public static async Task<(string? Exe, string Message)> DownloadAsync(AgentConfig config, string sha256, CancellationToken cancel)
    {
        Directory.CreateDirectory(UpdateFolder);
        var exe = Path.Combine(UpdateFolder, "EduHelpdesk.Agent.exe");
        var (ok, message) = await new HelpdeskClient(config).DownloadAgentAsync(exe, sha256, cancel);
        return (ok ? exe : null, message);
    }

    // Runs the new exe's "install", which replaces this one with the saved settings.
    public static Process Start(string exe) =>
        Process.Start(new ProcessStartInfo(exe, "install") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = UpdateFolder })!;
}
