using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EduHelpdesk.Services;

// The error log: warnings and errors written to a file a day in the data folder's logs folder, kept for 30 days. The
// console window the app used to report to is gone once it runs as a service, and with it every clue to what went
// wrong. Each entry carries the request's reference - the one the error page shows - so a person's "it said
// 0HN...:00000001" leads straight to the entry. Settings → Error log reads it back (FileLog.Read).
public sealed class FileLogProvider : ILoggerProvider, ISupportExternalScope
{
    public const int KeepDays = 30;
    private readonly string _folder;
    private readonly Lock _sync = new();
    private IExternalScopeProvider? _scopes;
    private DateOnly _prunedOn;

    public FileLogProvider(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public string Folder => _folder;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    public void Dispose() { }

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        // The request's reference, from the logging scope ASP.NET opens for every request.
        string? reference = null;
        _scopes?.ForEachScope((scope, _) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> values)
                foreach (var pair in values)
                    if (pair.Key == "RequestId" && pair.Value is { } id) reference = id.ToString();
        }, (object?)null);

        var now = DateTime.Now;
        var entry = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(' ')
            .Append(FileLog.LevelName(level)).Append(' ')
            .Append('[').Append(reference ?? "-").Append("] ")
            .Append(category).Append(": ")
            .Append(message.ReplaceLineEndings(" "))
            .AppendLine();
        if (exception is not null) entry.AppendLine(Indent(exception.ToString()));

        lock (_sync)
        {
            try
            {
                File.AppendAllText(Path.Combine(_folder, FileLog.FileName(DateOnly.FromDateTime(now))), entry.ToString());
                var today = DateOnly.FromDateTime(now);
                if (_prunedOn != today) { _prunedOn = today; Prune(today); }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Indent(string text) => "    " + text.ReplaceLineEndings("\n    ");

    private void Prune(DateOnly today)
    {
        foreach (var file in Directory.EnumerateFiles(_folder, "eduhelpdesk-*.log"))
            if (FileLog.DayOf(file) is { } day && day < today.AddDays(-KeepDays))
                try { File.Delete(file); } catch (IOException) { }
    }

    private sealed class FileLogger(FileLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes?.Push(state);
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}

public static class FileLog
{
    public sealed record Entry(DateTime At, string Level, string Reference, string Source, string Message, string Details);

    public static string FileName(DateOnly day) => $"eduhelpdesk-{day:yyyyMMdd}.log";
    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Critical => "CRITICAL",
        LogLevel.Error => "ERROR",
        LogLevel.Warning => "WARNING",
        _ => level.ToString().ToUpperInvariant()
    };

    public static DateOnly? DayOf(string path) =>
        DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path).Replace("eduhelpdesk-", ""), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static readonly Regex Head = new(@"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d) (\w+) \[([^\]]*)\] ([^:]+): (.*)$", RegexOptions.Compiled);

    // The newest entries first, from the newest files back, up to `max`.
    public static IReadOnlyList<Entry> Read(string folder, int max = 200)
    {
        var entries = new List<Entry>();
        if (!Directory.Exists(folder)) return entries;
        foreach (var file in Directory.EnumerateFiles(folder, "eduhelpdesk-*.log").OrderByDescending(x => x, StringComparer.Ordinal))
        {
            string[] lines;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                lines = reader.ReadToEnd().Split('\n');
            }
            catch (IOException) { continue; }
            var inFile = new List<Entry>();
            Entry? current = null;
            var details = new StringBuilder();
            void Finish()
            {
                if (current is not null) inFile.Add(current with { Details = details.ToString().TrimEnd() });
                details.Clear();
            }
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                var match = Head.Match(line);
                if (match.Success)
                {
                    Finish();
                    current = new Entry(DateTime.ParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value, match.Groups[5].Value, "");
                }
                else if (current is not null && line.Length > 0) details.AppendLine(line.StartsWith("    ") ? line[4..] : line);
            }
            Finish();
            inFile.Reverse();
            entries.AddRange(inFile);
            if (entries.Count >= max) break;
        }
        return entries.Take(max).ToList();
    }

    public static int CountSince(string folder, DateTime since, string level = "ERROR") =>
        Read(folder, 2000).Count(x => x.At >= since && (x.Level == level || x.Level == "CRITICAL"));
}
