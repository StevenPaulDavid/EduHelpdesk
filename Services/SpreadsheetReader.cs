using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EduHelpdesk.Services;

// A sheet read into plain text cells: the header row, and each row after it with its spreadsheet row number and
// whether it was hidden. Hidden rows matter because the DfE templates say to hide their example rows rather than delete
// them - an import that ignored that would load six made-up assets. (Merged from EduInventory.)
public sealed class SheetTable
{
    public List<string> Headers { get; set; } = [];
    public List<SheetRow> Rows { get; set; } = [];
}

public sealed class SheetRow
{
    public int Number { get; set; }
    public bool Hidden { get; set; }
    public List<string> Cells { get; set; } = [];
    public string Cell(int index) => index >= 0 && index < Cells.Count ? Cells[index].Trim() : "";
}

public static partial class SpreadsheetReader
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    // Reads a CSV, or an .xlsx's first sheet with recognisable headings (a sheet named in `preferredSheets` first), and
    // finds the header row: the first of the top 15 rows with at least two cells matching `knownHeaders`. Everything
    // above it is ignored, as are the DfE templates' "How to fill in this section" rows.
    public static SheetTable Read(byte[] bytes, string fileName, IEnumerable<string> knownHeaders, params string[] preferredSheets)
    {
        var known = knownHeaders.Select(NormaliseHeader).ToHashSet();
        var rows = IsExcel(fileName) ? ReadXlsx(bytes, known, preferredSheets) : ReadCsv(bytes);
        var headerIndex = rows.Take(15).ToList().FindIndex(r => r.Cells.Count(c => known.Contains(NormaliseHeader(c))) >= 2);
        if (headerIndex < 0) return new SheetTable();
        var table = new SheetTable { Headers = rows[headerIndex].Cells.Select(x => x.Trim()).ToList() };
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Cells.All(string.IsNullOrWhiteSpace)) continue;
            var first = row.Cells.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? "";
            if (first.StartsWith("How to fill in", StringComparison.OrdinalIgnoreCase)
                || first.StartsWith("This section is completed automatically", StringComparison.OrdinalIgnoreCase))
                continue;
            table.Rows.Add(row);
        }
        return table;
    }

    public static bool IsExcel(string fileName) => fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    // "Condition of asset\n\n" and "condition of asset" are the same heading.
    public static string NormaliseHeader(string? header) =>
        WhitespaceOrPunctuation().Replace((header ?? "").ToLowerInvariant().Replace("£", "").Replace("(", " ").Replace(")", " "), " ").Trim();

    [GeneratedRegex(@"[\s\?\.:/,'’]+")]
    private static partial Regex WhitespaceOrPunctuation();

    private static List<SheetRow> ReadCsv(byte[] bytes)
    {
        var parsed = CsvReader.Parse(CsvReader.Decode(bytes));
        return parsed.Rows.Select((cells, i) => new SheetRow { Number = i + 1, Cells = cells.ToList() }).ToList();
    }

    private static List<SheetRow> ReadXlsx(byte[] bytes, HashSet<string> known, string[] preferredSheets)
    {
        using var buffer = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(buffer, false);
        var workbook = document.WorkbookPart ?? throw new InvalidDataException("That workbook has no sheets.");
        var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToList() ?? [];
        var sheets = workbook.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];
        // The DfE templates put instructions on the first tab, so the named data tab is tried first, then any tab with
        // recognisable headings near the top.
        var ordered = sheets.OrderBy(s => preferredSheets.Any(p => string.Equals(p, s.Name?.Value?.Trim(), StringComparison.OrdinalIgnoreCase)) ? 0 : 1).ToList();
        foreach (var sheet in ordered)
        {
            if (sheet.Id?.Value is not { } relId || workbook.GetPartById(relId) is not WorksheetPart part) continue;
            var rows = ReadSheet(part, shared);
            if (rows.Take(15).Any(r => r.Cells.Count(c => known.Contains(NormaliseHeader(c))) >= 2)) return rows;
        }
        return [];
    }

    private static List<SheetRow> ReadSheet(WorksheetPart part, List<string> shared)
    {
        var rows = new List<SheetRow>();
        if (part.Worksheet is null) return rows;
        foreach (var row in part.Worksheet.Descendants<Row>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<Cell>())
            {
                var column = ColumnIndex(cell.CellReference?.Value);
                while (cells.Count < column) cells.Add("");
                cells.Add(CellText(cell, shared));
            }
            rows.Add(new SheetRow { Number = (int)(row.RowIndex?.Value ?? (uint)(rows.Count + 1)), Hidden = row.Hidden?.Value == true, Cells = cells });
        }
        return rows;
    }

    private static string CellText(Cell cell, List<string> shared)
    {
        var value = cell.CellValue?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(value, out var index) && index < shared.Count) return shared[index];
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.Boolean) return value == "1" ? "Yes" : "No";
        return value;
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return 0;
        var index = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return index - 1;
    }

    // ---- Values ----

    private static readonly string[] DateFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "d MMM yyyy", "d MMMM yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "MMM yyyy", "MMMM yyyy"];

    // Dates arrive as text in a CSV and as Excel serial numbers in an .xlsx (the DfE examples are 45525 and so on).
    // Blank is a success with no date.
    public static bool TryParseDate(string? text, out DateOnly? date)
    {
        date = null;
        var value = text?.Trim() ?? "";
        if (value.Length == 0) return true;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        if (DateTime.TryParseExact(value, DateFormats, Uk, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            || DateTime.TryParse(value, Uk, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }
        return false;
    }

    public static bool TryParseMoney(string? text, out decimal? amount)
    {
        amount = null;
        var value = (text ?? "").Replace("£", "").Replace(",", "").Trim();
        if (value.Length == 0) return true;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0) { amount = parsed; return true; }
        return false;
    }

    public static bool? ParseYesNo(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "yes" or "y" or "true" or "1" => true,
        "no" or "n" or "false" or "0" => false,
        _ => null
    };

    // "£10,500 pa", "£135 per quarter", "£200 pm", "£875 (year 1 only), £175 pa year 2 onwards": the first amount and
    // whatever period the text names. The caller keeps the wording as a note when there is more to it.
    public static (decimal? Amount, string? Period) ParseCost(string? text)
    {
        var value = (text ?? "").Trim();
        var match = MoneyPattern().Match(value);
        if (!match.Success) return (null, null);
        var amount = decimal.Parse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture);
        var rest = " " + value.ToLowerInvariant();
        string? period =
            rest.Contains(" pm") || rest.Contains("per month") || rest.Contains("monthly") || rest.Contains("/month") ? Models.ContractCostPeriods.PerMonth
            : rest.Contains("quarter") ? Models.ContractCostPeriods.PerQuarter
            : rest.Contains(" pa") || rest.Contains("per year") || rest.Contains("per annum") || rest.Contains("annual") || rest.Contains("/year") ? Models.ContractCostPeriods.PerYear
            : rest.Contains("one-off") || rest.Contains("one off") ? Models.ContractCostPeriods.OneOff
            : rest.Contains("total") ? Models.ContractCostPeriods.Total
            : null;
        return (amount, period);
    }

    [GeneratedRegex(@"£?\s*([0-9][0-9,]*(?:\.[0-9]+)?)")]
    private static partial Regex MoneyPattern();
}
