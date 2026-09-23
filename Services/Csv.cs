using System.Text;

namespace EduHelpdesk.Services;

// Shared CSV writing for every export in the app. Escape used to be copied verbatim into AssetCsv and PartCsv; it
// carries the formula-injection guard, which is a security control and belongs in exactly one place.
public static class Csv
{
    // Quotes a cell when needed, and stops spreadsheets treating text that starts with = + - or @ as a formula.
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static void AppendRow(StringBuilder csv, IEnumerable<string?> cells) =>
        csv.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");

    // A whole table from a header row and a projection. The report pages build their own rows with this rather than
    // handing their view models to a service - the report types live in the page models, and Services should not
    // depend on Pages just to format a spreadsheet.
    public static string Table<T>(IEnumerable<string> headers, IEnumerable<T> rows, Func<T, IEnumerable<string?>> cells)
    {
        var csv = new StringBuilder();
        AppendRow(csv, headers);
        foreach (var row in rows) AppendRow(csv, cells(row));
        return csv.ToString();
    }

    // What every export handler hands to File(): the UTF-8 preamble matters, or Excel mangles accented names.
    public static byte[] ToBytes(string csv) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

    public const string ContentType = "text/csv; charset=utf-8";

    public static string FileName(string report, DateTime now) => $"{report}-{now:yyyyMMdd-HHmm}.csv";
}
