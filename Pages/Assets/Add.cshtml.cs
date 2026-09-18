using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Assets;

public class AddModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<EduHelpdesk.Models.UserRecord> Users => store.Users;
    public IReadOnlyList<EduHelpdesk.Models.SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<string> AssetMakes => store.AssetMakes;
    public IReadOnlyList<string> AssetModels => store.AssetModels;
    public IReadOnlyList<string> Locations => store.Locations;
    [TempData] public string? Message { get; set; }
    public IActionResult OnPost(string assetTag, string make, string type, string model, string? serialNumber, string? location, Guid? assignedUserId, Guid? supplierId)
    {
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
            return Invalid("Asset tag, type, and model are required.");
        store.AddAsset(new(Guid.NewGuid(), assetTag.Trim(), (make ?? "").Trim(), model.Trim(), type.Trim(), (serialNumber ?? "").Trim(), (location ?? "").Trim(), assignedUserId, supplierId));
        Message = "Asset added.";
        return RedirectToPage("/Assets");
    }
    private IActionResult Invalid(string message) { Message = message; return Page(); }
}
