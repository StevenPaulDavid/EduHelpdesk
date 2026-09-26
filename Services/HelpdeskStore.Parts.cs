using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Suppliers and the parts store: parts, bulk changes, stock adjustments and deliveries.
public sealed partial class HelpdeskStore
{
    // A repeated part SKU is allowed but worth a warning; returns the other part that has it.
    public PartRecord? FindDuplicateSku(string? sku, Guid? excludePartId)
    {
        lock (_sync)
        {
            var value = (sku ?? string.Empty).Trim();
            return value.Length == 0 ? null : _data.Parts.FirstOrDefault(x => x.Id != excludePartId && string.Equals(x.Sku, value, StringComparison.OrdinalIgnoreCase));
        }
    }
    public void AddSupplier(SupplierRecord item) { lock (_sync) { _data.Suppliers.Add(item); Save(); } }
    public bool UpdateSupplier(SupplierRecord item) => Update(item, _data.Suppliers, x => x.Id == item.Id);
    public string? DeleteSupplier(Guid id)
    {
        lock (_sync)
        {
            if (_data.Assets.Any(x => x.SupplierId == id)) return "This supplier is linked to assets and cannot be deleted.";
            if (_data.Parts.Any(x => x.SupplierIds.Contains(id))) return "This supplier is linked to parts and cannot be deleted.";
            // Their quotes are part of a project's record, so they stay until they are taken off the project.
            if (_data.Projects.FirstOrDefault(p => p.Items.Any(i => i.Suppliers.Any(s => s.SupplierId == id))) is { } project)
                return $"This supplier is on the quote list for {project.Reference} and cannot be deleted. Remove them from the project first.";
            var item = _data.Suppliers.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Supplier was not found.";
            _data.Suppliers.Remove(item); Save(); return null;
        }
    }
    public void AddPart(PartRecord item) { lock (_sync) { _data.Parts.Add(item); Save(); } }
    public bool UpdatePart(PartRecord item) => Update(item, _data.Parts, x => x.Id == item.Id);
    public string? DeletePart(Guid id)
    {
        lock (_sync)
        {
            if (_data.TicketParts.Any(x => x.PartId == id)) return "This part is assigned to a ticket and cannot be deleted.";
            var item = _data.Parts.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Part was not found.";
            _data.Parts.Remove(item); Save(); return null;
        }
    }
    public sealed record PartBulkChange(bool ChangeCategory, string? Category, bool ChangeLocation, string? Location);

    // Applies the same change to many parts and saves once, however many there are. Parts the change would not alter are counted, not touched.
    public (int Updated, int Unchanged, string? Error) BulkUpdateParts(IEnumerable<Guid> partIds, PartBulkChange change)
    {
        lock (_sync)
        {
            if (!change.ChangeCategory && !change.ChangeLocation) return (0, 0, "Choose what to change.");
            var category = string.Empty;
            if (change.ChangeCategory && !string.IsNullOrWhiteSpace(change.Category) && change.Category != PartListQuery.None)
            {
                var match = _data.PartCategories.FirstOrDefault(x => string.Equals(x, change.Category.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return (0, 0, "Select a valid category.");
                category = match;
            }
            var location = string.Empty;
            if (change.ChangeLocation && !string.IsNullOrWhiteSpace(change.Location) && change.Location != PartListQuery.None)
            {
                var match = _data.PartLocations.FirstOrDefault(x => string.Equals(x, change.Location.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return (0, 0, "Select a valid location.");
                location = match;
            }

            var updated = 0;
            var unchanged = 0;
            foreach (var id in partIds.Distinct())
            {
                var index = _data.Parts.FindIndex(x => x.Id == id);
                if (index < 0) continue;
                var part = _data.Parts[index];
                var next = part;
                if (change.ChangeCategory) next = next with { Category = category };
                if (change.ChangeLocation) next = next with { Location = location };
                if (next == part) { unchanged++; continue; }
                _data.Parts[index] = next;
                updated++;
            }
            if (updated > 0) Save();
            return (updated, unchanged, null);
        }
    }
    // Deletes many parts and saves once. A part still assigned to a ticket is skipped, same guard as DeletePart.
    public (int Deleted, int Skipped) BulkDeleteParts(IEnumerable<Guid> partIds)
    {
        lock (_sync)
        {
            var deleted = 0;
            var skipped = 0;
            foreach (var id in partIds.Distinct())
            {
                var item = _data.Parts.FirstOrDefault(x => x.Id == id);
                if (item is null) continue;
                if (_data.TicketParts.Any(x => x.PartId == id)) { skipped++; continue; }
                _data.Parts.Remove(item);
                deleted++;
            }
            if (deleted > 0) Save();
            return (deleted, skipped);
        }
    }
    // A dedicated, reasoned way to change QuantityOnHand, logged on the part's own History - unlike every other Part
    // field, which is just quietly diffed for the audit log. Replaces free editing of the field on the Edit page.
    public string? AdjustPartStock(Guid id, int newQuantity, string? reason)
    {
        lock (_sync)
        {
            if (newQuantity < 0) return "Enter a quantity of 0 or more.";
            if (string.IsNullOrWhiteSpace(reason)) return "Enter a reason for the adjustment.";
            var index = _data.Parts.FindIndex(x => x.Id == id);
            if (index < 0) return "Part was not found.";
            var part = _data.Parts[index];
            if (newQuantity == part.QuantityOnHand) return "That's already the quantity on hand.";
            var history = part.History.ToList();
            history.Add(new PartActivity("Stock adjusted", $"{part.QuantityOnHand} -> {newQuantity} ({reason.Trim()})", DateTime.UtcNow) { By = CurrentActor() });
            _data.Parts[index] = part with { QuantityOnHand = newQuantity, History = history };
            Save();
            return null;
        }
    }

    public const int MaxPartDelivery = 100_000;

    // Adds a delivery to what is on the shelf, which is how restocking actually happens: you know how many came in the
    // box, not what the new total should be. The total is worked out here, inside the lock, from the stored count - so
    // two people booking in deliveries at once both land, where typing a new total would lose one of them.
    public (bool Ok, string Message) RestockPart(Guid id, int delivered, string? note)
    {
        lock (_sync)
        {
            if (delivered < 1) return (false, "Enter how many were delivered - 1 or more.");
            if (delivered > MaxPartDelivery) return (false, $"Enter a delivery of {MaxPartDelivery:N0} or fewer.");
            var index = _data.Parts.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "Part was not found.");
            var part = _data.Parts[index];
            var total = part.QuantityOnHand + delivered;
            var details = $"+{delivered} delivered: {part.QuantityOnHand} -> {total}";
            if (!string.IsNullOrWhiteSpace(note)) details += $" ({note.Trim()})";
            var history = part.History.ToList();
            history.Add(new PartActivity("Restocked", details, DateTime.UtcNow) { By = CurrentActor() });
            _data.Parts[index] = part with { QuantityOnHand = total, History = history };
            Save();
            return (true, $"{delivered} added. {part.Name} now has {total} in stock.");
        }
    }
}
