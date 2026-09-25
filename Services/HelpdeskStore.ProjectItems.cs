using System.Text.RegularExpressions;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// A project's items, their sub-items, and the suppliers asked to quote for each. Every change lands in the project's
// own history, so the audit log picks it up the same way it picks up everything else a project records.
public sealed partial class HelpdeskStore
{
    public const int MaxProjectItems = 50;
    public const int MaxSubItems = 50;
    public const int MaxItemSuppliers = 20;
    public const int MaxItemQuantity = 100_000;
    public const int MaxItemNameLength = 200;

    public (bool Ok, string Message) AddProjectItem(int number, string? name, int quantity) => EditProject(number, project =>
    {
        if (project.Items.Count >= MaxProjectItems) return Refuse($"A project can have up to {MaxProjectItems} items.");
        if (CheckItem(name, quantity) is { } error) return Refuse(error);
        var item = new ProjectItem(Guid.NewGuid(), name!.Trim(), quantity);
        return (project with { Items = [.. project.Items, item] }, "Item added.", $"Item added: {Describe(item)}.");
    });

    // The quickest way from what the requester typed to a list to work on: one item per line, reading a leading or
    // trailing quantity ("30 iPads", "30 x iPad", "iPad x30"). Only while the project has no items, so it can't be used
    // to pile a second copy on top of a list someone has already tidied.
    public (bool Ok, string Message) AddItemsFromRequest(int number) => EditProject(number, project =>
    {
        if (project.Items.Count > 0) return Refuse("This project already has items.");
        var items = project.ItemsWanted.Split('\n')
            .Select(ParseRequestLine).OfType<ProjectItem>()
            .Take(MaxProjectItems).ToList();
        if (items.Count == 0) return Refuse("There were no lines to turn into items.");
        return (project with { Items = items }, $"{items.Count} {(items.Count == 1 ? "item" : "items")} created from the request - check the names and quantities.",
            $"Items created from the request: {string.Join("; ", items.Select(Describe))}.");
    });

