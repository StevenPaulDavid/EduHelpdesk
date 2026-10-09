using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages;
public class SupplierModel(HelpdeskStore store) : PageModel
{
    public SupplierRecord? Supplier { get; private set; }
    public IReadOnlyList<AssetRecord> Assets => store.Assets.Where(x => x.SupplierId == Supplier?.Id).ToList();
    // The supplier's contracts, for whoever can reach the contracts register; costs show with the contracts, so not otherwise.
    public bool CanSeeContracts => store.UserCan(User, Modules.Contracts, ModulePermission.Access);
    public bool CanOpenContracts => store.UserCan(User, Modules.Contracts, ModulePermission.View);
    public IReadOnlyList<ContractRecord> Contracts => store.Contracts.Where(x => x.SupplierId == Supplier?.Id).ToList();
    public IActionResult OnGet(Guid id) { Supplier = store.Suppliers.FirstOrDefault(x => x.Id == id); return Supplier is null ? NotFound() : Page(); }
}
