using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The one rule for "this part is low on stock", shared by the Parts list badge/flag, the Overview review list and
// the low-stock report, so they can never disagree with each other.
public static class PartInsights
{
    public static bool IsLow(PartRecord part, int defaultReorderThreshold) => part.QuantityOnHand <= (part.ReorderThreshold ?? defaultReorderThreshold);

    public static List<PartRecord> LowStock(IEnumerable<PartRecord> parts, int defaultReorderThreshold) =>
        parts.Where(x => IsLow(x, defaultReorderThreshold))
            .OrderBy(x => x.QuantityOnHand).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
