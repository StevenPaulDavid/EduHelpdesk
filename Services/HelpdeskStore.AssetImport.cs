using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Importing assets from a spreadsheet: rows are matched to existing assets by asset tag (a match updates that asset, no match adds one).
public sealed partial class HelpdeskStore
{
    private const string ListType = "Asset type", ListMake = "Asset make", ListModel = "Asset model", ListLocation = "Location", ListStatus = "Asset status", ListSupplier = "Supplier",
        ListBuilding = "Building", ListCondition = "Asset condition";

    // What the import would do, without changing anything.
    public AssetImportPlan PlanAssetImport(IReadOnlyList<string[]> rows, IReadOnlyList<string?> targets, AssetImportOptions options)
    {
        lock (_sync) return RunAssetImport(rows, targets, options, null);
    }

    // Does it: creates and updates assets, adds any missing list values, and saves once for the whole file.
    public AssetImportPlan ApplyAssetImport(IReadOnlyList<string[]> rows, IReadOnlyList<string?> targets, AssetImportOptions options, string source)
    {
        lock (_sync) return RunAssetImport(rows, targets, options, source);
    }

    private sealed class ListNeed
    {
        public string List = "";
        public string Value = "";
        public string? Make;
    }

    private sealed class ImportRowPlan
    {
        public int RowNumber;
        public string Tag = "";
        public ImportAction Action = ImportAction.Unchanged;
        public string? Error;
        public readonly List<string> Changes = [];
        public readonly List<string> Warnings = [];
        public int ExistingIndex = -1;
        public AssetRecord? Next;
        public readonly List<(Guid Definition, string? Value)> Attributes = [];
        public readonly List<ListNeed> Needs = [];
        public SupplierRecord? NewSupplier;
    }

