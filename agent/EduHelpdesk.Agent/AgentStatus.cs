using Microsoft.Win32;

namespace EduHelpdesk.Agent;

// What the tray icon shows, written by the service after each report to HKLM\SOFTWARE\EduHelpdesk Agent. HKLM is
// readable by every user and writable only by SYSTEM and administrators, so the tray (running as whoever signed in)
// can read it but nobody can change what it says. Nothing secret goes here: the token and key stay in agent.json.
public static class AgentStatus
{
    public const string KeyPath = @"SOFTWARE\EduHelpdesk Agent";

    public sealed record Snapshot(string AssetTag, string Status, string ComputerName, string Message, DateTime? ReportedAt);

    public static void Write(string? assetTag, string status, string? message)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.CreateSubKey(KeyPath);
            key.SetValue("AssetTag", assetTag ?? "");
            key.SetValue("Status", status);
            key.SetValue("ComputerName", Environment.MachineName);
            key.SetValue("Message", message ?? "");
            key.SetValue("ReportedAt", DateTime.UtcNow.ToString("O"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AgentLog.Write($"Couldn't record the status for the tray icon: {exception.Message}");
        }
    }

    public static Snapshot Read()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(KeyPath);
            string Text(string name) => key?.GetValue(name) as string ?? "";
            return new Snapshot(Text("AssetTag"), Text("Status"), Text("ComputerName") is { Length: > 0 } name ? name : Environment.MachineName, Text("Message"),
                DateTime.TryParse(Text("ReportedAt"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new Snapshot("", "", Environment.MachineName, "", null);
        }
    }

    public static void Delete()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            root.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }
}
