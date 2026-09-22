using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

public class PartReportsModel(HelpdeskStore store) : PageModel
{
    public int TotalParts { get; private set; }
    public int DefaultReorderThreshold { get; private set; }
    public int OutOfStockCount { get; private set; }
    public IReadOnlyList<PartRecord> LowStock { get; private set; } = [];
    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;

    public void OnGet()
    {
        var parts = store.Parts;
        TotalParts = parts.Count;
        DefaultReorderThreshold = store.PartsDefaultReorderThreshold;
        OutOfStockCount = parts.Count(x => x.QuantityOnHand == 0);
        LowStock = PartInsights.LowStock(parts, DefaultReorderThreshold);
    }

    public int Threshold(PartRecord part) => part.ReorderThreshold ?? DefaultReorderThreshold;

    public string SupplierNames(PartRecord part) =>
        string.Join(", ", part.SupplierIds.Select(id => Suppliers.FirstOrDefault(x => x.Id == id)?.Name).Where(x => x is not null));
}
