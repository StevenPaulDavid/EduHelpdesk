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
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostAddAsset(string assetTag, string type, string model, string serialNumber, string location, Guid? assignedUserId)
    {
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
        {
            Message = "Asset tag, type, and model are required.";
            return RedirectToPage();
        }

        store.AddAsset(new AssetRecord(Guid.NewGuid(), assetTag.Trim(), type.Trim(), model.Trim(), serialNumber.Trim(), location.Trim(), assignedUserId));
        Message = "Asset added.";
        return RedirectToPage();
    }

    public IActionResult OnPostSaveAsset(Guid id, string assetTag, string type, string model, string serialNumber, string location, Guid? assignedUserId)
    {
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
        {
            Message = "Asset tag, type, and model are required.";
            return RedirectToPage();
        }

        Message = store.UpdateAsset(new AssetRecord(id, assetTag.Trim(), type.Trim(), model.Trim(), serialNumber.Trim(), location.Trim(), assignedUserId))
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
