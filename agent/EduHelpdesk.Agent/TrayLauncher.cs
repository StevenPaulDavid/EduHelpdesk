using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EduHelpdesk.Agent;

// The service starts the tray icon in every signed-in session, as that session's user, with no console window. Doing it
// from the service rather than a Run key means the icon appears straight after an install or update (not at the next
// sign-in), comes back if someone closes it, and never flashes a console window. The tray's own mutex (Tray.cs) stops
// a second copy in a session.
public static class TrayLauncher
{
    private const int WtsActive = 0;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;

    // At service start: tray icons from before (an older exe after an update) go, so the current one starts instead.
    public static void StopOldTrays()
    {
        foreach (var process in Process.GetProcessesByName("EduHelpdesk.Agent"))
            using (process)
            {
                if (process.Id == Environment.ProcessId || process.SessionId == 0) continue;
                try { process.Kill(); } catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
    }

    // Starts the tray in each active session that hasn't got one. Only works as SYSTEM (the service).
    public static void EnsureTrays()
    {
        var running = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("EduHelpdesk.Agent"))
            using (process) running.Add(process.SessionId);
        foreach (var session in ActiveSessions())
            if (!running.Contains(session)) Launch(session);
    }

    // Who is signed in to each active session, as SIDs, for reading their answer to a remote support request.
    public static List<string> ActiveUserSids()
    {
        var sids = new List<string>();
        foreach (var session in ActiveSessions())
        {
            if (!WTSQueryUserToken((uint)session, out var token)) continue;
            try
            {
                using var identity = new System.Security.Principal.WindowsIdentity(token);
                if (identity.User?.Value is { } sid) sids.Add(sid);
            }
            finally { CloseHandle(token); }
        }
        return sids;
    }

    public static bool AnyoneSignedIn() => ActiveSessions().Count > 0;

    private static List<int> ActiveSessions()
    {
        var sessions = new List<int>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var info, out var count)) return sessions;
        try
        {
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var session = Marshal.PtrToStructure<WtsSessionInfo>(info + i * size);
                if (session.State == WtsActive && session.SessionId != 0) sessions.Add(session.SessionId);
            }
        }
        finally { WTSFreeMemory(info); }
        return sessions;
    }

    private static void Launch(int session)
    {
        if (!WTSQueryUserToken((uint)session, out var token)) return;
        var environment = IntPtr.Zero;
        try
        {
            CreateEnvironmentBlock(out environment, token, false);
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = @"winsta0\default" };
            var exe = Environment.ProcessPath!;
            var command = new StringBuilder($"\"{exe}\" tray");
            if (CreateProcessAsUser(token, exe, command, IntPtr.Zero, IntPtr.Zero, false, CreateUnicodeEnvironment | CreateNoWindow, environment,
                    Path.GetDirectoryName(exe), ref startup, out var started))
            {
                CloseHandle(started.hThread);
                CloseHandle(started.hProcess);
            }
            else AgentLog.Write($"Couldn't start the tray icon for session {session} (error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll")]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
