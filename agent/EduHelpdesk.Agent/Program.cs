using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using EduHelpdesk.Agent;
using Microsoft.Extensions.Hosting.WindowsServices;

// EduHelpdesk Agent. Run by Windows as the "EduHelpdesk Agent" service, or from an administrator command prompt:
//
//   EduHelpdesk.Agent.exe install --server https://helpdesk.school.local --key ehe_... [--fingerprint ab12...]
//       Copies itself to Program Files, saves the settings, and installs and starts the service. Run again to change
//       the settings or update the agent: the PC keeps its enrolment.
//   EduHelpdesk.Agent.exe uninstall     Stops and removes the service and its settings.
//   EduHelpdesk.Agent.exe report        Sends a report now and says what happened.
//   EduHelpdesk.Agent.exe collect       Shows what would be sent, without sending it.
const string ServiceName = "EduHelpdeskAgent";
const string DisplayName = "EduHelpdesk Agent";

if (WindowsServiceHelpers.IsWindowsService() || args.Length == 0 || args[0] == "run")
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options => options.ServiceName = ServiceName);
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
    case "collect":
        Console.WriteLine(JsonSerializer.Serialize(Collector.Collect(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return 0;
    default:
        Console.WriteLine("Use: EduHelpdesk.Agent.exe install --server <https address> --key <enrolment key> [--fingerprint <certificate fingerprint>]");
        Console.WriteLine("     EduHelpdesk.Agent.exe uninstall | report | collect");
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

    StopService();
    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EduHelpdesk Agent");
    var target = Path.Combine(folder, "EduHelpdesk.Agent.exe");
    var source = Environment.ProcessPath!;
    if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
    {
        Directory.CreateDirectory(folder);
        File.Copy(source, target, overwrite: true);
    }

    var binPath = $"binPath= \"\\\"{target}\\\"\"";
    var exists = ServiceController.GetServices().Any(x => x.ServiceName == ServiceName);
    var made = exists
        ? AgentConfig.Run("sc.exe", $"config {ServiceName} {binPath} start= delayed-auto")
        : AgentConfig.Run("sc.exe", $"create {ServiceName} {binPath} start= delayed-auto DisplayName= \"{DisplayName}\"");
    if (made != 0) return Fail($"Windows wouldn't {(exists ? "update" : "create")} the service (sc.exe exit code {made}).");
    AgentConfig.Run("sc.exe", $"description {ServiceName} \"Keeps this PC's details up to date in EduHelpdesk.\"");
    AgentConfig.Run("sc.exe", $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/600000");
    using (var service = new ServiceController(ServiceName))
    {
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }
    Console.WriteLine($"{DisplayName} {Collector.Version} is installed and running. It reports to {server} in about 30 seconds, then daily.");
    Console.WriteLine($"Checking the helpdesk: {await new HelpdeskClient(config).CheckAsync(CancellationToken.None)}");
    return 0;
}

int Uninstall()
{
    if (!IsAdministrator()) return Fail("Run this from a command prompt opened as an administrator.");
    StopService();
    if (ServiceController.GetServices().Any(x => x.ServiceName == ServiceName)) AgentConfig.Run("sc.exe", $"delete {ServiceName}");
    try { if (Directory.Exists(AgentConfig.Folder)) Directory.Delete(AgentConfig.Folder, recursive: true); }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Console.WriteLine($"Couldn't remove {AgentConfig.Folder}: {exception.Message}"); }
    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EduHelpdesk Agent");
    var runningFromIt = Path.GetFullPath(Environment.ProcessPath!).StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
    if (!runningFromIt && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    Console.WriteLine($"{DisplayName} is removed.{(runningFromIt ? $" Delete {folder} once this window is closed." : "")} The PC stays on the Device agent page until someone removes it there.");
    return 0;
}

async Task<int> ReportNow()
{
    if (!IsAdministrator()) Console.WriteLine("Not running as an administrator: TPM and BitLocker can't be read, and the settings may not be readable.");
    var config = AgentConfig.Load();
    if (config is null) return Fail($"No settings at {AgentConfig.FilePath}. Install the agent first.");
    var outcome = await new HelpdeskClient(config).SendAsync(Collector.Collect(), CancellationToken.None);
    Console.WriteLine(outcome.Message);
    return outcome.Ok ? 0 : 1;
}

void StopService()
{
    if (!ServiceController.GetServices().Any(x => x.ServiceName == ServiceName)) return;
    using var service = new ServiceController(ServiceName);
    if (service.Status is ServiceControllerStatus.Stopped) return;
    service.Stop();
    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
}

static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
