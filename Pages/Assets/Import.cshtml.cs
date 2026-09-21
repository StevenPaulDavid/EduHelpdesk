using System.Text;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace EduHelpdesk.Pages.Assets;

// The upload, held on the server between the steps so a large file is not posted again at each one.
public sealed class AssetImportSession
{
    public string FileName { get; init; } = "";
    public string[] Headers { get; init; } = [];
    public List<string[]> Rows { get; init; } = [];
    public string?[] Targets { get; set; } = [];
    public AssetImportOptions Options { get; set; } = new("dmy", true, false, null, null);
    public bool Mapped { get; set; }
}

public class ImportModel(HelpdeskStore store, IMemoryCache cache) : PageModel
{
    private const int MaxBytes = 10_000_000;
    private const int MaxRows = 20_000;
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true, Name = "t")] public string? Token { get; set; }
    [BindProperty(SupportsGet = true, Name = "step")] public string? Step { get; set; }
    [BindProperty(SupportsGet = true, Name = "show")] public string? Show { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    // Messages that follow a redirect (TempData) and one for the page being shown now.
    [TempData] public string? Message { get; set; }
    public string? Notice { get; private set; }

    public AssetImportSession? Session { get; private set; }
    public AssetImportPlan? Plan { get; private set; }
    public IReadOnlyList<ImportRowResult> ShownRows { get; private set; } = [];
    public int ShownCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    // upload, map or preview
    public string Stage { get; private set; } = "upload";

    public IReadOnlyList<AssetAttributeDefinition> Attributes => store.AssetAttributeDefinitions;
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<string> Statuses => store.AssetStatuses;

    public void OnGet()
    {
        Session = Load(Token);
        if (Session is null)
        {
            if (!string.IsNullOrWhiteSpace(Token)) Notice = "That import has expired. Upload the file again.";
            Stage = "upload";
            return;
        }
        if (!Session.Mapped || Step == "map")
        {
            Stage = "map";
            return;
        }
        Stage = "preview";
        Plan = store.PlanAssetImport(Session.Rows, Session.Targets, Session.Options);
        var show = Show is "create" or "update" or "unchanged" or "error" ? Show : "all";
        Show = show;
        var rows = Plan.Results.Where(x => show switch
        {
            "create" => x.Action == ImportAction.Create,
            "update" => x.Action == ImportAction.Update,
            "unchanged" => x.Action == ImportAction.Unchanged,
            "error" => x.Action == ImportAction.Error,
            _ => true
        }).ToList();
        ShownCount = rows.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        ShownRows = rows.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file, string? delimiter)
    {
        if (file is null || file.Length == 0) { Message = "Choose a CSV file first."; return RedirectToPage(); }
        if (file.Length > MaxBytes) { Message = "That file is larger than 10 MB. Split it into smaller files."; return RedirectToPage(); }
        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream);
            bytes = stream.ToArray();
        }
        var separator = delimiter switch { "comma" => ',', "semicolon" => ';', "tab" => '\t', _ => (char?)null };
        var csv = CsvReader.Parse(CsvReader.Decode(bytes), separator);
        if (csv.Rows.Count < 2) { Message = "The file needs a header row and at least one row of assets."; return RedirectToPage(); }
        if (csv.Rows.Count - 1 > MaxRows) { Message = $"That file has more than {MaxRows:N0} rows. Split it into smaller files."; return RedirectToPage(); }

        var headers = csv.Rows[0];
        var session = new AssetImportSession
        {
            FileName = Path.GetFileName(file.FileName),
            Headers = headers,
            Rows = csv.Rows.Skip(1).ToList(),
            Targets = AssetImportTargets.Suggest(headers, store.AssetAttributeDefinitions),
            Options = new AssetImportOptions("dmy", true, false, null, null)
        };
        var token = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(token), session, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(45) });
        return RedirectToPage(new { t = token });
    }

    public IActionResult OnPostPreview(string? t, string?[]? targets, string? dateFormat, bool createMissing, bool blankClears, string? defaultType, string? defaultStatus)
    {
        var session = Load(t);
        if (session is null) { Message = "That import has expired. Upload the file again."; return RedirectToPage(); }

        var chosen = new string?[session.Headers.Length];
        for (var i = 0; i < chosen.Length; i++)
        {
            var target = targets is not null && i < targets.Length ? targets[i] : null;
            chosen[i] = AssetImportTargets.IsKnown(target, store.AssetAttributeDefinitions) ? target : null;
        }
        session.Targets = chosen;
        session.Options = new AssetImportOptions(
            ImportParsing.DateFormats.Any(x => x.Key == dateFormat) ? dateFormat! : "dmy",
            createMissing, blankClears,
            string.IsNullOrWhiteSpace(defaultType) ? null : defaultType,
            string.IsNullOrWhiteSpace(defaultStatus) ? null : defaultStatus);

        var used = chosen.Where(x => x is not null).ToList();
        if (!used.Contains("tag")) return Invalid(t, "Choose which column holds the asset tag. Rows are matched to assets by it.");
        var repeated = used.GroupBy(x => x).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null) return Invalid(t, "Each field can be used by one column only. Two columns are mapped to the same field.");
        session.Mapped = true;
        return RedirectToPage(new { t });
    }

    public IActionResult OnPostApply(string? t)
    {
        var session = Load(t);
        if (session is null || !session.Mapped) { Message = "That import has expired. Upload the file again."; return RedirectToPage(); }
        var result = store.ApplyAssetImport(session.Rows, session.Targets, session.Options, session.FileName);
        cache.Remove(CacheKey(t!));
        TempData["Message"] = $"Import finished: {result.Created} added, {result.Updated} updated, {result.Unchanged} unchanged, {result.Errors} skipped"
            + (result.NewValues.Count > 0 ? $", {result.NewValues.Count} new list value{(result.NewValues.Count == 1 ? "" : "s")} created." : ".");
        return RedirectToPage("/Assets");
    }

    // The rows that would be skipped, with the reason, so they can be fixed and imported again.
    public IActionResult OnGetProblems(string? t)
    {
        var session = Load(t);
        if (session is null || !session.Mapped) return RedirectToPage();
        var plan = store.PlanAssetImport(session.Rows, session.Targets, session.Options);
        var csv = new StringBuilder();
        csv.Append(string.Join(",", session.Headers.Append("Problem").Select(AssetCsv.Escape))).Append("\r\n");
        foreach (var result in plan.Results.Where(x => x.Action == ImportAction.Error))
        {
            var cells = session.Rows[result.RowNumber - 2];
            var padded = Enumerable.Range(0, session.Headers.Length).Select(i => i < cells.Length ? cells[i] : "");
            csv.Append(string.Join(",", padded.Append(result.Error ?? "").Select(AssetCsv.Escape))).Append("\r\n");
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "import-problems.csv");
    }

    // A starting file: the same columns the asset export produces, so an export can be edited and imported straight back.
    public IActionResult OnGetTemplate()
    {
        string[] headers = ["Asset tag", "Make", "Model", "Type", "Serial number", "Status", "Location", "Assigned to", "Loan due back", "Supplier", "Purchase date", "Purchase price", "Purchase order", "Warranty end", "Replacement date"];
        string[] example = ["LT-2001", "Dell", "Latitude 5440", "Laptop", "SN-EXAMPLE-01", "In use", "Main Campus", "jordan.lee@school.example", "", "", "2025-09-01", "749.99", "PO-1042", "2028-09-01", ""];
        var csv = string.Join(",", headers.Select(AssetCsv.Escape)) + "\r\n" + string.Join(",", example.Select(AssetCsv.Escape)) + "\r\n";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", "assets-import-template.csv");
    }

    public string? SampleFor(int column) => string.Join(" · ", Session!.Rows.Select(r => column < r.Length ? r[column] : "").Where(x => x.Length > 0).Distinct().Take(3));

    public string? PageUrl(int page, string? show = null) => Url.Page("/Assets/Import", new { t = Token, show = (show ?? Show) is null or "all" ? null : (show ?? Show), p = page > 1 ? page : (int?)null });

    private IActionResult Invalid(string? token, string message)
    {
        Message = message;
        return RedirectToPage(new { t = token, step = "map" });
    }

    private AssetImportSession? Load(string? token) =>
        !string.IsNullOrWhiteSpace(token) && cache.TryGetValue(CacheKey(token), out AssetImportSession? session) ? session : null;

    private static string CacheKey(string token) => "asset-import:" + token;
}
