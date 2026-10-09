using System.Globalization;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The asset register: adding, editing, lending, returning, disposing and deleting assets, bulk changes, custom attributes, comments and ticket links.
public sealed partial class HelpdeskStore
{
    public void AddAsset(AssetRecord item)
    {
        lock (_sync)
        {
            AddAssetCore(item);
            Save();
        }
    }
    // Adds the asset with its first ownership period. The caller holds the lock and saves.
    private void AddAssetCore(AssetRecord item)
    {
        if (string.IsNullOrWhiteSpace(item.Status)) item = item with { Status = "In use" };
        if (!item.AssignedUserId.HasValue) item = item with { LoanDueDate = null };
        var assignments = item.AssignedUserId is { } holder
            ? new List<AssetAssignment> { new(holder, UserName(holder), DateTime.UtcNow, null, item.LoanDueDate) }
            : [];
        _data.Assets.Add(item with { Assignments = assignments });
    }
    // Null when the tag is free to use; otherwise the reason it can't be. Tags are compared ignoring case and surrounding spaces.
    public string? CheckAssetTag(string? tag, Guid? excludeAssetId)
    {
        lock (_sync)
        {
            var value = (tag ?? string.Empty).Trim();
            return _data.Assets.Any(x => x.Id != excludeAssetId && string.Equals(x.AssetTag, value, StringComparison.OrdinalIgnoreCase))
                ? $"Asset tag {value} is already used by another asset."
                : null;
        }
    }

    // A repeated serial number is allowed but worth a warning; returns the other asset that has it.
    public AssetRecord? FindDuplicateSerial(string? serialNumber, Guid? excludeAssetId)
    {
        lock (_sync)
        {
            var value = (serialNumber ?? string.Empty).Trim();
            return value.Length == 0 ? null : _data.Assets.FirstOrDefault(x => x.Id != excludeAssetId && string.Equals(x.SerialNumber, value, StringComparison.OrdinalIgnoreCase));
        }
    }
    // The two statuses the loan kit feature sets by itself. Looked up rather than assumed, because a school can rename
    // or delete any asset status - if one is missing, the asset keeps the status it already had.
    private const string OnLoanStatus = "On loan";
    private const string InStockStatus = "In stock or spare";
    public const string DisposedStatus = "Disposed";
    // A disposed asset has left the estate. It stays in the register for audit, but should not turn up anywhere that
    // implies it is still usable - see the guards in LoanAsset, IssueKit and LinkAssetToTicket, and the list filters.
    public static bool IsDisposed(AssetRecord asset) => string.Equals(asset.Status, DisposedStatus, StringComparison.OrdinalIgnoreCase);
    private string? ResolveAssetStatus(string name) => _data.AssetStatuses.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    // Why an asset that already has a holder cannot be lent or put in a kit, or null if it is free. Someone's own
    // laptop is theirs until it is returned: lending it on, or bundling it into a kit, would move it off their desk
    // without anyone deciding to take it back. The same goes for something already out on loan.
    private string? HeldBlock(AssetRecord asset, string action) => asset.AssignedUserId is not { } holder ? null
        : asset.LoanDueDate is { } due
            ? $"{asset.AssetTag} is already on loan to {UserName(holder)}, due back {AssetInsights.Format(due)}. Book it back in before it is {action}."
            : $"{asset.AssetTag} is assigned to {UserName(holder)}. Return it from the asset's page before it is {action}.";

