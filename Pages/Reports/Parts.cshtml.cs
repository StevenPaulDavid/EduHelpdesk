using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

public class PartReportsModel(HelpdeskStore store) : PageModel
{
    // The Finance tab is only shown to people who can actually open it - see the AuthorizePage entries in Program.cs.
    public bool CanSeeFinance => store.UserHasPermission(User, EduHelpdesk.Models.Permissions.Settings);
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

    public Microsoft.AspNetCore.Mvc.IActionResult OnGetExport()
    {
        OnGet();
        var csv = Csv.Table(
            ["Part", "SKU", "Category", "Location", "Suppliers", "Quantity on hand", "Reorder threshold", "Out of stock"],
            LowStock,
            x => [x.Name, x.Sku, x.Category, x.Location, SupplierNames(x), x.QuantityOnHand.ToString(), Threshold(x).ToString(), x.QuantityOnHand == 0 ? "Yes" : "No"]);
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName("parts-low-stock", DateTime.Now));
    }

    public int Threshold(PartRecord part) => part.ReorderThreshold ?? DefaultReorderThreshold;

    public string SupplierNames(PartRecord part) =>
        string.Join(", ", part.SupplierIds.Select(id => Suppliers.FirstOrDefault(x => x.Id == id)?.Name).Where(x => x is not null));
}
