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

    // ---- Remote support: the service puts the request here for the tray (HKLM, read-only to users), and the tray puts
    // the person's answer in their own HKCU, which the service reads through HKEY_USERS. Nobody can change a request,
    // and an answer can only come from someone signed in to the PC. ----

    private const string AnswerPath = @"Software\EduHelpdesk Agent";

    public static void WriteRemoteRequest(AgentContract.RemoteOffer? offer)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.CreateSubKey(KeyPath);
            if (offer is null) key.DeleteValue("RemoteRequest", throwOnMissingValue: false);
            else key.SetValue("RemoteRequest", System.Text.Json.JsonSerializer.Serialize(offer, Json));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AgentLog.Write($"Couldn't pass the remote support request to the tray icon: {exception.Message}");
        }
    }

    public static AgentContract.RemoteOffer? ReadRemoteRequest()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(KeyPath);
            return key?.GetValue("RemoteRequest") is string text ? System.Text.Json.JsonSerializer.Deserialize<AgentContract.RemoteOffer>(text, Json) : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // From the tray, as the signed-in person: "<request id>:accepted" (or declined, unavailable).
    public static void WriteRemoteAnswer(Guid id, string answer)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AnswerPath);
            key.SetValue("RemoteAnswer", $"{id}:{answer}");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    // From the service: the answer for this request from any of these users (SIDs), taken so it isn't read twice.
    public static string? TakeRemoteAnswer(Guid id, IEnumerable<string> sids)
    {
        foreach (var sid in sids)
        {
            try
            {
                using var key = Registry.Users.OpenSubKey($@"{sid}\{AnswerPath}", writable: true);
                if (key?.GetValue("RemoteAnswer") is not string value) continue;
                key.DeleteValue("RemoteAnswer", throwOnMissingValue: false);
                var prefix = $"{id}:";
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return value[prefix.Length..];
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        }
        return null;
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

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
