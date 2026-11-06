using System.Diagnostics;
using System.Security.Cryptography;

namespace EduHelpdesk.Services;

// The EduHelpdesk Agent exe this helpdesk hands out: agent-app\EduHelpdesk.Agent.exe beside the program, put there by
// deploy/New-Release.ps1 so a school's agent and helpdesk move together. PCs are told its version and hash with every
// report and update themselves from /api/agent/v1/agent (Services/AgentEndpoints.cs); the Device agent page offers it
// for download. Its version and hash are worked out once per file, not on every report.
public static class AgentPackage
{
    public const string Folder = "agent-app";
    public const string FileName = "EduHelpdesk.Agent.exe";

    public sealed record Package(string Path, Version Version, string Sha256, long Length)
    {
        public string VersionText => Version.ToString(3);
    }

    private static readonly Lock Sync = new();
    private static (string Path, DateTime Written, long Length, Package? Package)? _cached;

    public static Package? Latest(IWebHostEnvironment environment) => Latest(Path.Combine(environment.ContentRootPath, Folder));

    public static Package? Latest(string folder)
    {
        var path = Path.Combine(folder, FileName);
        var file = new FileInfo(path);
        if (!file.Exists) return null;
        lock (Sync)
        {
            if (_cached is { } cached && cached.Path == path && cached.Written == file.LastWriteTimeUtc && cached.Length == file.Length) return cached.Package;
            Package? package = null;
            try
            {
                // ProductVersion carries the version the agent says it is ("0.2.0+abc123" from a git build).
                var info = FileVersionInfo.GetVersionInfo(path);
                var text = (info.ProductVersion ?? info.FileVersion ?? "").Split('+')[0].Trim();
                if (Version.TryParse(text, out var version))
                {
                    using var stream = file.OpenRead();
                    package = new Package(path, new Version(version.Major, version.Minor, Math.Max(version.Build, 0)),
                        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), file.Length);
                }
            }
            catch (IOException) { }
            _cached = (path, file.LastWriteTimeUtc, file.Length, package);
            return package;
        }
    }
}
