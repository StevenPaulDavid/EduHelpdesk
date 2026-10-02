using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The service catalogue: common problems and requests, filed under a ticket category, that staff pick in the portal
// instead of typing a title. Anything not listed is still typed by hand - the catalogue never limits what can be reported.
public sealed partial class HelpdeskStore
{
    public const int MaxServiceItemNameLength = 120;

    // Grouped the way the portal shows them: categories in their own order, items alphabetical within each.
    public IReadOnlyList<ServiceItem> ServiceItems
    {
        get
        {
            lock (_sync)
            {
                var order = _data.Categories.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
                return _data.ServiceItems
                    .OrderBy(x => order.GetValueOrDefault(x.Category, int.MaxValue))
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    public ServiceItem? GetServiceItem(Guid id)
    {
        lock (_sync) return _data.ServiceItems.FirstOrDefault(x => x.Id == id);
    }

    // The item a requester picked, if it really is in that category - the portal posts text, so it is checked again here.
    public ServiceItem? FindServiceItem(string? category, string? name)
    {
        lock (_sync)
            return _data.ServiceItems.FirstOrDefault(x => string.Equals(x.Category, category?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string AddServiceItem(string? category, string? name, string? defaultPriority)
    {
        lock (_sync)
        {
            var (item, error) = BuildServiceItem(Guid.NewGuid(), category, name, defaultPriority);
            if (item is null) return error!;
            _data.ServiceItems.Add(item);
            Save();
            return "Catalogue item added.";
        }
    }

    public string UpdateServiceItem(Guid id, string? category, string? name, string? defaultPriority)
    {
        lock (_sync)
        {
            var index = _data.ServiceItems.FindIndex(x => x.Id == id);
            if (index < 0) return "Catalogue item was not found.";
            var old = _data.ServiceItems[index];
            var (item, error) = BuildServiceItem(id, category, name, defaultPriority);
            if (item is null) return error!;
            _data.ServiceItems[index] = item;
            // A rename follows through to the tickets raised from it, as a category rename does. Only within the same
            // category: moving an item to another one leaves old tickets saying what they were raised as.
            if (!string.Equals(old.Name, item.Name, StringComparison.Ordinal) && string.Equals(old.Category, item.Category, StringComparison.OrdinalIgnoreCase))
                for (var i = 0; i < _data.Tickets.Count; i++)
                    if (string.Equals(_data.Tickets[i].Category, old.Category, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(_data.Tickets[i].SubCategory, old.Name, StringComparison.OrdinalIgnoreCase))
                        _data.Tickets[i] = _data.Tickets[i] with { SubCategory = item.Name };
            Save();
            return "Catalogue item saved.";
        }
    }

    // Tickets already raised from it keep the text.
    public string DeleteServiceItem(Guid id)
    {
        lock (_sync)
        {
            if (_data.ServiceItems.RemoveAll(x => x.Id == id) == 0) return "Catalogue item was not found.";
            Save();
            return "Catalogue item deleted.";
        }
    }

    private (ServiceItem? Item, string? Error) BuildServiceItem(Guid id, string? category, string? name, string? defaultPriority)
    {
        var validCategory = _data.Categories.FirstOrDefault(x => string.Equals(x, category?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (validCategory is null) return (null, "Select a valid category.");
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return (null, "Enter a name for the catalogue item.");
        if (name.Length > MaxServiceItemNameLength) return (null, $"The name is too long ({MaxServiceItemNameLength} characters at most).");
        if (_data.ServiceItems.Any(x => x.Id != id && string.Equals(x.Category, validCategory, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return (null, $"{validCategory} already has an item with that name.");
        var priority = string.Empty;
        if (!string.IsNullOrWhiteSpace(defaultPriority))
        {
            priority = _data.Priorities.FirstOrDefault(x => string.Equals(x, defaultPriority.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            if (priority.Length == 0) return (null, "Select a valid priority.");
        }
        return (new ServiceItem(id, validCategory, name, priority), null);
    }
}
