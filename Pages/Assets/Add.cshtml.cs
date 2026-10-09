using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Assets;

public class AddModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<string> AssetMakes => store.AssetMakes;
    public IReadOnlyList<string> AssetModels => store.AssetModels;
    public IReadOnlyDictionary<string, string> AssetModelMakes => store.AssetModelMakes;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<string> Statuses => store.AssetStatuses;
    public IReadOnlyList<string> Buildings => store.Buildings;
    public IReadOnlyList<string> Conditions => store.AssetConditions;
    [TempData] public string? Message { get; set; }

    // The value posted for a field, so the form keeps what was typed when it is shown again after an error.
    public string Posted(string name) => Request.HasFormContentType ? Request.Form[name].ToString() : string.Empty;

    public IActionResult OnPost(string assetTag, string make, string type, string model, string? serialNumber, string? location, Guid? assignedUserId, Guid? supplierId,
        string? status, DateOnly? purchaseDate, string? purchasePrice, string? purchaseOrder, string? quoteReference, DateOnly? warrantyEnd, DateOnly? replacementDate,
        string? building, string? operatingSystem, string? condition, string? ownership, DateOnly? nextCheckDate, DateOnly? endOfSupport)
    {
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
            return Invalid("Asset tag, type, and model are required.");
        if (AssetForm.DescribeInvalid(ModelState) is { } formError) return Invalid(formError);
        if (store.CheckAssetTag(assetTag, null) is { } tagError) return Invalid(tagError);
        if (!store.AssetModelMatchesMake(model, make))
            return Invalid($"{model.Trim()} does not belong to {make.Trim()}. Select a model for that make.");
        if (!AssetForm.TryPrice(purchasePrice, out var price)) return Invalid("Enter the purchase price as a positive amount, such as 349.99.");
        var chosenStatus = string.IsNullOrWhiteSpace(status) ? store.AssetStatuses.FirstOrDefault() ?? "In use" : store.AssetStatuses.FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
        if (chosenStatus is null) return Invalid("Select a valid status.");
        if (!AssetForm.TryListValue(building, null, store.Buildings, out var chosenBuilding)) return Invalid("Select a building from the list.");
        if (!AssetForm.TryListValue(condition, null, store.AssetConditions, out var chosenCondition)) return Invalid("Select a condition from the list.");

        var duplicate = store.FindDuplicateSerial(serialNumber, null);
        store.AddAsset(new AssetRecord(Guid.NewGuid(), assetTag.Trim(), (make ?? "").Trim(), model.Trim(), type.Trim(), (serialNumber ?? "").Trim(), (location ?? "").Trim(), assignedUserId, supplierId)
        {
            Status = chosenStatus,
            PurchaseDate = purchaseDate,
            PurchasePrice = price,
            PurchaseOrder = (purchaseOrder ?? "").Trim(),
            QuoteReference = (quoteReference ?? "").Trim(),
            WarrantyEnd = warrantyEnd,
            ReplacementDate = replacementDate,
            Building = chosenBuilding,
            OperatingSystem = (operatingSystem ?? "").Trim(),
            Condition = chosenCondition,
            Ownership = AssetOwnership.Normalize(ownership),
            NextCheckDate = nextCheckDate,
            EndOfSupport = endOfSupport
        });
        Message = duplicate is null ? "Asset added." : $"Asset added. Warning: serial number {(serialNumber ?? "").Trim()} is also recorded on asset {duplicate.AssetTag}.";
        return RedirectToPage("/Assets");
    }

    private IActionResult Invalid(string message) { Message = message; return Page(); }
}