    // A reason is required, from the same list kit loans use, so an individual loan can be told apart from a kit one in
    // the repeat-borrower report. Borrowers must be in the directory here - unlike kit loans, which accept a typed name.
    public (bool Ok, string Message) LoanAsset(Guid assetId, Guid userId, DateOnly dueBack, string? reason)
    {
        lock (_sync)
        {
            var chosenReason = _data.LoanReasons.FirstOrDefault(x => string.Equals(x, (reason ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosenReason is null) return (false, "Choose a reason for the loan.");
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null) return (false, "Asset was not found.");
            if (IsDisposed(asset)) return (false, $"{asset.AssetTag} has been disposed of and cannot be loaned out.");
            // Kit equipment is lent as a kit or not at all, whether or not the kit is currently out. Two reasons: the
            // kit would otherwise show as available while its laptop is on someone's desk, and loaning it separately
            // was also a way round the return block - loan it out, then book it back in.
            if (KitContainingCore(assetId) is { } owningKit)
                return (false, KitLoanHoldingCore(assetId) is { } held
                    ? $"This asset is out on loan with {owningKit.Name} ({held.Loan.BorrowerName}). Book the kit back in from the Loans page before loaning it separately."
                    : $"This asset is part of {owningKit.Name} and is only loaned out by issuing that kit from the Loans page. Remove it from the kit first if it needs to be loaned on its own.");
            if (HeldBlock(asset, "loaned out") is { } holderBlock) return (false, holderBlock);
            if (!_data.Users.Any(x => x.Id == userId)) return (false, "Select who the device is loaned to.");
            if (dueBack < AssetInsights.Today) return (false, "The due-back date cannot be in the past.");
            var status = ResolveAssetStatus(OnLoanStatus) ?? asset.Status;
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            ApplyAssetUpdate(index, asset with { AssignedUserId = userId, LoanDueDate = dueBack, Status = status }, chosenReason);
            Save();
            return (true, $"{asset.AssetTag} loaned to {UserName(userId)}, due back {AssetInsights.Format(dueBack)}.");
        }
    }
    // Takes an asset out of the estate. Deliberately not a delete: DeleteAsset removes the row, and an auditor needs
    // the record to survive - what it cost, when it was bought and what became of it.
    // The holder is cleared as well, because DeleteUser refuses while any asset is assigned to somebody, and a scrapped
    // laptop still showing a leaver as its holder would block deleting them for good.
    // disposedBy and certificate are the DfE evidence that it went *securely*: who did it (often the WEEE contractor)
    // and the recycling or data-destruction certificate number. Both optional, since a lost device has neither.
    public (bool Ok, string Message) DisposeAsset(Guid assetId, DateOnly? date, string? method, decimal? proceeds, string? disposedBy = null, string? certificate = null)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return (false, "Asset was not found.");
            var asset = _data.Assets[index];
            if (IsDisposed(asset)) return (false, $"{asset.AssetTag} is already recorded as disposed.");
            if (date is not { } disposedOn) return (false, "Enter the date it was disposed of.");
            if (disposedOn > AssetInsights.Today) return (false, "The disposal date cannot be in the future.");
            if (string.IsNullOrWhiteSpace(method)) return (false, "Choose how it was disposed of.");
            // Something still out with somebody, or sitting in a kit, is not ready to be written off.
            if (KitLoanHoldingCore(assetId) is { } held)
                return (false, $"{asset.AssetTag} is out on loan with {held.Kit.Name}. Book the kit back in first.");
            if (KitContainingCore(assetId) is { } kit)
                return (false, $"{asset.AssetTag} is part of {kit.Name}. Take it out of the kit before disposing of it.");
            if (asset.LoanDueDate is not null)
                return (false, $"{asset.AssetTag} is out on loan. Book it back in first.");

            var status = ResolveAssetStatus(DisposedStatus) ?? asset.Status;
            ApplyAssetUpdate(index, asset with
            {
                Status = status,
                AssignedUserId = null,
                LoanDueDate = null,
                DisposalDate = disposedOn,
                DisposalMethod = method.Trim(),
                DisposalProceeds = proceeds,
                DisposedBy = (disposedBy ?? string.Empty).Trim(),
                DisposalCertificate = (certificate ?? string.Empty).Trim()
            });
            Save();
            return (true, $"{asset.AssetTag} recorded as disposed on {AssetInsights.Format(disposedOn)}.");
        }
    }

    // The methods offered when disposing of an asset. Fixed rather than a managed list: they map to how a school
    // actually accounts for kit leaving, and the finance report groups on them. The DfE register's own methods (data
    // destruction, returned to lessor, part exchange) were added when EduInventory was merged in.
    public static readonly string[] DisposalMethods = ["Sold", "Recycled (WEEE)", "Data destruction and recycling", "Returned to lessor", "Part exchange", "Donated", "Written off", "Lost or stolen"];

    // DfE "Record a check": stamps the check date and who did it on each asset and sets the next check from the interval
    // in Settings → Inventory rules. Saves once for the lot. Disposed assets are skipped - there is nothing to check.
    // Unchanged: already carrying this exact check. Skipped: disposed.
    public (int Checked, int Unchanged, int Skipped, string? Error) RecordAssetChecks(IEnumerable<Guid> assetIds, DateOnly checkedOn, string? checkedBy)
    {
        lock (_sync)
        {
            if (checkedOn > AssetInsights.Today) return (0, 0, 0, "The check date cannot be in the future.");
            var by = (checkedBy ?? string.Empty).Trim();
            if (by.Length == 0) return (0, 0, 0, "Enter who did the check.");
            var next = AssetChecks.NextCheck(checkedOn, _data.CheckIntervalMonths);
            int count = 0, unchanged = 0, skipped = 0;
            foreach (var id in assetIds.Distinct())
            {
                var index = _data.Assets.FindIndex(x => x.Id == id);
                if (index < 0) continue;
                if (IsDisposed(_data.Assets[index])) { skipped++; continue; }
                var asset = _data.Assets[index];
                var updated = asset with { LastCheckDate = checkedOn, LastCheckBy = by, NextCheckDate = next };
                if (updated == asset) { unchanged++; continue; }
                ApplyAssetUpdate(index, updated);
                count++;
            }
            if (count > 0) Save();
            return (count, unchanged, skipped, null);
        }
    }

    // Ends the current holder's period. The status can be set at the same time, for example back to stock.
    public string ReturnAsset(Guid assetId, string? status)
    {
        lock (_sync)
        {
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null) return "Asset was not found.";
            // Checked before the "not assigned" test: a kit issued to someone outside the directory leaves no holder on
            // the asset, so that check alone would let this one through with a misleading message.
            if (KitLoanHoldingCore(assetId) is { } held)
                return $"This asset is out on loan with {held.Kit.Name} ({held.Loan.BorrowerName}). Book the kit back in from the Loans page instead.";
            if (!asset.AssignedUserId.HasValue) return "This asset is not currently assigned to anyone.";
            var newStatus = asset.Status;
            if (!string.IsNullOrWhiteSpace(status))
            {
                var match = _data.AssetStatuses.FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return "Select a valid status.";
                newStatus = match;
            }
            var holder = UserName(asset.AssignedUserId.Value);
            UpdateAsset(asset with { AssignedUserId = null, LoanDueDate = null, Status = newStatus });
            return $"Returned by {holder}.";
        }
    }
    public bool UpdateAsset(AssetRecord item)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == item.Id);
            if (index < 0) return false;
            ApplyAssetUpdate(index, item);
            Save();
            return true;
        }
    }
    // Replaces the asset at index, recording history and ownership changes. The caller holds the lock and saves.
    // loanReason and kitLoanId stamp the assignment period this creates, so the unified loan list can tell why the
    // asset went out and whether a kit loan already accounts for it. Both are null for an ordinary asset edit.
    private void ApplyAssetUpdate(int index, AssetRecord item, string? loanReason = null, Guid? kitLoanId = null)
    {
        var previous = _data.Assets[index];
        if (string.IsNullOrWhiteSpace(item.Status)) item = item with { Status = previous.Status };
        if (!item.AssignedUserId.HasValue) item = item with { LoanDueDate = null };
        var history = previous.History.ToList();
        var from = history.Count;
        AddAssetActivities(history, previous, item, _data.Users);
        StampActor(history, from);
        var assignments = previous.Assignments.ToList();
        var now = DateTime.UtcNow;
        if (previous.AssignedUserId != item.AssignedUserId)
        {
            for (var i = 0; i < assignments.Count; i++)
                if (assignments[i].EndedAt is null) assignments[i] = assignments[i] with { EndedAt = now };
            if (item.AssignedUserId is { } holder)
                assignments.Add(new AssetAssignment(holder, UserName(holder), now, null, item.LoanDueDate) { Reason = loanReason, KitLoanId = kitLoanId });
        }
        else if (previous.LoanDueDate != item.LoanDueDate)
        {
            var open = assignments.FindLastIndex(x => x.EndedAt is null);
            if (open >= 0) assignments[open] = assignments[open] with { DueBack = item.LoanDueDate };
        }
        _data.Assets[index] = item with { History = history, Comments = previous.Comments, Assignments = assignments };
    }

    // One or more of these can be set: the status, the owner (OwnerId null clears it) and the location (blank clears it).
    public sealed record AssetBulkChange(string? Status, bool ChangeOwner, Guid? OwnerId, bool ChangeLocation, string? Location);

    // Applies the same change to many assets and saves once, however many there are. Assets the change would not alter are counted, not touched.
    public (int Updated, int Unchanged, string? Error) BulkUpdateAssets(IEnumerable<Guid> assetIds, AssetBulkChange change)
    {
        lock (_sync)
        {
            if (change.Status is null && !change.ChangeOwner && !change.ChangeLocation) return (0, 0, "Choose what to change.");
            string? status = null;
            if (change.Status is not null)
            {
                status = _data.AssetStatuses.FirstOrDefault(x => string.Equals(x, change.Status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (status is null) return (0, 0, "Select a valid status.");
            }
            if (change.ChangeOwner && change.OwnerId.HasValue && !_data.Users.Any(x => x.Id == change.OwnerId.Value)) return (0, 0, "Select a valid user.");
            var location = string.Empty;
            if (change.ChangeLocation && !string.IsNullOrWhiteSpace(change.Location))
            {
                var match = _data.Locations.FirstOrDefault(x => string.Equals(x, change.Location.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return (0, 0, "Select a valid location.");
                location = match;
            }

            var updated = 0;
            var unchanged = 0;
            foreach (var id in assetIds.Distinct())
            {
                var index = _data.Assets.FindIndex(x => x.Id == id);
                if (index < 0) continue;
                var asset = _data.Assets[index];
                var next = asset;
                if (status is not null) next = next with { Status = status };
                if (change.ChangeOwner)
                {
                    // Handing a device to someone else is a normal assignment, not a continuation of the old loan.
                    next = next with { AssignedUserId = change.OwnerId, LoanDueDate = asset.AssignedUserId == change.OwnerId ? asset.LoanDueDate : null };
                }
                if (change.ChangeLocation) next = next with { Location = location };
                if (next == asset) { unchanged++; continue; }
                ApplyAssetUpdate(index, next);
                updated++;
            }
            if (updated > 0) Save();
            return (updated, unchanged, null);
        }
    }
    public IReadOnlyList<AssetAttributeValue> AssetAttributeValues { get { lock (_sync) return _data.AssetAttributeValues.ToList(); } }
    public bool AddAssetComment(Guid assetId, string text)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return false;
            var comments = _data.Assets[index].Comments.ToList();
            comments.Add(new AssetComment(text.Trim(), DateTime.UtcNow) { By = CurrentActor() });
            _data.Assets[index] = _data.Assets[index] with { Comments = comments };
            Save();
            return true;
        }
    }
    public bool LinkAssetToTicket(Guid assetId, int ticketNumber)
    {
        lock (_sync)
        {
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null) return false;
            // Nothing new gets linked to kit that has left the estate. Tickets already linked keep their link, so the
            // repair history of a disposed asset stays readable.
            if (IsDisposed(asset)) return false;
            var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (index < 0) return false;
            var ticket = _data.Tickets[index];
            if (ticket.AssetIds.Contains(assetId)) return true;
            var history = ticket.History.ToList();
            history.Add(new("Asset changed", $"Asset {asset.AssetTag} was linked.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { AssetIds = ticket.AssetIds.Append(assetId).ToList(), History = history };
            var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
            if (assetIndex >= 0)
            {
                var assetHistory = _data.Assets[assetIndex].History.ToList();
                assetHistory.Add(new("Ticket linked", $"Linked to ticket #{ticket.Number} - {ticket.Title}", DateTime.UtcNow) { By = CurrentActor() });
                _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
            }
            Save();
            return true;
        }
    }
    public bool UnlinkAssetFromTicket(Guid assetId, int ticketNumber)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (index < 0) return false;
            var ticket = _data.Tickets[index];
            if (!ticket.AssetIds.Contains(assetId)) return true;
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            var history = ticket.History.ToList();
            history.Add(new("Asset changed", $"Asset {asset?.AssetTag ?? "Unknown"} was unlinked.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { AssetIds = ticket.AssetIds.Where(x => x != assetId).ToList(), History = history };
            var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
            if (assetIndex >= 0)
            {
                var assetHistory = _data.Assets[assetIndex].History.ToList();
                assetHistory.Add(new("Ticket unlinked", $"Unlinked from ticket #{ticket.Number} - {ticket.Title}", DateTime.UtcNow) { By = CurrentActor() });
                _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
            }
            Save();
            return true;
        }
    }
    public IReadOnlyList<AssetAttributeValue> GetAssetAttributeValues(Guid assetId)
    {
        lock (_sync) return _data.AssetAttributeValues.Where(x => x.AssetId == assetId).ToList();
    }
    public string AddAssetAttributeDefinition(string name, IEnumerable<string>? assetTypes, string fieldType, string? choices)
    {
        lock (_sync)
        {
            name = (name ?? string.Empty).Trim();
            fieldType = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
            choices = NormalizeChoices(choices);
            if (string.IsNullOrWhiteSpace(name)) return "Attribute name is required.";
            var types = ResolveScope(assetTypes, _data.AssetTypes);
            if (types is null) return "Select valid asset types.";
            if (!IsAttributeType(fieldType)) return "Select a valid field type.";
            if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
            if (_data.AssetAttributeDefinitions.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.AssetTypes, types))) return DuplicateAttributeMessage(types, "asset types");
            _data.AssetAttributeDefinitions.Add(new(Guid.NewGuid(), name, fieldType, choices) { AssetTypes = types });
            Save();
            return "Custom attribute added.";
        }
    }
    public string AddAssetAttributeDefinition(string name, IEnumerable<string>? assetTypes) =>
        AddAssetAttributeDefinition(name, assetTypes, "single-line", null);
    public string UpdateAssetAttributeDefinition(Guid id, string name, IEnumerable<string>? assetTypes, string fieldType, string? choices)
    {
        lock (_sync)
        {
            name = (name ?? string.Empty).Trim();
            fieldType = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
            choices = NormalizeChoices(choices);
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            if (string.IsNullOrWhiteSpace(name)) return "Attribute name is required.";
            var types = ResolveScope(assetTypes, _data.AssetTypes);
            if (types is null) return "Select valid asset types.";
            if (!IsAttributeType(fieldType)) return "Select a valid field type.";
            if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
            if (_data.AssetAttributeDefinitions.Any(x => x.Id != id && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.AssetTypes, types))) return DuplicateAttributeMessage(types, "asset types");
            _data.AssetAttributeDefinitions[index] = new(id, name, fieldType, choices) { AssetTypes = types };
            Save();
            return "Custom attribute updated.";
        }
    }
    public string UpdateAssetAttributeDefinition(Guid id, string name, IEnumerable<string>? assetTypes) =>
        UpdateAssetAttributeDefinition(id, name, assetTypes, "single-line", null);
    public string SetAssetAttributeAssetTypes(Guid id, IEnumerable<string>? assetTypes)
    {
        lock (_sync)
        {
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            var types = ResolveScope(assetTypes, _data.AssetTypes);
            if (types is null) return "Select valid asset types.";
            var definition = _data.AssetAttributeDefinitions[index];
            if (_data.AssetAttributeDefinitions.Any(x => x.Id != id && x.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.AssetTypes, types))) return DuplicateAttributeMessage(types, "asset types");
            _data.AssetAttributeDefinitions[index] = definition with { AssetTypes = types };
            Save();
            return "Custom attribute updated.";
        }
    }
    public string DeleteAssetAttributeDefinition(Guid id)
    {
        lock (_sync)
        {
            if (id == Guid.Empty) return "A valid custom attribute is required.";
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            _data.AssetAttributeDefinitions.RemoveAt(index);
            _data.AssetAttributeValues.RemoveAll(x => x.AttributeDefinitionId == id);
            Save();
            return "Custom attribute deleted.";
        }
    }
    public bool UpdateAssetAttributeValues(Guid assetId, string assetType, IDictionary<Guid, string>? values)
    {
        lock (_sync)
        {
            if (!_data.Assets.Any(x => x.Id == assetId)) return false;
            _data.AssetAttributeValues.RemoveAll(x => x.AssetId == assetId);
            foreach (var definition in _data.AssetAttributeDefinitions.Where(x => x.AppliesTo(assetType)))
            {
                string? raw = null;
                values?.TryGetValue(definition.Id, out raw);
                var value = raw?.Trim() ?? string.Empty;
                if (definition.FieldType == "checkbox")
                    value = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                else if (definition.FieldType == "dropdown" && !string.IsNullOrEmpty(value) && !GetChoices(definition).Contains(value, StringComparer.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(value))
                    _data.AssetAttributeValues.Add(new(assetId, definition.Id, value));
            }
            Save();
            return true;
        }
    }

    public string? DeleteAsset(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.AssetIds.Contains(id)))
                return "This asset is linked to a ticket and cannot be deleted.";
            var item = _data.Assets.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Asset was not found.";
            // Deleting it would change the kit's contents, and a kit that is out is locked until it comes back.
            if (KitLoanHoldingCore(id) is { } held)
                return $"{item.AssetTag} is out on loan with {held.Kit.Name}. Book the kit back in before deleting it.";
            _data.Assets.Remove(item);
            for (var i = 0; i < _data.LoanKits.Count; i++)
                if (_data.LoanKits[i].AssetIds.Contains(id))
                    _data.LoanKits[i] = _data.LoanKits[i] with { AssetIds = _data.LoanKits[i].AssetIds.Where(x => x != id).ToList() };
            Save();
            return null;
        }
    }

    private static void AddAssetActivities(List<AssetActivity> history, AssetRecord previous, AssetRecord updated, IReadOnlyList<UserRecord> users)
    {
        var now = DateTime.UtcNow;
        string Person(Guid? id) => users.FirstOrDefault(x => x.Id == id)?.Name ?? "an unknown user";
        static string Date(DateOnly? value) => value.HasValue ? AssetInsights.Format(value.Value) : "(none)";
        static string Money(decimal? value) => value.HasValue ? value.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "(none)";
        static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        if (!string.Equals(previous.Status, updated.Status, StringComparison.Ordinal)) history.Add(new("Status changed", $"{previous.Status} -> {updated.Status}", now));
        if (previous.PurchaseDate != updated.PurchaseDate) history.Add(new("Purchase date changed", $"{Date(previous.PurchaseDate)} -> {Date(updated.PurchaseDate)}", now));
        if (previous.PurchasePrice != updated.PurchasePrice) history.Add(new("Purchase price changed", $"{Money(previous.PurchasePrice)} -> {Money(updated.PurchasePrice)}", now));
        if (!string.Equals(previous.PurchaseOrder, updated.PurchaseOrder, StringComparison.Ordinal)) history.Add(new("Purchase order changed", $"{Text(previous.PurchaseOrder)} -> {Text(updated.PurchaseOrder)}", now));
        if (previous.WarrantyEnd != updated.WarrantyEnd) history.Add(new("Warranty end changed", $"{Date(previous.WarrantyEnd)} -> {Date(updated.WarrantyEnd)}", now));
        if (previous.ReplacementDate != updated.ReplacementDate) history.Add(new("Replacement date changed", $"{Date(previous.ReplacementDate)} -> {Date(updated.ReplacementDate)}", now));
        if (previous.LoanDueDate != updated.LoanDueDate) history.Add(new("Loan due date changed", updated.LoanDueDate.HasValue ? $"Due back {Date(updated.LoanDueDate)}." : "The loan due date was cleared.", now));
        if (previous.AssetTag != updated.AssetTag) history.Add(new("Asset tag changed", $"{previous.AssetTag} -> {updated.AssetTag}", now));
        if (previous.Make != updated.Make) history.Add(new("Make changed", $"{previous.Make} -> {updated.Make}", now));
        if (previous.Model != updated.Model) history.Add(new("Model changed", $"{previous.Model} -> {updated.Model}", now));
        if (previous.Type != updated.Type) history.Add(new("Type changed", $"{previous.Type} -> {updated.Type}", now));
        if (previous.SerialNumber != updated.SerialNumber) history.Add(new("Serial number changed", $"{previous.SerialNumber} -> {updated.SerialNumber}", now));
        if (previous.Location != updated.Location) history.Add(new("Location changed", string.IsNullOrWhiteSpace(updated.Location) ? "The location was removed." : $"Moved to {updated.Location}.", now));
        if (previous.AssignedUserId != updated.AssignedUserId) history.Add(new("Assigned user changed", updated.AssignedUserId.HasValue ? $"Assigned to {Person(updated.AssignedUserId)}." : $"No longer assigned to {Person(previous.AssignedUserId)}.", now));
        if (previous.SupplierId != updated.SupplierId) history.Add(new("Supplier changed", updated.SupplierId.HasValue ? "A supplier was linked." : "The supplier was removed.", now));
        // DfE register fields.
        if (!string.Equals(previous.Building, updated.Building, StringComparison.Ordinal)) history.Add(new("Building changed", string.IsNullOrWhiteSpace(updated.Building) ? "The building was removed." : $"Moved to {updated.Building}.", now));
        if (!string.Equals(previous.OperatingSystem, updated.OperatingSystem, StringComparison.Ordinal)) history.Add(new("Operating system changed", $"{Text(previous.OperatingSystem)} -> {Text(updated.OperatingSystem)}", now));
        if (!string.Equals(previous.Condition, updated.Condition, StringComparison.Ordinal)) history.Add(new("Condition changed", $"{Text(previous.Condition)} -> {Text(updated.Condition)}", now));
        if (!string.Equals(previous.Ownership, updated.Ownership, StringComparison.Ordinal)) history.Add(new("Ownership changed", $"{AssetOwnership.Label(previous.Ownership)} -> {AssetOwnership.Label(updated.Ownership)}", now));
        if (previous.ContractId != updated.ContractId) history.Add(new("Contract changed", updated.ContractId.HasValue ? "Linked to a contract on the contracts register." : "No longer linked to a contract.", now));
        if (previous.EndOfSupport != updated.EndOfSupport) history.Add(new("End of support changed", $"{Date(previous.EndOfSupport)} -> {Date(updated.EndOfSupport)}", now));
        // A recorded check reads as one line, rather than three separate field changes.
        if (previous.LastCheckDate != updated.LastCheckDate || !string.Equals(previous.LastCheckBy, updated.LastCheckBy, StringComparison.Ordinal))
            history.Add(new("Check recorded", updated.LastCheckDate.HasValue
                ? $"Checked {Date(updated.LastCheckDate)}{(string.IsNullOrWhiteSpace(updated.LastCheckBy) ? "" : $" by {updated.LastCheckBy}")}; next check {Date(updated.NextCheckDate)}."
                : "The last check was cleared.", now));
        else if (previous.NextCheckDate != updated.NextCheckDate) history.Add(new("Next check changed", $"{Date(previous.NextCheckDate)} -> {Date(updated.NextCheckDate)}", now));
        if (previous.DisposalDate is null && updated.DisposalDate is { } disposed)
            history.Add(new("Disposed", $"{Date(disposed)}: {Text(updated.DisposalMethod)}{(string.IsNullOrWhiteSpace(updated.DisposedBy) ? "" : $", by {updated.DisposedBy}")}{(string.IsNullOrWhiteSpace(updated.DisposalCertificate) ? "" : $", certificate {updated.DisposalCertificate}")}.", now));
    }
}
