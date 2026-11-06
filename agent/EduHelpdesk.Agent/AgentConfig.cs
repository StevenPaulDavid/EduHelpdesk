using System.Diagnostics;
using System.Text.Json;

namespace EduHelpdesk.Agent;

// What the agent knows about its helpdesk, kept in %ProgramData%\EduHelpdesk Agent\agent.json. The folder is locked to
// SYSTEM and Administrators when the agent is installed, because the token in it lets whoever has it report as this PC
// and the key enrols any PC.
public sealed class AgentConfig
{
    public string Server { get; set; } = "";
    public string EnrolmentKey { get; set; } = "";
    // SHA-256 of the helpdesk's certificate, lower-case hex: trust that one certificate rather than Windows' list. Most
    // schools' certificates are self-signed. Blank means trust what Windows trusts.
    public string Fingerprint { get; set; } = "";
    public string Token { get; set; } = "";

    // EDUHELPDESK_AGENT_FOLDER moves it, for trying the agent out (report, collect) without installing it.
    public static string Folder => Environment.GetEnvironmentVariable("EDUHELPDESK_AGENT_FOLDER") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EduHelpdesk Agent");
    public static string FilePath => Path.Combine(Folder, "agent.json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static AgentConfig? Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(FilePath), Json) : null; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            AgentLog.Write($"The settings file couldn't be read: {exception.Message}");
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json));
        File.Move(temporary, FilePath, overwrite: true);
    }

    // Only SYSTEM and Administrators, by SID so it works on a Windows in any language. Done at install, as an administrator.
    public static void LockFolder()
    {
        Directory.CreateDirectory(Folder);
        Run("icacls", $"\"{Folder}\" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F /T /Q");
    }

    // "AB:CD 12 ..." in any spacing or case, as the Device agent page shows it, to plain lower-case hex.
    public static string NormaliseFingerprint(string? text) =>
        new string((text ?? "").Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();

    internal static int Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}

// A small log beside the settings, for looking into a PC that isn't reporting. Kept under 1 MB: the old half is dropped.
public static class AgentLog
{
    private static readonly Lock Sync = new();
    public static bool ToConsole { get; set; }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        if (ToConsole) Console.WriteLine(message);
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(AgentConfig.Folder);
                var path = Path.Combine(AgentConfig.Folder, "agent.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                {
                    var lines = File.ReadAllLines(path);
                    File.WriteAllLines(path, lines.Skip(lines.Length / 2));
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
