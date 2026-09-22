using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The search, filters and sort order of the parts list. Mirrors AssetListQuery so "the parts matching the current
// filters" behaves the same way as it does for assets.
public sealed class PartListQuery
{
    // Filter values that mean "blank".
    public const string None = "(none)";
    public static readonly string[] SortColumns = ["name", "sku", "category", "location", "quantity"];
    // low (at or below its reorder threshold)
    public static readonly string[] Flags = ["low"];

    public string? Search { get; set; }
    public string? Category { get; set; }
    public string? Location { get; set; }
    // "none" for parts with no supplier, or a supplier id.
    public string? Supplier { get; set; }
    public string? Flag { get; set; }
    public string Sort { get; set; } = "name";
    public bool Descending { get; set; }

    public List<PartRecord> Run(IEnumerable<PartRecord> parts, IReadOnlyList<SupplierRecord> suppliers, int defaultReorderThreshold)
    {
        var supplierNames = suppliers.ToDictionary(x => x.Id, x => x.Name);
        IEnumerable<PartRecord> query = parts as IReadOnlyCollection<PartRecord> ?? parts.ToList();

        if (!string.IsNullOrWhiteSpace(Category)) query = query.Where(x => Matches(x.Category, Category));
        if (!string.IsNullOrWhiteSpace(Location)) query = query.Where(x => Matches(x.Location, Location));
        if (!string.IsNullOrWhiteSpace(Supplier))
        {
            if (string.Equals(Supplier, "none", StringComparison.OrdinalIgnoreCase)) query = query.Where(x => x.SupplierIds.Count == 0);
            else if (Guid.TryParse(Supplier, out var supplierId)) query = query.Where(x => x.SupplierIds.Contains(supplierId));
        }
        if (string.Equals(Flag?.Trim(), "low", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => PartInsights.IsLow(x, defaultReorderThreshold));
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(x =>
            {
                var supplierText = string.Join(' ', x.SupplierIds.Select(id => supplierNames.GetValueOrDefault(id, string.Empty)));
                var text = string.Join('\n', x.Name, x.Sku ?? string.Empty, x.Category ?? string.Empty, x.Location, supplierText);
                return terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
        }
        return Order(query.ToList());
    }

    private static bool Matches(string? value, string filter) =>
        filter == None ? string.IsNullOrWhiteSpace(value) : string.Equals(value?.Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private List<PartRecord> Order(List<PartRecord> list)
    {
        var natural = NaturalComparer.Instance;
        IOrderedEnumerable<PartRecord> By(Func<PartRecord, string?> key) => Descending ? list.OrderByDescending(key, natural) : list.OrderBy(key, natural);
        IOrderedEnumerable<PartRecord> ByQuantity() => Descending ? list.OrderByDescending(x => x.QuantityOnHand) : list.OrderBy(x => x.QuantityOnHand);

        var ordered = Sort switch
        {
            "sku" => By(x => x.Sku),
            "category" => By(x => x.Category),
            "location" => By(x => x.Location),
            "quantity" => ByQuantity(),
            _ => By(x => x.Name)
        };
        return ordered.ThenBy(x => x.Name, natural).ToList();
    }
}
