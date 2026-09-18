using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class AssetModel(HelpdeskStore store) : PageModel
{
    public AssetRecord? Asset { get; private set; }
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(Guid id)
    {
        Asset = store.Assets.FirstOrDefault(x => x.Id == id);
        return Asset is null ? NotFound() : Page();
    }

    public IActionResult OnPostSave(
        Guid id,
        string assetTag,
        string type,
        string model,
        string serialNumber,
        string location,
        Guid? assignedUserId,
        int[]? selectedNumbers)
    {
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
        {
            Message = "Asset tag, type, and model are required.";
            return RedirectToPage(new { id });
        }

        if (assignedUserId.HasValue && !store.Users.Any(x => x.Id == assignedUserId.Value))
        {
            Message = "Select a valid assigned user.";
            return RedirectToPage(new { id });
        }

        var asset = new AssetRecord(id, assetTag.Trim(), type.Trim(), model.Trim(), serialNumber.Trim(), location.Trim(), assignedUserId);
        Message = store.UpdateAssetAndTickets(asset, selectedNumbers ?? []) ? "Asset and linked jobs updated." : "Asset was not found.";
        return RedirectToPage(new { id });
    }
}