    private static readonly Regex LeadingQuantity = new(@"^(\d{1,6})\s*(?:x|×|\*|off)?\s+(.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex TrailingQuantity = new(@"^(.+?)\s*(?:x|×|\*)\s*(\d{1,6})$", RegexOptions.IgnoreCase);

    private static ProjectItem? ParseRequestLine(string line)
    {
        var text = line.Trim().TrimStart('-', '•', '*', '·').Trim();
        if (text.Length == 0) return null;
        var (name, quantity) = LeadingQuantity.Match(text) is { Success: true } lead ? (lead.Groups[2].Value, int.Parse(lead.Groups[1].Value))
            : TrailingQuantity.Match(text) is { Success: true } trail ? (trail.Groups[1].Value, int.Parse(trail.Groups[2].Value))
            : (text, 1);
        name = name.Trim();
        if (name.Length > MaxItemNameLength) name = name[..MaxItemNameLength];
        return new ProjectItem(Guid.NewGuid(), name, Math.Clamp(quantity, 1, MaxItemQuantity));
    }

    public (bool Ok, string Message) UpdateProjectItem(int number, Guid itemId, string? name, int quantity) => EditItem(number, itemId, item =>
    {
        if (CheckItem(name, quantity) is { } error) return RefuseItem(error);
        var updated = item with { Name = name!.Trim(), Quantity = quantity };
        return updated == item || (updated.Name == item.Name && updated.Quantity == item.Quantity)
            ? RefuseItem("Nothing had changed.")
            : (updated, "Item updated.", $"Item changed: {Describe(item)} → {Describe(updated)}.");
    });

    public (bool Ok, string Message) DeleteProjectItem(int number, Guid itemId) => EditProject(number, project =>
    {
        var item = project.Items.FirstOrDefault(x => x.Id == itemId);
        if (item is null) return Refuse("That item couldn't be found.");
        return (project with { Items = project.Items.Where(x => x.Id != itemId).ToList() }, "Item removed.",
            $"Item removed: {Describe(item)}" + (item.Suppliers.Count == 0 ? "." : $", with its {item.Suppliers.Count} {(item.Suppliers.Count == 1 ? "supplier" : "suppliers")}."));
    });

    // Moves an item one place up (-1) or down (+1), so the proposal can list them in a sensible order.
    public (bool Ok, string Message) MoveProjectItem(int number, Guid itemId, int direction) => EditProject(number, project =>
    {
        var index = project.Items.FindIndex(x => x.Id == itemId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= project.Items.Count) return Refuse("That item can't move any further.");
        var items = project.Items.ToList();
        (items[index], items[target]) = (items[target], items[index]);
        return (project with { Items = items }, "Item moved.", null);
    });

    public (bool Ok, string Message) AddSubItem(int number, Guid itemId, string? name, int quantity) => EditItem(number, itemId, item =>
    {
        if (item.SubItems.Count >= MaxSubItems) return RefuseItem($"An item can have up to {MaxSubItems} sub-items.");
        if (CheckItem(name, quantity) is { } error) return RefuseItem(error);
        var sub = new ProjectSubItem(Guid.NewGuid(), name!.Trim(), quantity);
        return (item with { SubItems = [.. item.SubItems, sub] }, "Sub-item added.", $"Sub-item added to {item.Name}: {sub.Quantity} × {sub.Name}.");
    });

    public (bool Ok, string Message) UpdateSubItem(int number, Guid itemId, Guid subItemId, string? name, int quantity) => EditItem(number, itemId, item =>
    {
        var sub = item.SubItems.FirstOrDefault(x => x.Id == subItemId);
        if (sub is null) return RefuseItem("That sub-item couldn't be found.");
        if (CheckItem(name, quantity) is { } error) return RefuseItem(error);
        var updated = sub with { Name = name!.Trim(), Quantity = quantity };
        if (updated == sub) return RefuseItem("Nothing had changed.");
        return (item with { SubItems = item.SubItems.Select(x => x.Id == subItemId ? updated : x).ToList() }, "Sub-item updated.",
            $"Sub-item on {item.Name} changed: {sub.Quantity} × {sub.Name} → {updated.Quantity} × {updated.Name}.");
    });

    public (bool Ok, string Message) DeleteSubItem(int number, Guid itemId, Guid subItemId) => EditItem(number, itemId, item =>
    {
        var sub = item.SubItems.FirstOrDefault(x => x.Id == subItemId);
        if (sub is null) return RefuseItem("That sub-item couldn't be found.");
        return (item with { SubItems = item.SubItems.Where(x => x.Id != subItemId).ToList() }, "Sub-item removed.",
            $"Sub-item removed from {item.Name}: {sub.Quantity} × {sub.Name}.");
    });

    // Puts a supplier on an item's quote list, or on every item's at once - the same firm is usually asked about the
    // whole project. Items that already have them are skipped rather than refused.
    public (bool Ok, string Message) AddItemSupplier(int number, Guid itemId, Guid supplierId, bool everyItem) => EditProject(number, project =>
    {
        var supplier = _data.Suppliers.FirstOrDefault(x => x.Id == supplierId);
        if (supplier is null) return Refuse("That supplier couldn't be found.");
        return AddSupplierTo(project, itemId, supplier, everyItem);
    });

    // Adds a supplier who isn't in the directory yet, from the project page. A name already in the directory is reused
    // rather than duplicated, so two technicians typing the same firm don't end up with two of it.
    public (bool Ok, string Message) QuickAddItemSupplier(int number, Guid itemId, string? name, string? email, bool everyItem) => EditProject(number, project =>
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) return Refuse("Enter the supplier's name.");
        if (value.Length > MaxItemNameLength) return Refuse($"Keep the supplier's name under {MaxItemNameLength} characters.");
        var mail = (email ?? string.Empty).Trim();
        if (mail.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(mail, out _)) return Refuse("That email address doesn't look right.");
        var supplier = _data.Suppliers.FirstOrDefault(x => string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase));
        var created = supplier is null;
        if (supplier is null)
        {
            supplier = new SupplierRecord(Guid.NewGuid(), value, "", mail, "", "", "", "", "", "", "", "", $"Added from {project.Reference}.", DateTime.UtcNow);
            _data.Suppliers.Add(supplier);
        }
        var result = AddSupplierTo(project, itemId, supplier, everyItem);
        if (result.Updated is null && created) _data.Suppliers.Remove(supplier);
        return result.Updated is null ? result : (result.Updated, (created ? $"{supplier.Name} added to the supplier directory. " : $"{supplier.Name} was already in the directory. ") + result.Message, result.History);
    });

    private (ProjectRecord? Updated, string Message, string? History) AddSupplierTo(ProjectRecord project, Guid itemId, SupplierRecord supplier, bool everyItem)
    {
        if (project.Items.All(x => x.Id != itemId)) return Refuse("That item couldn't be found.");
        var targets = project.Items.Where(x => (everyItem || x.Id == itemId) && x.Suppliers.All(s => s.SupplierId != supplier.Id)).ToList();
        if (targets.Count == 0) return Refuse($"{supplier.Name} is already on {(everyItem ? "every item" : "this item")}.");
        if (targets.Any(x => x.Suppliers.Count >= MaxItemSuppliers)) return Refuse($"An item can have up to {MaxItemSuppliers} suppliers.");
        var actor = CurrentActor();
        var ids = targets.Select(x => x.Id).ToHashSet();
        var items = project.Items.Select(x => ids.Contains(x.Id)
            ? x with { Suppliers = [.. x.Suppliers, new ItemSupplier(supplier.Id) { StatusHistory = [new QuoteStatusChange(QuoteStatuses.NotRequested, DateTime.UtcNow) { By = actor }] }] }
            : x).ToList();
        var names = string.Join(", ", targets.Select(x => x.Name));
        return (project with { Items = items }, $"{supplier.Name} added to {(targets.Count == 1 ? targets[0].Name : $"{targets.Count} items")}.",
            $"Supplier {supplier.Name} added to {names}.");
    }

    public (bool Ok, string Message) SetQuoteStatus(int number, Guid itemId, Guid supplierId, string? status) => EditSupplier(number, itemId, supplierId, (item, row, name) =>
    {
        var target = QuoteStatuses.Find(status);
        if (target is null) return RefuseSupplier("Choose a quote status.");
        if (target == row.Status) return RefuseSupplier("Nothing had changed.");
        var updated = row with { StatusHistory = [.. row.StatusHistory, new QuoteStatusChange(target, DateTime.UtcNow) { By = CurrentActor() }] };
        return (updated, $"{name}: {target}.", $"{name} for {item.Name}: {row.Status} → {target}.");
    });

    public (bool Ok, string Message) SetQuoteValidUntil(int number, Guid itemId, Guid supplierId, DateOnly? validUntil) => EditSupplier(number, itemId, supplierId, (item, row, name) =>
    {
        if (validUntil == row.ValidUntil) return RefuseSupplier("Nothing had changed.");
        var updated = row with { ValidUntil = validUntil };
        return (updated, validUntil is null ? $"{name}: valid-until date cleared." : $"{name}: quote valid until {Day(validUntil.Value)}.",
            $"{name}'s quote for {item.Name} valid until: {(row.ValidUntil is { } old ? Day(old) : "not set")} → {(validUntil is { } now ? Day(now) : "not set")}.");
    });

    public (bool Ok, string Message) RemoveItemSupplier(int number, Guid itemId, Guid supplierId) => EditItem(number, itemId, item =>
    {
        var row = item.Suppliers.FirstOrDefault(x => x.SupplierId == supplierId);
        if (row is null) return RefuseItem("That supplier isn't on this item.");
        var name = SupplierName(supplierId);
        return (item with { Suppliers = item.Suppliers.Where(x => x.SupplierId != supplierId).ToList() }, $"{name} removed from {item.Name}.",
            $"Supplier {name} removed from {item.Name} (quote status was {row.Status}).");
    });

    // The one funnel for item and quote changes: finds the project, refuses a closed one (its record is finished -
    // reopen it to change anything), applies the change, and records it in the project's history.
    private (bool Ok, string Message) EditProject(int number, Func<ProjectRecord, (ProjectRecord? Updated, string Message, string? History)> change)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var project = _data.Projects[index];
            if (!project.IsActive) return (false, "This project is closed. Reopen it to change its items or suppliers.");
            var (updated, message, history) = change(project);
            if (updated is null) return (message == "Nothing had changed.", message);
            _data.Projects[index] = history is null ? updated : WithHistory(updated, "Items and quotes", history);
            Save();
            return (true, message);
        }
    }

    private (bool Ok, string Message) EditItem(int number, Guid itemId, Func<ProjectItem, (ProjectItem? Updated, string Message, string? History)> change) =>
        EditProject(number, project =>
        {
            var item = project.Items.FirstOrDefault(x => x.Id == itemId);
            if (item is null) return Refuse("That item couldn't be found.");
            var (updated, message, history) = change(item);
            return updated is null ? Refuse(message) : (project with { Items = project.Items.Select(x => x.Id == itemId ? updated : x).ToList() }, message, history);
        });

    private (bool Ok, string Message) EditSupplier(int number, Guid itemId, Guid supplierId, Func<ProjectItem, ItemSupplier, string, (ItemSupplier? Updated, string Message, string? History)> change) =>
        EditItem(number, itemId, item =>
        {
            var row = item.Suppliers.FirstOrDefault(x => x.SupplierId == supplierId);
            if (row is null) return RefuseItem("That supplier isn't on this item.");
            var (updated, message, history) = change(item, row, SupplierName(supplierId));
            return updated is null ? RefuseItem(message) : (item with { Suppliers = item.Suppliers.Select(x => x.SupplierId == supplierId ? updated : x).ToList() }, message, history);
        });

    private string SupplierName(Guid supplierId) => _data.Suppliers.FirstOrDefault(x => x.Id == supplierId)?.Name ?? "Unknown supplier";

    private static (ProjectRecord? Updated, string Message, string? History) Refuse(string message) => (null, message, null);
    private static (ProjectItem? Updated, string Message, string? History) RefuseItem(string message) => (null, message, null);
    private static (ItemSupplier? Updated, string Message, string? History) RefuseSupplier(string message) => (null, message, null);

    private static string? CheckItem(string? name, int quantity)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Give it a name.";
        if (name.Trim().Length > MaxItemNameLength) return $"Keep names under {MaxItemNameLength} characters.";
        if (quantity is < 1 or > MaxItemQuantity) return $"Enter a quantity from 1 to {MaxItemQuantity:N0}.";
        return null;
    }

    private static string Describe(ProjectItem item) => $"{item.Quantity} × {item.Name}";
}
