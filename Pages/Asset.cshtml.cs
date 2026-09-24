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
    public IReadOnlyDictionary<string, string> AssetModelMakes => store.AssetModelMakes;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    public IReadOnlyList<string> Statuses => store.AssetStatuses;
    public IReadOnlyList<string> LoanReasons => store.LoanReasons;
    // The date the asset is due for replacement, from the typed date or the asset type's lifespan.
    public DateOnly? ReplacementDue => Asset is null ? null : AssetInsights.ReplacementDate(Asset, store.AssetTypeLifespans);
    public IReadOnlyList<AssetAssignment> Ownership => Asset is null ? [] : Asset.Assignments.OrderByDescending(x => x.EndedAt is null).ThenByDescending(x => x.StartedAt ?? DateTime.MinValue).ToList();
    public string? HolderName => Asset?.AssignedUserId is { } id ? store.Users.FirstOrDefault(x => x.Id == id)?.Name : null;
    public bool LoanOverdue => Asset is { AssignedUserId: not null, LoanDueDate: { } due } && due < AssetInsights.Today;
    // Set while a loan kit containing this asset is out. The kit owns the loan, so the per-asset loan and return
    // controls are replaced with a pointer to the Loans page (the store refuses both either way).
    public (LoanKit Kit, KitLoan Loan)? HeldByKit => Asset is null ? null : store.KitLoanHolding(Asset.Id);
    // Any kit this asset belongs to. Kit equipment is never loaned on its own, so the loan form is hidden for these
    // whether the kit is out or not.
    public LoanKit? PartOfKit => Asset is null ? null : store.KitContaining(Asset.Id);
    public IReadOnlyList<AssetAttributeDefinition> CustomAttributes => Asset is null ? [] : store.GetAssetAttributes(Asset.Type);
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
        Dictionary<Guid, string>? customAttributes,
        string? status,
        DateOnly? purchaseDate,
        string? purchasePrice,
        string? purchaseOrder,
        string? quoteReference,
        DateOnly? warrantyEnd,
        DateOnly? replacementDate)
    {
        if (!store.UserHasPermission(User, Permissions.ManageAssets)) return Forbid();
        if (string.IsNullOrWhiteSpace(assetTag) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(model))
        {
            Message = "Asset tag, type, and model are required.";
            return RedirectToPage(new { id });
        }
        if (AssetForm.DescribeInvalid(ModelState) is { } formError)
        {
            Message = formError;
            return RedirectToPage(new { id });
        }
        if (!AssetForm.TryPrice(purchasePrice, out var price))
        {
            Message = "Enter the purchase price as a positive amount, such as 349.99.";
            return RedirectToPage(new { id });
        }
        var chosenStatus = string.IsNullOrWhiteSpace(status) ? store.Assets.FirstOrDefault(x => x.Id == id)?.Status : store.AssetStatuses.FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
        if (chosenStatus is null)
        {
            Message = "Select a valid status.";
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
        var current = store.Assets.FirstOrDefault(x => x.Id == id);
        var makeModelChanged = current is null
            || !string.Equals(current.Make, (make ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.Model, model.Trim(), StringComparison.OrdinalIgnoreCase);
        if (makeModelChanged && !store.AssetModelMatchesMake(model, make))
        {
            Message = $"{model.Trim()} does not belong to {(make ?? string.Empty).Trim()}. Select a model for that make.";
            return RedirectToPage(new { id });
        }
        // A tag may not be changed to one another asset already uses; a repeated serial number only earns a warning.
        if (current is not null && !string.Equals(current.AssetTag, assetTag.Trim(), StringComparison.OrdinalIgnoreCase) && store.CheckAssetTag(assetTag, id) is { } tagError)
        {
            Message = tagError;
            return RedirectToPage(new { id });
        }
        var serialChanged = current is null || !string.Equals(current.SerialNumber, (serialNumber ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        var duplicateSerial = serialChanged ? store.FindDuplicateSerial(serialNumber, id) : null;
        var asset = new AssetRecord(id, assetTag.Trim(), (make ?? string.Empty).Trim(), model.Trim(), type.Trim(), (serialNumber ?? string.Empty).Trim(), (location ?? string.Empty).Trim(), assignedUserId, supplierId)
        {
            Status = chosenStatus,
            PurchaseDate = purchaseDate,
            PurchasePrice = price,
            PurchaseOrder = (purchaseOrder ?? string.Empty).Trim(),
            QuoteReference = (quoteReference ?? string.Empty).Trim(),
            // Disposal is set by its own action, never typed into the details form.
            DisposalDate = current?.DisposalDate,
            DisposalMethod = current?.DisposalMethod ?? string.Empty,
            DisposalProceeds = current?.DisposalProceeds,
            WarrantyEnd = warrantyEnd,
            ReplacementDate = replacementDate,
            // Deliberately not editable here. A due-back date is what makes an assignment a loan, and loans need a
            // reason, so they are set by Loan out and cleared by Return rather than typed into the details form.
            LoanDueDate = current?.LoanDueDate
        };
        if (!store.UpdateAssetAttributeValues(id, type.Trim(), customAttributes))
        {
            Message = "Asset was not found.";
            return RedirectToPage(new { id });
        }
        var saved = store.UpdateAsset(asset);
        Message = !saved ? "Asset was not found."
            : duplicateSerial is null ? "Asset updated."
            : $"Asset updated. Warning: serial number {(serialNumber ?? string.Empty).Trim()} is also recorded on asset {duplicateSerial.AssetTag}.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostLoan(Guid id, Guid? userId, DateOnly? dueBack, string? reason)
    {
        if (!store.UserHasPermission(User, Permissions.ManageAssets)) return Forbid();
        Message = userId is null || dueBack is null
            ? "Choose who the device is loaned to and the date it is due back."
            : store.LoanAsset(id, userId.Value, dueBack.Value, reason).Message;
        return RedirectToPage(new { id });
    }

    public IReadOnlyList<string> DisposalMethods => HelpdeskStore.DisposalMethods;
    public DateOnly Today => AssetInsights.Today;
    public bool IsDisposed => Asset is not null && HelpdeskStore.IsDisposed(Asset);

    public IActionResult OnPostDispose(Guid id, DateOnly? disposalDate, string? disposalMethod, string? disposalProceeds)
    {
        if (!store.UserHasPermission(User, Permissions.ManageAssets)) return Forbid();
        if (!AssetForm.TryPrice(disposalProceeds, out var proceeds))
        {
            Message = "Enter the proceeds as a positive amount, such as 45.00, or leave it blank.";
            return RedirectToPage(new { id });
        }
        Message = store.DisposeAsset(id, disposalDate, disposalMethod, proceeds).Message;
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostReturn(Guid id, string? status)
    {
        if (!store.UserHasPermission(User, Permissions.ManageAssets)) return Forbid();
        Message = store.ReturnAsset(id, status);
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostLinkTicket(Guid id, int? ticketNumber)
    {
        if (ticketNumber is null)
        {
            Message = "Select a ticket to link.";
            return RedirectToPage(new { id });
        }
        Message = store.LinkAssetToTicket(id, ticketNumber.Value) ? "Ticket linked." : "Ticket or asset was not found.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostUnlinkTicket(Guid id, int ticketNumber)
    {
        Message = store.UnlinkAssetFromTicket(id, ticketNumber) ? "Ticket unlinked." : "Ticket or asset was not found.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostAddComment(Guid id, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            Message = "Enter a comment before saving.";
            return RedirectToPage(new { id });
        }

        Message = store.AddAssetComment(id, comment) ? "Comment added." : "Asset was not found.";
        return RedirectToPage(new { id });
    }
}
