using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages;
public class SupplierModel(HelpdeskStore store) : PageModel
{
    public EduHelpdesk.Models.SupplierRecord? Supplier { get; private set; }
    public IReadOnlyList<EduHelpdesk.Models.AssetRecord> Assets => store.Assets.Where(x => x.SupplierId == Supplier?.Id).ToList();
    public IActionResult OnGet(Guid id) { Supplier = store.Suppliers.FirstOrDefault(x => x.Id == id); return Supplier is null ? NotFound() : Page(); }
}
