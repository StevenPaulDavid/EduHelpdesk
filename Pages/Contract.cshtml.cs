using EduHelpdesk.Models;
using EduHelpdesk.Pages.Contracts;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// One contract: a read view of everything the register holds, what needs acting on, the leased assets that belong to
// it and its history, with editing in a drawer. Opens at Contracts: View; changing it needs Edit, removing it Delete.
public class ContractModel(HelpdeskStore store) : PageModel
{
    public ContractRecord? Contract { get; private set; }
    [BindProperty] public ContractInput Input { get; set; } = new();
    public ContractForm Form => ContractForm.For(store, Input);
    [TempData] public string? Message { get; set; }
    // Set when a save was refused, so the drawer opens again with what was typed and says why.
    public bool ReopenEdit { get; private set; }
    public string? Error { get; private set; }

    public bool CanEdit => store.UserCan(User, Modules.Contracts, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Contracts, ModulePermission.Delete);
    public bool CanOpenAssets => store.UserCan(User, Modules.Assets, ModulePermission.View);
    public bool CanOpenSuppliers => store.UserCan(User, Modules.Suppliers, ModulePermission.View);
    public bool CanOpenProjects => store.UserCan(User, Modules.Projects, ModulePermission.View);

    public DateOnly Today { get; } = AssetInsights.Today;
    public SupplierRecord? Supplier { get; private set; }
    public ProjectRecord? Project { get; private set; }
    public IReadOnlyList<AssetRecord> LeasedAssets { get; private set; } = [];
    public IReadOnlyList<AuditEntry> History { get; private set; } = [];

    public IActionResult OnGet(Guid id)
    {
        if (!Load(id)) return NotFound();
        Input = ContractInput.From(Contract!);
        return Page();
    }

    public IActionResult OnPostSave(Guid id)
    {
        if (!CanEdit) return Forbid();
        if (!Load(id)) return NotFound();
        var record = Input.ToRecord(id, out var error);
        var (ok, message) = record is null ? (false, error!) : store.UpdateContract(record);
        if (!ok)
        {
            // Shown again with what was typed rather than redirecting it away.
            Error = message;
            ReopenEdit = true;
            return Page();
        }
        Message = message;
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostDelete(Guid id)
    {
        if (!CanDelete) return Forbid();
        var name = store.FindContract(id)?.Name;
        var error = store.DeleteContract(id);
        if (error is not null) { Message = error; return RedirectToPage(new { id }); }
        TempData["Message"] = $"{name} was removed from the contracts register.";
        return RedirectToPage("/Contracts");
    }

    private bool Load(Guid id)
    {
        Contract = store.FindContract(id);
        if (Contract is null) return false;
        Supplier = Contract.SupplierId is { } supplierId ? store.Suppliers.FirstOrDefault(x => x.Id == supplierId) : null;
        Project = Contract.ProjectNumber is { } number ? store.FindProject(number) : null;
        LeasedAssets = store.Assets.Where(x => x.ContractId == id).OrderBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
        var key = id.ToString();
        History = store.GetAuditEntries().Where(x => x.EntityType == "Contract" && x.EntityKey == key).OrderByDescending(x => x.At).ToList();
        return true;
    }
}
