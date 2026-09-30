using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Everything stored about one ticket, asset, person or project: its rows from every table that mentions it. Reached
// from the record's own page ("Raw data") or Settings → Database.
public class DatabaseRecordModel(RawDatabase raw) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true)] public string? Key { get; set; }

    public RawDatabase.RecordKind Record { get; private set; } = null!;
    public IReadOnlyList<RawDatabase.Found> Found { get; private set; } = [];
    public bool SeesSecrets => RawDatabase.IsAdministrator(User);
    public string Subject => $"{Record.Label} {Key}";

    public IActionResult OnGet()
    {
        if (RawDatabase.FindKind(Kind) is not { } kind || string.IsNullOrWhiteSpace(Key)) return NotFound();
        Record = kind;
        Key = Key.Trim();
        Found = raw.ForRecord(kind, Key, SeesSecrets);
        raw.RecordLook("Viewed raw data", Subject, Found.Count == 0 ? "Nothing found" : $"{Found.Sum(x => x.Total):N0} rows in {Found.Count} tables");
        return Page();
    }
}
