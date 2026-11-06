using System.Diagnostics;
using System.ServiceProcess;

namespace EduHelpdesk.Agent;

// Putting the agent on a PC and taking it off: the exe in Program Files and the Windows service (which starts the tray
// icons itself). Used by "install" and "uninstall" (Program.cs), and by an update, which runs the new exe's "install".
public static class Installer
{
    public const string ServiceName = "EduHelpdeskAgent";
    public const string DisplayName = "EduHelpdesk Agent";

    public static string ProgramFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EduHelpdesk Agent");
    public static string TargetExe => Path.Combine(ProgramFolder, "EduHelpdesk.Agent.exe");

    // Copies this exe into Program Files. A tray icon still running the old one holds the file open, but a running exe
    // can be renamed: it moves aside to .old and carries on until its user signs out; the next start cleans it up.
    public static void CopySelf()
    {
        var source = Path.GetFullPath(Environment.ProcessPath!);
        if (string.Equals(source, Path.GetFullPath(TargetExe), StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(ProgramFolder);
        try { File.Copy(source, TargetExe, overwrite: true); }
        catch (IOException)
        {
            var aside = TargetExe + ".old";
            try { File.Delete(aside); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { aside = TargetExe + $".{DateTime.UtcNow:yyyyMMddHHmmss}.old"; }
            File.Move(TargetExe, aside);
            File.Copy(source, TargetExe);
        }
    }

    // Old exes moved aside by an update, once nothing is running them.
    public static void CleanUp()
    {
        if (!Directory.Exists(ProgramFolder)) return;
        foreach (var old in Directory.EnumerateFiles(ProgramFolder, "*.old"))
            try { File.Delete(old); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public static bool ServiceExists() => ServiceController.GetServices().Any(x => x.ServiceName == ServiceName);

    // Creates the service, or points the existing one at Program Files again. Null when it worked, else why not.
    public static string? RegisterService()
    {
        var binPath = $"binPath= \"\\\"{TargetExe}\\\"\"";
        var exists = ServiceExists();
        var made = exists
            ? AgentConfig.Run("sc.exe", $"config {ServiceName} {binPath} start= delayed-auto")
            : AgentConfig.Run("sc.exe", $"create {ServiceName} {binPath} start= delayed-auto DisplayName= \"{DisplayName}\"");
        if (made != 0) return $"Windows wouldn't {(exists ? "update" : "create")} the service (sc.exe exit code {made}).";
        AgentConfig.Run("sc.exe", $"description {ServiceName} \"Keeps this PC's details up to date in EduHelpdesk.\"");
        // Restart after a crash: a minute, a minute, then ten.
        AgentConfig.Run("sc.exe", $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/600000");
        return null;
    }

    public static void StartService()
    {
        using var service = new ServiceController(ServiceName);
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    public static void StopService()
    {
        if (!ServiceExists()) return;
        using var service = new ServiceController(ServiceName);
        if (service.Status is ServiceControllerStatus.Stopped) return;
        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    public static void DeleteService()
    {
        if (ServiceExists()) AgentConfig.Run("sc.exe", $"delete {ServiceName}");
    }

    // Every tray icon still running (the service starts them, TrayLauncher), so the folder can go.
    public static void StopTrays()
    {
        foreach (var process in Process.GetProcessesByName("EduHelpdesk.Agent"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try { process.Kill(); } catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }
}
