using System.Security.Principal;
using System.Text.Json;
using EduHelpdesk.Agent;
using Microsoft.Extensions.Hosting.WindowsServices;

// EduHelpdesk Agent. Run by Windows as the "EduHelpdesk Agent" service, as the tray icon at each sign-in ("tray"), or
// from an administrator command prompt:
//
//   EduHelpdesk.Agent.exe install --server https://helpdesk.school.local --key ehe_... [--fingerprint ab12...]
//       Copies itself to Program Files, saves the settings, and installs and starts the service, which shows the tray
//       icon to whoever is signed in. Silent, so Intune can run it as a Win32 app. Run again (or a newer exe) to change
//       the settings or update the agent: the PC keeps its enrolment. With no options it keeps the saved settings.
//   EduHelpdesk.Agent.exe uninstall     Stops and removes the service, the tray icons and the settings.
//   EduHelpdesk.Agent.exe report        Sends a report now and says what happened.
//   EduHelpdesk.Agent.exe update        Reports, and updates to the helpdesk's newer agent now if it has one.
//   EduHelpdesk.Agent.exe collect       Shows what would be sent, without sending it.
// Everything they say also goes in agent.log.

// The service starts this in each signed-in session, with no window (TrayLauncher).
if (args.Length > 0 && args[0].Equals("tray", StringComparison.OrdinalIgnoreCase)) return Tray.Run();

if (WindowsServiceHelpers.IsWindowsService() || args.Length == 0 || args[0] == "run")
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options => options.ServiceName = Installer.ServiceName);
    builder.Services.AddHostedService<AgentService>();
    builder.Build().Run();
    return 0;
}

AgentLog.ToConsole = true;
switch (args[0].ToLowerInvariant())
{
    case "install": return await Install(args);
    case "uninstall": return Uninstall();
    case "report": return await ReportNow();
    case "update": return await UpdateNow();
    case "collect":
        Console.WriteLine(JsonSerializer.Serialize(Collector.Collect(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return 0;
    default:
        Console.WriteLine("Use: EduHelpdesk.Agent.exe install --server <https address> --key <enrolment key> [--fingerprint <certificate fingerprint>]");
        Console.WriteLine("     EduHelpdesk.Agent.exe uninstall | report | update | collect");
        return 2;
}

async Task<int> Install(string[] arguments)
{
    if (!IsAdministrator()) return Fail("Run this from a command prompt opened as an administrator.");
    string? Option(string name)
    {
        var index = Array.FindIndex(arguments, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
    var existing = AgentConfig.Load();
    var server = (Option("--server") ?? existing?.Server ?? "").Trim().TrimEnd('/');
    var key = (Option("--key") ?? existing?.EnrolmentKey ?? "").Trim();
    if (!Uri.TryCreate(server, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        return Fail("Give the helpdesk's address with --server, starting https:// (as on the Device agent page).");
    if (key.Length == 0) return Fail("Give the enrolment key with --key (it's in the install command on the Device agent page).");

    var config = existing ?? new AgentConfig();
    // A different helpdesk doesn't know this PC's token.
    if (!string.Equals(config.Server, server, StringComparison.OrdinalIgnoreCase)) config.Token = "";
    config.Server = server;
    config.EnrolmentKey = key;
    if (Option("--fingerprint") is { } fingerprint) config.Fingerprint = AgentConfig.NormaliseFingerprint(fingerprint);
    AgentConfig.LockFolder();
    config.Save();

    try
    {
        Installer.StopService();
        Installer.CopySelf();
        if (Installer.RegisterService() is { } problem) return Fail(problem);
        Installer.StartService();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ServiceProcess.TimeoutException)
    {
        return Fail($"Installing failed: {exception.Message}");
    }
    AgentLog.Write($"{Installer.DisplayName} {Collector.Version} is installed and running. It reports to {server} in about 30 seconds, then daily; the tray icon appears within a minute.");
    AgentLog.Write($"Checking the helpdesk: {await new HelpdeskClient(config).CheckAsync(CancellationToken.None)}");
    return 0;
}

int Uninstall()
{
    if (!IsAdministrator()) return Fail("Run this from a command prompt opened as an administrator.");
    try
    {
        Installer.StopService();
        Installer.DeleteService();
        Installer.StopTrays();
        AgentStatus.Delete();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ServiceProcess.TimeoutException)
    {
        return Fail($"Removing the service failed: {exception.Message}");
    }
    AgentLog.ToConsole = true;
    try { if (Directory.Exists(AgentConfig.Folder)) Directory.Delete(AgentConfig.Folder, recursive: true); }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Console.WriteLine($"Couldn't remove {AgentConfig.Folder}: {exception.Message}"); }
    var folder = Installer.ProgramFolder;
    var runningFromIt = Path.GetFullPath(Environment.ProcessPath!).StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
    if (!runningFromIt && Directory.Exists(folder))
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Console.WriteLine($"Couldn't remove {folder}: {exception.Message}"); }
    Console.WriteLine($"{Installer.DisplayName} is removed.{(runningFromIt ? $" Delete {folder} once this window is closed." : "")} The PC stays on the Device agent page until someone removes it there.");
    return 0;
}

async Task<int> ReportNow()
{
    if (!IsAdministrator()) Console.WriteLine("Not running as an administrator: TPM and BitLocker can't be read, and the settings may not be readable.");
    var config = AgentConfig.Load();
    if (config is null) return Fail($"No settings at {AgentConfig.FilePath}. Install the agent first.");
    var outcome = await new HelpdeskClient(config).SendAsync(Collector.Collect(), CancellationToken.None);
    AgentLog.Write(outcome.Message);
    if (outcome.Ok && IsAdministrator()) AgentStatus.Write(outcome.Answer?.AssetTag, outcome.Answer?.Status ?? "", outcome.Answer?.Message);
    if (Updater.Newer(outcome.Answer) is { } newer) Console.WriteLine($"The helpdesk has a newer agent ({newer}). The service updates itself to it within a couple of hours.");
    return outcome.Ok ? 0 : 1;
}

// Reports, and if the helpdesk has a newer agent, updates to it now rather than within a couple of hours.
async Task<int> UpdateNow()
{
    var config = AgentConfig.Load();
    if (config is null) return Fail($"No settings at {AgentConfig.FilePath}. Install the agent first.");
    var outcome = await new HelpdeskClient(config).SendAsync(Collector.Collect(), CancellationToken.None);
    if (!outcome.Ok) return Fail(outcome.Message);
    if (Updater.Newer(outcome.Answer) is not { } newer) { Console.WriteLine($"Already up to date: this is agent {Collector.Version}, and the helpdesk hands out {outcome.Answer?.AgentVersion ?? "none"}."); return 0; }
    var (exe, message) = await Updater.DownloadAsync(config, outcome.Answer!.AgentSha256 ?? "", CancellationToken.None);
    if (exe is null) return Fail($"Couldn't download agent {newer}: {message}");
    if (!IsAdministrator()) { Console.WriteLine($"Downloaded and checked agent {newer} to {exe}. Run it with install, as an administrator, to finish."); return 0; }
    Console.WriteLine($"Installing agent {newer}...");
    using var install = Updater.Start(exe);
    install.WaitForExit();
    Console.WriteLine(install.ExitCode == 0 ? $"Updated to agent {newer}." : $"The update failed (exit code {install.ExitCode}); see {Path.Combine(AgentConfig.Folder, "agent.log")}.");
    return install.ExitCode;
}

static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

static int Fail(string message)
{
    AgentLog.Write(message);
    return 1;
}
