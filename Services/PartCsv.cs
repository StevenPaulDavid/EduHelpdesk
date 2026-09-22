using System.Globalization;
using System.Text;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

public static class PartCsv
{
    public static string FileName(DateTime now) => $"parts-{now:yyyyMMdd-HHmm}.csv";

    public static string Build(IEnumerable<PartRecord> parts, IReadOnlyList<SupplierRecord> suppliers)
    {
        var supplierNames = suppliers.ToDictionary(x => x.Id, x => x.Name);
        var headers = new[] { "Name", "SKU", "Category", "Location", "Quantity on hand", "Reorder threshold", "Suppliers" };

        var csv = new StringBuilder();
        AppendRow(csv, headers);
        foreach (var part in parts)
        {
            var supplierText = string.Join("; ", part.SupplierIds.Select(id => supplierNames.GetValueOrDefault(id, string.Empty)).Where(x => x.Length > 0));
            AppendRow(csv, new[]
            {
                part.Name, part.Sku ?? string.Empty, part.Category ?? string.Empty, part.Location,
                part.QuantityOnHand.ToString(CultureInfo.InvariantCulture),
                part.ReorderThreshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                supplierText
            });
        }
        return csv.ToString();
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string> cells) =>
        csv.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");

    // Quotes a cell when needed, and stops spreadsheets treating text that starts with = + - or @ as a formula.
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
