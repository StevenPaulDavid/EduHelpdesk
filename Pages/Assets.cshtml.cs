using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class AssetsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<string> AssetMakes => store.AssetMakes;
    public IReadOnlyList<string> AssetModels => store.AssetModels;
    public IReadOnlyList<string> Locations => store.Locations;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostAddAsset(string assetTag, string make, string type, string model, string? serialNumber, string? location, Guid? assignedUserId, Guid? supplierId)
    {
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
        {
            Message = "Asset tag, type, and model are required.";
            return RedirectToPage();
        }

        store.AddAsset(new AssetRecord(Guid.NewGuid(), assetTag.Trim(), (make ?? string.Empty).Trim(), model.Trim(), type.Trim(), (serialNumber ?? string.Empty).Trim(), (location ?? string.Empty).Trim(), assignedUserId, supplierId));
        Message = "Asset added.";
        return RedirectToPage();
    }

    public IActionResult OnPostSaveAsset(Guid id, string assetTag, string make, string type, string model, string? serialNumber, string? location, Guid? assignedUserId, Guid? supplierId)
    {
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
        {
            Message = "Asset tag, type, and model are required.";
            return RedirectToPage();
        }

        Message = store.UpdateAsset(new AssetRecord(id, assetTag.Trim(), (make ?? string.Empty).Trim(), model.Trim(), type.Trim(), (serialNumber ?? string.Empty).Trim(), (location ?? string.Empty).Trim(), assignedUserId, supplierId))
            ? "Asset updated."
            : "Asset was not found.";
        return RedirectToPage();
    }

    public IActionResult OnPostDeleteAsset(Guid id)
    {
        Message = store.DeleteAsset(id) ?? "Asset deleted.";
        return RedirectToPage();
    }
}
