using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace EduHelpdesk.Pages.Settings;

// The upload, read once and held in memory between steps - never written to disk: the export is the school's real
// tickets, and some are safeguarding matters.
public sealed class SpiceworksImportSession
{
    public string FileName { get; init; } = "";
    public SpiceworksExport Export { get; init; } = null!;
    public Dictionary<string, string> Choices { get; set; } = [];
}

// Settings → Imports → From Spiceworks. Needs Settings: Edit, like every page under /Settings.
public class SpiceworksImportModel(HelpdeskStore store, IMemoryCache cache) : PageModel
{
    private const long MaxBytes = 50_000_000;

    [BindProperty(SupportsGet = true, Name = "t")] public string? Token { get; set; }
    [TempData] public string? Message { get; set; }

    public SpiceworksImportSession? Session { get; private set; }
    public HelpdeskStore.SpiceworksPlan? Plan { get; private set; }
    public int AlreadyImported { get; private set; }
    public IReadOnlyList<HelpdeskStore.SpiceworksImportRecord> Imports => store.SpiceworksImports;

    public void OnGet()
    {
        Session = Load(Token);
        if (Session is null)
        {
            if (!string.IsNullOrWhiteSpace(Token)) Message ??= "That upload has expired. Upload the file again.";
            return;
        }
        Plan = store.PlanSpiceworksImport(Session.Export, Session.Choices);
        AlreadyImported = store.SpiceworksAlreadyImported(Session.Export);
    }

    public IActionResult OnPostApply(string? t)
    {
        var session = Load(t);
        if (session is null) { Message = "That upload has expired. Upload the file again."; return RedirectToPage(); }
        var (ok, message, _) = store.ApplySpiceworksImport(session.Export, session.Choices, session.FileName);
        Message = message;
        if (!ok) return RedirectToPage(new { t });
        // Done with: the file isn't kept a moment longer than it's needed.
        cache.Remove(CacheKey(t!));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) { Message = "Choose the Spiceworks export file first."; return RedirectToPage(); }
        if (file.Length > MaxBytes) { Message = "That file is larger than 50 MB."; return RedirectToPage(); }
        // The workbook reader needs to move around the file, so it is read from a copy in memory.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        buffer.Position = 0;
        var (export, error) = SpiceworksExport.Read(buffer);
        if (export is null) { Message = error; return RedirectToPage(); }
        if (export.Tickets.Count == 0) { Message = "The export's Tickets sheet has no tickets in it."; return RedirectToPage(); }

        var token = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(token), new SpiceworksImportSession { FileName = Path.GetFileName(file.FileName), Export = export },
            new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(45) });
        return RedirectToPage(new { t = token });
    }

    // The value choices from the preview. Only keys the preview offered are kept; the plan checks each target is real.
    public IActionResult OnPostChoices(string? t, Dictionary<string, string>? choice)
    {
        var session = Load(t);
        if (session is null) { Message = "That upload has expired. Upload the file again."; return RedirectToPage(); }
        var offered = store.PlanSpiceworksImport(session.Export, session.Choices).Values.Select(x => x.Key).ToHashSet();
        session.Choices = (choice ?? []).Where(x => offered.Contains(x.Key) && !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.Key, x => x.Value);
        Message = "Choices saved. The preview below uses them.";
        return RedirectToPage(null, null, new { t }, "values");
    }

    public IActionResult OnPostForget(string? t)
    {
        if (!string.IsNullOrWhiteSpace(t)) cache.Remove(CacheKey(t));
        Message = "The upload has been cleared from memory.";
        return RedirectToPage();
    }

    private SpiceworksImportSession? Load(string? token) =>
        !string.IsNullOrWhiteSpace(token) && cache.TryGetValue(CacheKey(token), out SpiceworksImportSession? session) ? session : null;

    private static string CacheKey(string token) => "spiceworks-import:" + token;

    public static string Plural(int count, string one, string many) => $"{count:N0} {(count == 1 ? one : many)}";
}
