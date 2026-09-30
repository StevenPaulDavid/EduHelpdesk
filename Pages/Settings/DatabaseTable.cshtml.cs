using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// One table, exactly as stored: searchable, filterable by a column, a page at a time, and downloadable as CSV.
public class DatabaseTableModel(RawDatabase raw) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Name { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "col")] public string? Column { get; set; }
    [BindProperty(SupportsGet = true, Name = "op")] public string? Operator { get; set; }
    [BindProperty(SupportsGet = true, Name = "val")] public string? Value { get; set; }
    [BindProperty(SupportsGet = true)] public bool Newest { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public RawDatabase.Page Result { get; private set; } = null!;
    public bool SeesSecrets => RawDatabase.IsAdministrator(User);
    public string Description => RawDatabase.Descriptions.GetValueOrDefault(Result.Table, "");
    public int TotalPages => (int)Math.Max(1, (Result.Total + RawDatabase.PageSize - 1) / RawDatabase.PageSize);
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Column);
    private string Op => RawDatabase.Operators.Contains(Operator) ? Operator! : RawDatabase.Operators[0];

    private RawDatabase.Query Query => new(Search, Column, Op, Value, Newest);

    // The filters in words, for the audit log: "search 'smith', Email contains 'x'".
    private string FilterText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Search)) parts.Add($"search '{Search.Trim()}'");
        if (!string.IsNullOrWhiteSpace(Column)) parts.Add(Op == "is empty" ? $"{Column} is empty" : $"{Column} {Op} '{Value}'");
        return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
    }

    public IActionResult OnGet()
    {
        if (PageNumber < 1) PageNumber = 1;
        if (raw.Read(Name, Query, SeesSecrets, PageNumber) is not { } page) return NotFound();
        Result = page;
        if (PageNumber > TotalPages) { PageNumber = TotalPages; Result = raw.Read(Name, Query, SeesSecrets, PageNumber)!; }
        raw.RecordLook("Viewed raw data", $"{Result.Table} table", $"{Result.Total:N0} rows{FilterText()}");
        return Page();
    }

    public IActionResult OnGetExport()
    {
        if (raw.Read(Name, Query, SeesSecrets, page: 0) is not { } page) return NotFound();
        raw.RecordLook("Exported raw data", $"{page.Table} table", $"{page.Rows.Count:N0} rows to CSV{FilterText()}", always: true);
        var csv = Csv.Table(page.Columns.Select(x => x.Name), page.Rows, row => row.Select(RawDatabase.Text));
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName($"raw-{page.Table}", DateTime.Now));
    }

    // This page again with some filters changed.
    public Dictionary<string, string?> Route(int? page = null, bool? newest = null) => new()
    {
        ["name"] = Result.Table, ["q"] = Search, ["col"] = Column, ["op"] = Column is null ? null : Op, ["val"] = Value,
        ["newest"] = (newest ?? Newest) ? "true" : null, ["p"] = page is > 1 ? page.ToString() : null
    };
}