    // Values created earlier in the same import, so later rows can use them before they exist in the lists.
    private sealed class PendingLists
    {
        public readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Makes = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Locations = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Buildings = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Conditions = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Models = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SupplierRecord> Suppliers = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string List, string Value)> Created = [];
    }

    private AssetImportPlan RunAssetImport(IReadOnlyList<string[]> rows, IReadOnlyList<string?> targets, AssetImportOptions options, string? applySource)
    {
        var apply = applySource is not null;
        var column = new Dictionary<string, int>();
        for (var i = 0; i < targets.Count; i++)
            if (!string.IsNullOrWhiteSpace(targets[i]) && !column.ContainsKey(targets[i]!)) column[targets[i]!] = i;

        var results = new List<ImportRowResult>();
        var pending = new PendingLists();
        if (!column.ContainsKey("tag")) return new AssetImportPlan(results, pending.Created);

        var assetsByTag = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _data.Assets.Count; i++)
        {
            var key = _data.Assets[i].AssetTag.Trim();
            if (!assetsByTag.TryGetValue(key, out var list)) assetsByTag[key] = list = [];
            list.Add(i);
        }
        var usersByEmail = _data.Users.Where(x => !string.IsNullOrWhiteSpace(x.Email)).GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var usersByName = _data.Users.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var suppliersByName = new Dictionary<string, SupplierRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var supplier in _data.Suppliers) suppliersByName.TryAdd(supplier.Name.Trim(), supplier);
        var definitions = _data.AssetAttributeDefinitions.ToDictionary(x => x.Id);
        var attributeValues = new Dictionary<(Guid, Guid), string>();
        foreach (var value in _data.AssetAttributeValues) attributeValues.TryAdd((value.AssetId, value.AttributeDefinitionId), value.Value);
        var serialOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in _data.Assets)
            if (!string.IsNullOrWhiteSpace(asset.SerialNumber)) serialOwners.TryAdd(asset.SerialNumber.Trim(), asset.AssetTag);
        var seenTags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var defaultStatus = _data.AssetStatuses.FirstOrDefault(x => string.Equals(x, options.DefaultStatus?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? _data.AssetStatuses.FirstOrDefault() ?? "In use";

        for (var r = 0; r < rows.Count; r++)
        {
            var plan = PlanImportRow(rows[r], r + 2, column, options, defaultStatus, assetsByTag, usersByEmail, usersByName, suppliersByName, definitions, attributeValues, serialOwners, seenTags, pending);
            results.Add(new ImportRowResult(plan.RowNumber, plan.Tag, plan.Error is not null ? ImportAction.Error : plan.Action, plan.Changes, plan.Warnings, plan.Error));
            if (plan.Error is not null) continue;

            // The row is good: what it needs becomes available to the rows after it.
            foreach (var need in plan.Needs) RegisterNeed(pending, need, plan.NewSupplier, apply);
            if (plan.Next is not null && !string.IsNullOrWhiteSpace(plan.Next.SerialNumber)) serialOwners.TryAdd(plan.Next.SerialNumber.Trim(), plan.Tag);
            if (!apply || plan.Action is ImportAction.Unchanged) continue;

            if (plan.Action == ImportAction.Create) AddAssetCore(plan.Next!);
            else ApplyAssetUpdate(plan.ExistingIndex, plan.Next!);
            foreach (var (definitionId, value) in plan.Attributes)
            {
                _data.AssetAttributeValues.RemoveAll(x => x.AssetId == plan.Next!.Id && x.AttributeDefinitionId == definitionId);
                if (!string.IsNullOrEmpty(value)) _data.AssetAttributeValues.Add(new AssetAttributeValue(plan.Next!.Id, definitionId, value));
            }
        }

        var outcome = new AssetImportPlan(results, pending.Created);
        if (apply && (outcome.Created > 0 || outcome.Updated > 0))
        {
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Assets", null, null, "Asset import", "Imported",
                $"{applySource}: {outcome.Created} created, {outcome.Updated} updated, {outcome.Unchanged} unchanged, {outcome.Errors} skipped."));
            Save();
        }
        return outcome;
    }

    private void RegisterNeed(PendingLists pending, ListNeed need, SupplierRecord? newSupplier, bool apply)
    {
        var added = false;
        switch (need.List)
        {
            case ListType: added = pending.Types.Add(need.Value); if (added && apply) _data.AssetTypes.Add(need.Value); break;
            case ListMake: added = pending.Makes.Add(need.Value); if (added && apply) _data.AssetMakes.Add(need.Value); break;
            case ListLocation: added = pending.Locations.Add(need.Value); if (added && apply) _data.Locations.Add(need.Value); break;
            case ListStatus: added = pending.Statuses.Add(need.Value); if (added && apply) _data.AssetStatuses.Add(need.Value); break;
            case ListBuilding: added = pending.Buildings.Add(need.Value); if (added && apply) _data.Buildings.Add(need.Value); break;
            case ListCondition: added = pending.Conditions.Add(need.Value); if (added && apply) _data.AssetConditions.Add(need.Value); break;
            case ListModel:
                added = pending.Models.TryAdd(need.Value, need.Make ?? string.Empty);
                if (added && apply)
                {
                    _data.AssetModels.Add(need.Value);
                    if (!string.IsNullOrWhiteSpace(need.Make)) _data.AssetModelMakes[need.Value] = need.Make;
                }
                break;
            case ListSupplier:
                if (newSupplier is not null && pending.Suppliers.TryAdd(need.Value, newSupplier)) { added = true; if (apply) _data.Suppliers.Add(newSupplier); }
                break;
        }
        if (added) pending.Created.Add((need.List, need.Value));
    }

    private ImportRowPlan PlanImportRow(
        string[] cells, int rowNumber, Dictionary<string, int> column, AssetImportOptions options, string defaultStatus,
        Dictionary<string, List<int>> assetsByTag, Dictionary<string, List<UserRecord>> usersByEmail, Dictionary<string, List<UserRecord>> usersByName,
        Dictionary<string, SupplierRecord> suppliersByName, Dictionary<Guid, AssetAttributeDefinition> definitions, Dictionary<(Guid, Guid), string> attributeValues,
        Dictionary<string, string> serialOwners, Dictionary<string, int> seenTags, PendingLists pending)
    {
        var row = new ImportRowPlan { RowNumber = rowNumber, Action = ImportAction.Unchanged };
        ImportRowPlan Fail(string message) { row.Error ??= message; return row; }

        // null: the column is not mapped. "": the cell is blank.
        string? Cell(string key) => column.TryGetValue(key, out var index) ? (index < cells.Length ? cells[index].Trim() : string.Empty) : null;
        // A value to apply, or a request to clear the field (blank cell with the "blank clears" option), or neither.
        (bool Has, string Value, bool Clear) Field(string key, bool canClear)
        {
            var text = Cell(key);
            if (text is null) return (false, string.Empty, false);
            if (text.Length == 0) return (false, string.Empty, options.BlankClears && canClear);
            return (true, text, false);
        }

        var tag = Cell("tag") ?? string.Empty;
        if (tag.Length == 0) return Fail("The asset tag is blank.");
        row.Tag = tag;
        if (seenTags.TryGetValue(tag, out var firstRow)) return Fail($"This tag also appears on row {firstRow}; only the first one is used.");
        seenTags[tag] = rowNumber;

        assetsByTag.TryGetValue(tag, out var matches);
        if (matches is { Count: > 1 }) return Fail("More than one existing asset has this tag, so it cannot be matched. Fix the duplicate tags first.");
        var existing = matches is { Count: 1 } ? _data.Assets[matches[0]] : null;
        var isNew = existing is null;
        row.ExistingIndex = matches is { Count: 1 } ? matches[0] : -1;
        var next = existing ?? new AssetRecord(Guid.NewGuid(), tag, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null) { Status = defaultStatus };

        // Looks a value up in a list (including values created by earlier rows), or asks for it to be created.
        string? Resolve(string list, string value, IReadOnlyList<string> existingValues, HashSet<string> pendingValues, string? makeForModel = null)
        {
            var match = existingValues.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            if (pendingValues.TryGetValue(value, out var created)) return created;
            if (!options.CreateMissing) { Fail($"{list} '{value}' is not in the list."); return null; }
            row.Needs.Add(new ListNeed { List = list, Value = value, Make = makeForModel });
            return value;
        }

        // Type first: it decides which custom attributes apply.
        var type = Field("type", false);
        if (type.Has && Resolve(ListType, type.Value, _data.AssetTypes, pending.Types) is { } resolvedType) next = next with { Type = resolvedType };
        if (row.Error is not null) return row;
        if (string.IsNullOrWhiteSpace(next.Type) && isNew && !string.IsNullOrWhiteSpace(options.DefaultType))
            next = next with { Type = _data.AssetTypes.FirstOrDefault(x => string.Equals(x, options.DefaultType.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty };

        var make = Field("make", true);
        if (make.Has && Resolve(ListMake, make.Value, _data.AssetMakes, pending.Makes) is { } resolvedMake) next = next with { Make = resolvedMake };
        else if (make.Clear) next = next with { Make = string.Empty };
        if (row.Error is not null) return row;

        var model = Field("model", false);
        if (model.Has)
        {
            var existingModel = _data.AssetModels.FirstOrDefault(x => string.Equals(x, model.Value, StringComparison.OrdinalIgnoreCase));
            if (existingModel is not null) next = next with { Model = existingModel };
            else if (pending.Models.Keys.FirstOrDefault(x => string.Equals(x, model.Value, StringComparison.OrdinalIgnoreCase)) is { } pendingModel) next = next with { Model = pendingModel };
            else if (!options.CreateMissing) return Fail($"Asset model '{model.Value}' is not in the list.");
            else
            {
                row.Needs.Add(new ListNeed { List = ListModel, Value = model.Value, Make = string.IsNullOrWhiteSpace(next.Make) ? null : next.Make });
                next = next with { Model = model.Value };
            }
        }

        var location = Field("location", true);
        if (location.Has && Resolve(ListLocation, location.Value, _data.Locations, pending.Locations) is { } resolvedLocation) next = next with { Location = resolvedLocation };
        else if (location.Clear) next = next with { Location = string.Empty };
        if (row.Error is not null) return row;

        var status = Field("status", false);
        if (status.Has && Resolve(ListStatus, status.Value, _data.AssetStatuses, pending.Statuses) is { } resolvedStatus) next = next with { Status = resolvedStatus };
        if (row.Error is not null) return row;

        // The DfE register's columns.
        var building = Field("building", true);
        if (building.Has && Resolve(ListBuilding, building.Value, _data.Buildings, pending.Buildings) is { } resolvedBuilding) next = next with { Building = resolvedBuilding };
        else if (building.Clear) next = next with { Building = string.Empty };
        if (row.Error is not null) return row;
        var condition = Field("condition", true);
        if (condition.Has && Resolve(ListCondition, condition.Value, _data.AssetConditions, pending.Conditions) is { } resolvedCondition) next = next with { Condition = resolvedCondition };
        else if (condition.Clear) next = next with { Condition = string.Empty };
        if (row.Error is not null) return row;
        var os = Field("os", true);
        if (os.Has) next = next with { OperatingSystem = os.Value };
        else if (os.Clear) next = next with { OperatingSystem = string.Empty };
        var checkedBy = Field("lastCheckBy", true);
        if (checkedBy.Has) next = next with { LastCheckBy = checkedBy.Value };
        else if (checkedBy.Clear) next = next with { LastCheckBy = string.Empty };
        // The DfE template asks "Owned by school?" (Yes/No) and "Leased or loaned?"; this column takes either wording.
        var ownership = Field("ownership", true);
        if (ownership.Has)
        {
            var text = ownership.Value.Trim().ToLowerInvariant();
            next = next with
            {
                Ownership = text.Contains("leas") ? AssetOwnership.Leased
                    : text.Contains("loan") ? AssetOwnership.Loaned
                    : text is "no" or "n" or "false" ? AssetOwnership.Leased
                    : AssetOwnership.Owned
            };
        }
        else if (ownership.Clear) next = next with { Ownership = AssetOwnership.Owned };

        var serial = Field("serial", true);
        if (serial.Has) next = next with { SerialNumber = serial.Value };
        else if (serial.Clear) next = next with { SerialNumber = string.Empty };
        var purchaseOrder = Field("purchaseOrder", true);
        if (purchaseOrder.Has) next = next with { PurchaseOrder = purchaseOrder.Value };
        else if (purchaseOrder.Clear) next = next with { PurchaseOrder = string.Empty };
            var quoteReference = Field("quoteReference", true);
            if (quoteReference.Has) next = next with { QuoteReference = quoteReference.Value };
            else if (quoteReference.Clear) next = next with { QuoteReference = string.Empty };

        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(next.Type)) return Fail("A new asset needs a type. Map a Type column, or choose a default type.");
            if (string.IsNullOrWhiteSpace(next.Model)) return Fail("A new asset needs a model.");
        }
        // A model that belongs to a make can't be given to another make. Unchanged pairs on existing assets are left alone.
        var pairChanged = isNew || !string.Equals(existing!.Make, next.Make, StringComparison.OrdinalIgnoreCase) || !string.Equals(existing.Model, next.Model, StringComparison.OrdinalIgnoreCase);
        if (pairChanged && !string.IsNullOrWhiteSpace(next.Make) && !string.IsNullOrWhiteSpace(next.Model))
        {
            var linked = _data.AssetModelMakes.TryGetValue(next.Model, out var linkedMake) ? linkedMake : pending.Models.TryGetValue(next.Model, out var pendingMake) && pendingMake.Length > 0 ? pendingMake : null;
            if (linked is not null && !string.Equals(linked, next.Make, StringComparison.OrdinalIgnoreCase)) return Fail($"Model '{next.Model}' belongs to {linked}, not {next.Make}.");
        }

        // Holder
        var holder = Field("holder", true);
        Guid? holderId = next.AssignedUserId;
        if (holder.Has)
        {
            var found = usersByEmail.GetValueOrDefault(holder.Value) ?? usersByName.GetValueOrDefault(holder.Value);
            if (found is null || found.Count == 0) return Fail($"No user matches '{holder.Value}'.");
            if (found.Count > 1) return Fail($"More than one user is called '{holder.Value}'; use their email address instead.");
            holderId = found[0].Id;
        }
        else if (holder.Clear) holderId = null;
        var holderChanged = holderId != next.AssignedUserId;
        next = next with { AssignedUserId = holderId };

        var loan = Cell("loanDue");
        if (!string.IsNullOrEmpty(loan))
        {
            if (!ImportParsing.TryParseDate(loan, options.DateFormat, out var loanDue)) return Fail($"'{loan}' is not a valid loan due date.");
            next = next with { LoanDueDate = loanDue };
        }
        else if (holderChanged || (loan is not null && options.BlankClears)) next = next with { LoanDueDate = null };
        if (next.LoanDueDate.HasValue && holderId is null) return Fail("A loan due date needs someone assigned to the asset.");

        // Supplier
        var supplier = Field("supplier", true);
        if (supplier.Has)
        {
            if (suppliersByName.TryGetValue(supplier.Value, out var knownSupplier)) next = next with { SupplierId = knownSupplier.Id };
            else if (pending.Suppliers.TryGetValue(supplier.Value, out var pendingSupplier)) next = next with { SupplierId = pendingSupplier.Id };
            else if (!options.CreateMissing) return Fail($"Supplier '{supplier.Value}' does not exist.");
            else
            {
                row.NewSupplier = new SupplierRecord(Guid.NewGuid(), supplier.Value, "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);
                row.Needs.Add(new ListNeed { List = ListSupplier, Value = supplier.Value });
                next = next with { SupplierId = row.NewSupplier.Id };
            }
        }
        else if (supplier.Clear) next = next with { SupplierId = null };

        // Dates and price
        DateOnly? ParseDateField(string key, string label, DateOnly? current, out bool failed)
        {
            failed = false;
            var text = Cell(key);
            if (text is null) return current;
            if (text.Length == 0) return options.BlankClears ? null : current;
            if (!ImportParsing.TryParseDate(text, options.DateFormat, out var parsed)) { failed = true; Fail($"'{text}' is not a valid {label}."); return current; }
            return parsed;
        }
        var purchaseDate = ParseDateField("purchaseDate", "purchase date", next.PurchaseDate, out var failedPurchase);
        var warrantyEnd = ParseDateField("warrantyEnd", "warranty end date", next.WarrantyEnd, out var failedWarranty);
        var replacement = ParseDateField("replacementDate", "replacement date", next.ReplacementDate, out var failedReplacement);
        var lastCheck = ParseDateField("lastCheck", "last check date", next.LastCheckDate, out var failedLastCheck);
        var nextCheck = ParseDateField("nextCheck", "next check date", next.NextCheckDate, out var failedNextCheck);
        var endOfSupport = ParseDateField("endOfSupport", "end of support date", next.EndOfSupport, out var failedSupport);
        if (failedPurchase || failedWarranty || failedReplacement || failedLastCheck || failedNextCheck || failedSupport) return row;
        next = next with { LastCheckDate = lastCheck, NextCheckDate = nextCheck, EndOfSupport = endOfSupport };
        // An exported file carries the calculated replacement date; typing that same date back must not turn it into an override.
        if (existing is not null && existing.ReplacementDate is null && replacement.HasValue && replacement == AssetInsights.ReplacementDate(existing, _data.AssetTypeLifespans)) replacement = null;
        next = next with { PurchaseDate = purchaseDate, WarrantyEnd = warrantyEnd, ReplacementDate = replacement };

        var price = Cell("purchasePrice");
        if (price is not null)
        {
            if (price.Length == 0) { if (options.BlankClears) next = next with { PurchasePrice = null }; }
            else if (!ImportParsing.TryParsePrice(price, out var parsedPrice)) return Fail($"'{price}' is not a valid purchase price.");
            else next = next with { PurchasePrice = parsedPrice };
        }

        // Custom attributes
        foreach (var (key, index) in column.Where(x => x.Key.StartsWith(AssetImportTargets.AttributePrefix)))
        {
            if (!Guid.TryParse(key[AssetImportTargets.AttributePrefix.Length..], out var definitionId) || !definitions.TryGetValue(definitionId, out var definition)) continue;
            var raw = index < cells.Length ? cells[index].Trim() : string.Empty;
            if (!definition.AppliesTo(next.Type))
            {
                if (raw.Length > 0) row.Warnings.Add($"'{definition.Name}' was skipped because it does not apply to {(string.IsNullOrWhiteSpace(next.Type) ? "assets with no type" : next.Type)}.");
                continue;
            }
            string? value;
            if (raw.Length == 0) { if (!options.BlankClears) continue; value = null; }
            else if (definition.FieldType == "checkbox")
            {
                if (!ImportParsing.TryParseBool(raw, out var flag)) return Fail($"'{raw}' is not yes or no for '{definition.Name}'.");
                value = flag ? "true" : "false";
            }
            else if (definition.FieldType == "dropdown")
            {
                var choice = GetChoices(definition).FirstOrDefault(x => string.Equals(x, raw, StringComparison.OrdinalIgnoreCase));
                if (choice is null) return Fail($"'{raw}' is not one of the choices for '{definition.Name}'.");
                value = choice;
            }
            else value = raw;
            var current = existing is not null && attributeValues.TryGetValue((existing.Id, definitionId), out var stored) ? stored : null;
            if (!string.Equals(current ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal)) row.Attributes.Add((definitionId, value));
        }

        // A repeated serial number is allowed, but worth knowing about.
        if (!string.IsNullOrWhiteSpace(next.SerialNumber) && (isNew || !string.Equals(existing!.SerialNumber, next.SerialNumber, StringComparison.OrdinalIgnoreCase))
            && serialOwners.TryGetValue(next.SerialNumber.Trim(), out var otherTag) && !string.Equals(otherTag, tag, StringComparison.OrdinalIgnoreCase))
            row.Warnings.Add($"Serial number {next.SerialNumber} is also on asset {otherTag}.");

        row.Next = next;
        if (isNew)
        {
            row.Action = ImportAction.Create;
            static string Date(DateOnly? d) => d.HasValue ? AssetInsights.Format(d.Value) : string.Empty;
            var supplierName = next.SupplierId is { } supplierId
                ? suppliersByName.Values.FirstOrDefault(x => x.Id == supplierId)?.Name ?? pending.Suppliers.Values.FirstOrDefault(x => x.Id == supplierId)?.Name
                : null;
            foreach (var (label, value) in new[]
            {
                ("Make", next.Make), ("Model", next.Model), ("Type", next.Type), ("Serial number", next.SerialNumber), ("Status", next.Status), ("Location", next.Location),
                ("Assigned to", next.AssignedUserId is { } holderName ? UserName(holderName) : null), ("Loan due back", Date(next.LoanDueDate)), ("Supplier", supplierName),
                ("Purchase date", Date(next.PurchaseDate)), ("Purchase price", next.PurchasePrice?.ToString("0.00")), ("Purchase order", next.PurchaseOrder), ("Quote reference", next.QuoteReference),
                ("Warranty end", Date(next.WarrantyEnd)), ("Replacement date", Date(next.ReplacementDate)),
                ("Building", next.Building), ("Operating system", next.OperatingSystem), ("Condition", next.Condition),
                ("Ownership", string.IsNullOrEmpty(next.Ownership) ? null : AssetOwnership.Label(next.Ownership)),
                ("Last check", Date(next.LastCheckDate)), ("Checked by", next.LastCheckBy), ("Next check", Date(next.NextCheckDate)), ("End of support", Date(next.EndOfSupport))
            })
                if (!string.IsNullOrWhiteSpace(value)) row.Changes.Add($"{label}: {value}");
            foreach (var (definitionId, value) in row.Attributes)
                if (!string.IsNullOrEmpty(value)) row.Changes.Add($"{definitions[definitionId].Name}: {value}");
            return row;
        }

        // What an update would change, field by field.
        void Change(string label, string? before, string? after)
        {
            if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
                row.Changes.Add($"{label}: {(string.IsNullOrEmpty(before) ? "(empty)" : before)} → {(string.IsNullOrEmpty(after) ? "(empty)" : after)}");
        }
        string Day(DateOnly? d) => d.HasValue ? AssetInsights.Format(d.Value) : string.Empty;
        string SupplierName(Guid? id) => id is null ? string.Empty : suppliersByName.Values.FirstOrDefault(x => x.Id == id)?.Name ?? pending.Suppliers.Values.FirstOrDefault(x => x.Id == id)?.Name ?? string.Empty;
        Change("Make", existing!.Make, next.Make);
        Change("Model", existing.Model, next.Model);
        Change("Type", existing.Type, next.Type);
        Change("Serial number", existing.SerialNumber, next.SerialNumber);
        Change("Status", existing.Status, next.Status);
        Change("Location", existing.Location, next.Location);
        Change("Assigned to", existing.AssignedUserId is { } was ? UserName(was) : string.Empty, next.AssignedUserId is { } now ? UserName(now) : string.Empty);
        Change("Loan due back", Day(existing.LoanDueDate), Day(next.LoanDueDate));
        Change("Supplier", SupplierName(existing.SupplierId), SupplierName(next.SupplierId));
        Change("Purchase date", Day(existing.PurchaseDate), Day(next.PurchaseDate));
        Change("Purchase price", existing.PurchasePrice?.ToString("0.00"), next.PurchasePrice?.ToString("0.00"));
        Change("Purchase order", existing.PurchaseOrder, next.PurchaseOrder);
        Change("Quote reference", existing.QuoteReference, next.QuoteReference);
        Change("Warranty end", Day(existing.WarrantyEnd), Day(next.WarrantyEnd));
        Change("Replacement date", Day(existing.ReplacementDate), Day(next.ReplacementDate));
        Change("Building", existing.Building, next.Building);
        Change("Operating system", existing.OperatingSystem, next.OperatingSystem);
        Change("Condition", existing.Condition, next.Condition);
        Change("Ownership", AssetOwnership.Label(existing.Ownership), AssetOwnership.Label(next.Ownership));
        Change("Last check", Day(existing.LastCheckDate), Day(next.LastCheckDate));
        Change("Checked by", existing.LastCheckBy, next.LastCheckBy);
        Change("Next check", Day(existing.NextCheckDate), Day(next.NextCheckDate));
        Change("End of support", Day(existing.EndOfSupport), Day(next.EndOfSupport));
        foreach (var (definitionId, value) in row.Attributes)
            Change(definitions[definitionId].Name, attributeValues.TryGetValue((existing.Id, definitionId), out var before) ? before : null, value);
        row.Action = row.Changes.Count == 0 ? ImportAction.Unchanged : ImportAction.Update;
        return row;
    }
}
