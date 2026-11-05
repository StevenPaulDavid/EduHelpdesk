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
    public string NextAssetTag => store.NextAssetTag;

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
        if (file is null || file.Length == 0) { Message = "Choose a CSV or Excel file first."; return RedirectToPage(); }
        if (file.Length > MaxBytes) { Message = "That file is larger than 10 MB. Split it into smaller files."; return RedirectToPage(); }
        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream);
            bytes = stream.ToArray();
        }
        string[] headers;
        List<string[]> rows;
        if (SpreadsheetReader.IsExcel(file.FileName))
        {
            // An Excel workbook - usually the DfE asset register template. Its header row is found wherever it sits, and
            // the rows the template asks you to hide, and its made-up examples, are left out.
            SheetTable table;
            try { table = SpreadsheetReader.Read(bytes, file.FileName, RegisterExports.AssetHeaders, "Digital technology assets", "Register of current assets"); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or FormatException)
            {
                Message = "That workbook couldn't be read. Save it again as .xlsx, or as .csv, and try again.";
                return RedirectToPage();
            }
            if (table.Headers.Count == 0) { Message = "No asset column headings were found near the top of any sheet in that workbook."; return RedirectToPage(); }
            var serial = table.Headers.FindIndex(x => SpreadsheetReader.NormaliseHeader(x) == "serial number");
            var kept = table.Rows.Where(r => !r.Hidden && !RegisterExports.ExampleAssetSerials.Contains(r.Cell(serial), StringComparer.OrdinalIgnoreCase)).ToList();
            var left = table.Rows.Count - kept.Count;
            if (left > 0) TempData["ImportNote"] = $"{left} row{(left == 1 ? " was" : "s were")} left out: hidden in the workbook, or the DfE template's own examples.";
            headers = [.. table.Headers];
            rows = kept.Select(r => Enumerable.Range(0, headers.Length).Select(i => r.Cell(i)).ToArray()).ToList();
            if (rows.Count == 0) { Message = "There are no asset rows to import in that workbook" + (left > 0 ? " once its hidden and example rows are left out." : "."); return RedirectToPage(); }
        }
        else
        {
            var separator = delimiter switch { "comma" => ',', "semicolon" => ';', "tab" => '\t', _ => (char?)null };
            var csv = CsvReader.Parse(CsvReader.Decode(bytes), separator);
            if (csv.Rows.Count < 2) { Message = "The file needs a header row and at least one row of assets."; return RedirectToPage(); }
            headers = csv.Rows[0];
            rows = csv.Rows.Skip(1).ToList();
        }
        if (rows.Count > MaxRows) { Message = $"That file has more than {MaxRows:N0} rows. Split it into smaller files."; return RedirectToPage(); }

        var targets = AssetImportTargets.Suggest(headers, store.AssetAttributeDefinitions);
        var session = new AssetImportSession
        {
            FileName = Path.GetFileName(file.FileName),
            Headers = headers,
            Rows = rows,
            Targets = targets,
            // Offered ticked when no column looks like an asset tag: the file has none to match on.
            Options = new AssetImportOptions("dmy", true, false, null, null, NumberBlankTags: !targets.Contains("tag"))
        };
        // A column that matches no field or attribute, and has something in it, is offered as a new custom attribute:
        // named from its heading, its kind guessed from its values, for the asset types that have a value in it.
        var offers = new List<NewAttributeSpec>();
        for (var i = 0; i < headers.Length; i++)
        {
            if (targets[i] is not null || string.IsNullOrWhiteSpace(headers[i]) || !rows.Any(r => i < r.Length && r[i].Trim().Length > 0)) continue;
            targets[i] = AssetImportTargets.NewAttributeTarget;
            offers.Add(SuggestNewAttribute(session, i));
        }
        session.Options = session.Options with { NewAttributes = offers };
        var token = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(token), session, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(45) });
        return RedirectToPage(new { t = token });
    }

    public IActionResult OnPostPreview(string? t, string?[]? targets, string? dateFormat, bool createMissing, bool blankClears, string? defaultType, string? defaultStatus, bool numberBlankTags)
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
            string.IsNullOrWhiteSpace(defaultStatus) ? null : defaultStatus,
            numberBlankTags);

        // Each column made into a new attribute, with the name, kind, choices and asset types set for it on the page.
        var specs = new List<NewAttributeSpec>();
        for (var i = 0; i < chosen.Length; i++)
        {
            if (chosen[i] != AssetImportTargets.NewAttributeTarget) continue;
            var form = Request.Form;
            specs.Add(new NewAttributeSpec(i, form[$"newName_{i}"].ToString().Trim(), form[$"newKind_{i}"].ToString().Trim(),
                form[$"newTypes_{i}"].Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList(), form[$"newChoices_{i}"].ToString()));
        }
        session.Options = session.Options with { NewAttributes = specs };
        if (store.CheckNewAttributes(specs) is { } attributeProblem) return Invalid(t, attributeProblem);

        var used = chosen.Where(x => x is not null && x != AssetImportTargets.NewAttributeTarget).ToList();
        if (!used.Contains("tag") && !numberBlankTags)
            return Invalid(t, "Choose which column holds the asset tag, or tick \"Give assets with no asset tag a built-in number\". Rows are matched to assets by their tag.");
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
            + ListAdditions(result.NewValues);
        return RedirectToPage("/Assets");
    }

    // The rows that would be skipped, with the reason, so they can be fixed and imported again.
    public IActionResult OnGetProblems(string? t)
    {
        var session = Load(t);
        if (session is null || !session.Mapped) return RedirectToPage();
        var plan = store.PlanAssetImport(session.Rows, session.Targets, session.Options);
        var csv = new StringBuilder();
        csv.Append(string.Join(",", session.Headers.Append("Problem").Select(Csv.Escape))).Append("\r\n");
        foreach (var result in plan.Results.Where(x => x.Action == ImportAction.Error))
        {
            var cells = session.Rows[result.RowNumber - 2];
            var padded = Enumerable.Range(0, session.Headers.Length).Select(i => i < cells.Length ? cells[i] : "");
            csv.Append(string.Join(",", padded.Append(result.Error ?? "").Select(Csv.Escape))).Append("\r\n");
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "import-problems.csv");
    }

    // A starting file: the same columns the asset export produces, so an export can be edited and imported straight back.
    public IActionResult OnGetTemplate()
    {
        string[] headers = ["Asset tag", "Make", "Model", "Type", "Serial number", "Status", "Location", "Assigned to", "Loan due back", "Supplier", "Purchase date", "Purchase price", "Purchase order", "Quote reference", "Warranty end", "Replacement date"];
        string[] example = ["LT-2001", "Dell", "Latitude 5440", "Laptop", "SN-EXAMPLE-01", "In use", "Main Campus", "jordan.lee@school.example", "", "", "2025-09-01", "749.99", "PO-1042", "Q-88231", "2028-09-01", ""];
        var csv = string.Join(",", headers.Select(Csv.Escape)) + "\r\n" + string.Join(",", example.Select(Csv.Escape)) + "\r\n";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", "assets-import-template.csv");
    }

    // What the page offers for a column made into a new attribute: what was set for it last time, or a fresh guess.
    public NewAttributeSpec NewAttributeFor(int column) =>
        Session!.Options.NewAttributes.FirstOrDefault(x => x.Column == column) ?? SuggestNewAttribute(Session, column);

    // Every asset type a new attribute could be for: the register's, and any new ones the file's type column brings.
    public IReadOnlyList<string> TypeChoices()
    {
        var types = store.AssetTypes.ToList();
        var typeColumn = Array.IndexOf(Session!.Targets, "type");
        if (typeColumn >= 0)
            foreach (var cell in Session.Rows.Select(r => typeColumn < r.Length ? r[typeColumn].Trim() : ""))
                if (cell.Length > 0 && !types.Contains(cell, StringComparer.OrdinalIgnoreCase)) types.Add(cell);
        return types;
    }

    private NewAttributeSpec SuggestNewAttribute(AssetImportSession session, int column)
    {
        var cells = session.Rows.Select(r => column < r.Length ? r[column] : "").ToList();
        var (kind, choices) = AssetImportTargets.SuggestKind(cells, session.Options.DateFormat);
        return new NewAttributeSpec(column, session.Headers[column].Trim(), kind, SuggestTypes(session, column), choices);
    }

    // The asset types of the rows with something in this column: the row's Type cell, else the existing asset's type
    // (matched by tag), else the default type for new assets.
    private List<string> SuggestTypes(AssetImportSession session, int column)
    {
        var typeColumn = Array.IndexOf(session.Targets, "type");
        var tagColumn = Array.IndexOf(session.Targets, "tag");
        var known = store.AssetTypes;
        var byTag = store.Assets.GroupBy(x => x.AssetTag.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Type, StringComparer.OrdinalIgnoreCase);
        var types = new List<string>();
        foreach (var row in session.Rows)
        {
            if (column >= row.Length || row[column].Trim().Length == 0) continue;
            var type = typeColumn >= 0 && typeColumn < row.Length && row[typeColumn].Trim().Length > 0 ? row[typeColumn].Trim()
                : tagColumn >= 0 && tagColumn < row.Length && byTag.TryGetValue(row[tagColumn].Trim(), out var existing) ? existing
                : session.Options.DefaultType;
            if (string.IsNullOrWhiteSpace(type)) continue;
            type = known.FirstOrDefault(x => string.Equals(x, type, StringComparison.OrdinalIgnoreCase)) ?? type;
            if (!types.Contains(type, StringComparer.OrdinalIgnoreCase)) types.Add(type);
        }
        return types;
    }

    public string? SampleFor(int column) => string.Join(" · ", Session!.Rows.Select(r => column < r.Length ? r[column] : "").Where(x => x.Length > 0).Distinct().Take(3));

    public string? PageUrl(int page, string? show = null) => Url.Page("/Assets/Import", new { t = Token, show = (show ?? Show) is null or "all" ? null : (show ?? Show), p = page > 1 ? page : (int?)null });

    // ", 2 new list values and 1 new contract (it needs its details) created." - contracts said apart, since they go on
    // the register rather than a list, and want filling in.
    private static string ListAdditions(IReadOnlyList<(string List, string Value)> added)
    {
        var contracts = added.Count(x => x.List == "Contract");
        var attributes = added.Count(x => x.List == "Custom attribute");
        var values = added.Count - contracts - attributes;
        var parts = new List<string>();
        if (values > 0) parts.Add($"{values} new list value{(values == 1 ? "" : "s")}");
        if (attributes > 0) parts.Add($"{attributes} new custom attribute{(attributes == 1 ? "" : "s")}");
        if (contracts > 0) parts.Add($"{contracts} new contract{(contracts == 1 ? "" : "s")} ({(contracts == 1 ? "it needs its" : "they need their")} details)");
        return parts.Count == 0 ? "." : $", {string.Join(" and ", parts)} created.";
    }

    private IActionResult Invalid(string? token, string message)
    {
        Message = message;
        return RedirectToPage(new { t = token, step = "map" });
    }

    private AssetImportSession? Load(string? token) =>
        !string.IsNullOrWhiteSpace(token) && cache.TryGetValue(CacheKey(token), out AssetImportSession? session) ? session : null;

    private static string CacheKey(string token) => "asset-import:" + token;
}
