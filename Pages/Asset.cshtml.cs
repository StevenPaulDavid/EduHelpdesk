using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class AssetModel(HelpdeskStore store) : PageModel
{
    public AssetRecord? Asset { get; private set; }
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<string> AssetMakes => store.AssetMakes;
    public IReadOnlyList<string> AssetModels => store.AssetModels;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    public IReadOnlyList<AssetAttributeDefinition> CustomAttributes => Asset is null ? [] : store.AssetAttributeDefinitions.Where(x => x.AssetType.Equals(Asset.Type, StringComparison.OrdinalIgnoreCase)).ToList();
    public IReadOnlyDictionary<Guid, string> CustomAttributeValues => Asset is null ? new Dictionary<Guid, string>() : store.GetAssetAttributeValues(Asset.Id).ToDictionary(x => x.AttributeDefinitionId, x => x.Value);
    public static IReadOnlyList<string> Choices(AssetAttributeDefinition definition) => HelpdeskStore.GetChoices(definition);
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(Guid id)
    {
        Asset = store.Assets.FirstOrDefault(x => x.Id == id);
        return Asset is null ? NotFound() : Page();
    }

    public IActionResult OnPostSave(
        Guid id,
        string assetTag,
        string make,
        string type,
        string model,
        string? serialNumber,
        string? location,
        Guid? assignedUserId,
        Guid? supplierId,
        int[]? selectedNumbers,
        Dictionary<Guid, string>? customAttributes)
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

        if (supplierId.HasValue && !store.Suppliers.Any(x => x.Id == supplierId.Value))
        {
            Message = "Select a valid supplier.";
            return RedirectToPage(new { id });
        }
        var asset = new AssetRecord(id, assetTag.Trim(), (make ?? string.Empty).Trim(), model.Trim(), type.Trim(), (serialNumber ?? string.Empty).Trim(), (location ?? string.Empty).Trim(), assignedUserId, supplierId);
        if (!store.UpdateAssetAttributeValues(id, type.Trim(), customAttributes))
        {
            Message = "Asset was not found.";
            return RedirectToPage(new { id });
        }
        Message = store.UpdateAssetAndTickets(asset, selectedNumbers ?? []) ? "Asset and linked jobs updated." : "Asset was not found.";
        return RedirectToPage(new { id });
    }
}
