using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Error log: the warnings and errors from the log files (FileLogProvider), newest first. A reference from the
// error page finds its entry. Needs Settings: Edit, like the other pages under /Settings (Program.cs).
public class LogModel(FileLogProvider log) : PageModel
{
    public IReadOnlyList<FileLog.Entry> Entries { get; private set; } = [];
    public string Folder => log.Folder;
    public string? Reference { get; private set; }
    public bool ErrorsOnly { get; private set; }

    public void OnGet(string? reference, bool errors = false)
    {
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        ErrorsOnly = errors;
        var entries = FileLog.Read(log.Folder, 500).AsEnumerable();
        if (Reference is not null) entries = entries.Where(x => x.Reference.Contains(Reference, StringComparison.OrdinalIgnoreCase));
        if (ErrorsOnly) entries = entries.Where(x => x.Level is "ERROR" or "CRITICAL");
        Entries = entries.Take(200).ToList();
    }
}
