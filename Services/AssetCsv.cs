using System.Globalization;
using System.Text;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

public static class AssetCsv
{
    public static string FileName(DateTime now) => $"assets-{now:yyyyMMdd-HHmm}.csv";

    // One row per asset: its own fields first, then one column per custom attribute that applies to any of the exported assets.
    public static string Build(
        IEnumerable<AssetRecord> assets,
        IReadOnlyList<UserRecord> users,
        IReadOnlyList<SupplierRecord> suppliers,
        IReadOnlyList<AssetAttributeDefinition> attributeDefinitions,
        IReadOnlyList<AssetAttributeValue> attributeValues,
        IReadOnlyDictionary<string, int> lifespanYears)
    {
        var rows = assets.ToList();
        var userNames = users.ToDictionary(x => x.Id, x => x.Name);
        var supplierNames = suppliers.ToDictionary(x => x.Id, x => x.Name);
        var valuesByAsset = attributeValues.GroupBy(x => x.AssetId).ToDictionary(g => g.Key, g => g.ToDictionary(v => v.AttributeDefinitionId, v => v.Value));
        var attributes = attributeDefinitions.Where(d => rows.Any(a => d.AppliesTo(a.Type))).ToList();

        var headers = new List<string>
        {
            "Asset tag", "Make", "Model", "Type", "Serial number", "Status", "Location", "Assigned to", "Loan due back", "Supplier",
            "Purchase date", "Purchase price", "Purchase order", "Quote reference", "Warranty end", "Replacement date",
            "Disposal date", "Disposal method", "Disposal proceeds"
        };
        // Two attributes can share a name (they apply to different types); keep the columns distinct.
        var attributeHeaders = new List<string>();
        foreach (var attribute in attributes)
        {
            var name = attribute.Name;
            var header = name;
            for (var n = 2; headers.Contains(header, StringComparer.OrdinalIgnoreCase) || attributeHeaders.Contains(header, StringComparer.OrdinalIgnoreCase); n++) header = $"{name} ({n})";
            attributeHeaders.Add(header);
        }

        var csv = new StringBuilder();
        AppendRow(csv, headers.Concat(attributeHeaders));
        foreach (var asset in rows)
        {
            var values = valuesByAsset.GetValueOrDefault(asset.Id);
            var cells = new List<string>
            {
                asset.AssetTag, asset.Make, asset.Model, asset.Type, asset.SerialNumber, asset.Status, asset.Location,
                asset.AssignedUserId is { } holder ? userNames.GetValueOrDefault(holder, "") : "",
                Day(asset.LoanDueDate),
                asset.SupplierId is { } supplier ? supplierNames.GetValueOrDefault(supplier, "") : "",
                Day(asset.PurchaseDate),
                asset.PurchasePrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                asset.PurchaseOrder,
                asset.QuoteReference,
                Day(asset.WarrantyEnd),
                Day(AssetInsights.ReplacementDate(asset, lifespanYears)),
                Day(asset.DisposalDate),
                asset.DisposalMethod,
                asset.DisposalProceeds?.ToString("0.00", CultureInfo.InvariantCulture) ?? ""
            };
            foreach (var attribute in attributes)
                cells.Add(attribute.AppliesTo(asset.Type) && values is not null && values.TryGetValue(attribute.Id, out var value) ? value : "");
            AppendRow(csv, cells);
        }
        return csv.ToString();
    }

    private static string Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static void AppendRow(StringBuilder csv, IEnumerable<string> cells) => Csv.AppendRow(csv, cells);
}
