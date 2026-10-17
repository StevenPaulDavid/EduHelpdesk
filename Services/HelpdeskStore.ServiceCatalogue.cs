using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The service catalogue: common problems and requests, filed under a ticket category, that staff pick in the portal
// instead of typing a title. Anything not listed is still typed by hand - the catalogue never limits what can be reported.
public sealed partial class HelpdeskStore
{
    public const int MaxServiceItemNameLength = 120;
    public const int MaxHelperLineLength = 120;

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

    // The item a requester picked, if it really is in that category - the portal posts text, so it is checked again here.
    public ServiceItem? FindServiceItem(string? category, string? name)
    {
        lock (_sync)
            return _data.ServiceItems.FirstOrDefault(x => string.Equals(x.Category, category?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // ---- What the staff portal offers ----

    // Every button staff can press, in one category or all of them: the catalogue items shown in the portal, in the
    // order set in Settings. Items never ordered by hand tie, and so sort alphabetically.
    public IReadOnlyList<ServiceItem> PortalChoices(string? category = null)
    {
        lock (_sync)
        {
            var categories = _data.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool InScope(string value) => categories.Contains(value) && (category is null || string.Equals(value, category.Trim(), StringComparison.OrdinalIgnoreCase));
            return _data.ServiceItems.Where(x => x.ShowInPortal && InScope(x.Category))
                .OrderBy(x => x.PortalOrder)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    // ---- How the portal looks and is ordered ----

    // The look of a category's tile: what was chosen in Settings, or the built-in one for its name.
    public CategoryStyle StyleFor(string? category)
    {
        lock (_sync)
        {
            var fallback = PortalLook.DefaultFor(category ?? "");
            if (category is not null && _data.CategoryStyles.TryGetValue(category.Trim(), out var chosen))
                return new CategoryStyle(PortalLook.FindIcon(chosen.Icon)?.Key ?? fallback.Icon, PortalLook.FindColor(chosen.Color)?.Key ?? fallback.Color);
            return new CategoryStyle(fallback.Icon, fallback.Color);
        }
    }

    public string SetCategoryStyle(string? category, string? icon, string? color)
    {
        lock (_sync)
        {
            var name = _data.Categories.FirstOrDefault(x => string.Equals(x, category?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return "Select a valid category.";
            var chosenIcon = PortalLook.FindIcon(icon);
            if (chosenIcon is null) return "Choose one of the icons.";
            var chosenColor = PortalLook.FindColor(color);
            if (chosenColor is null) return "Choose one of the colours.";
            _data.CategoryStyles[name] = new CategoryStyle(chosenIcon.Key, chosenColor.Key);
            Save();
            return $"{name} tile saved.";
        }
    }

    // Moves a category one place up (direction -1) or down in the category list, which is the order of the tiles and of
    // every category dropdown. Null when there is nothing to say - including when it is already at that end.
    public string? MoveCategory(string? category, int direction)
    {
        lock (_sync)
        {
            var index = _data.Categories.FindIndex(x => string.Equals(x, category?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Category was not found.";
            var other = index + (direction < 0 ? -1 : 1);
            if (other < 0 || other >= _data.Categories.Count) return null;
            (_data.Categories[index], _data.Categories[other]) = (_data.Categories[other], _data.Categories[index]);
            Save();
            return null;
        }
    }

    // Moves a portal button one place up (-1) or down inside its category. Every shown button in the category is
    // renumbered, so a category that has only ever been sorted alphabetically becomes an explicit order the first time
    // one moves.
    public string? MovePortalChoice(Guid id, int direction)
    {
        lock (_sync)
        {
            if (_data.ServiceItems.FirstOrDefault(x => x.Id == id) is not { } item) return "That button was not found.";
            var list = PortalChoices(item.Category).ToList();
            var index = list.FindIndex(x => x.Id == id);
            if (index < 0) return "That button was not found.";
            var other = index + (direction < 0 ? -1 : 1);
            if (other < 0 || other >= list.Count) return null;
            (list[index], list[other]) = (list[other], list[index]);
            for (var i = 0; i < list.Count; i++)
            {
                var at = _data.ServiceItems.FindIndex(x => x.Id == list[i].Id);
                if (_data.ServiceItems[at].PortalOrder != i) _data.ServiceItems[at] = _data.ServiceItems[at] with { PortalOrder = i };
            }
            Save();
            return null;
        }
    }

    // The place at the end of a category's portal buttons, for something new there (or newly shown, or moved in).
    private int NextPortalOrder(string category, Guid? exceptId)
    {
        var orders = _data.ServiceItems.Where(x => x.ShowInPortal && x.Id != exceptId && string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)).Select(x => x.PortalOrder).ToList();
        return orders.Count == 0 ? 0 : orders.Max() + 1;
    }

    // The categories that have at least one button, in category order, with how many.
    public IReadOnlyList<(string Category, int Count)> PortalCategories()
    {
        lock (_sync)
        {
            var counts = PortalChoices().GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            return _data.Categories.Where(counts.ContainsKey).Select(x => (x, counts[x])).ToList();
        }
    }

    // ---- Managing the catalogue ----

    public string AddServiceItem(string? category, string? name, string? defaultPriority, string? helperLine = null, bool showInPortal = true)
    {
        lock (_sync)
        {
            var (item, error) = BuildServiceItem(Guid.NewGuid(), category, name, defaultPriority, helperLine, showInPortal);
            if (item is null) return error!;
            _data.ServiceItems.Add(item);
            Save();
            return "Catalogue item added.";
        }
    }

    public string UpdateServiceItem(Guid id, string? category, string? name, string? defaultPriority, string? helperLine = null, bool showInPortal = true)
    {
        lock (_sync)
        {
            var index = _data.ServiceItems.FindIndex(x => x.Id == id);
            if (index < 0) return "Catalogue item was not found.";
            var old = _data.ServiceItems[index];
            var (item, error) = BuildServiceItem(id, category, name, defaultPriority, helperLine, showInPortal);
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

    // Shared with desks, whose portal tiles carry the same kind of hint.
    internal static string? CheckHelperLine(string? helperLine, out string cleaned)
    {
        cleaned = helperLine?.Trim() ?? string.Empty;
        return cleaned.Length > MaxHelperLineLength ? $"The helper line is too long ({MaxHelperLineLength} characters at most)." : null;
    }

    private (ServiceItem? Item, string? Error) BuildServiceItem(Guid id, string? category, string? name, string? defaultPriority, string? helperLine, bool showInPortal)
    {
        var validCategory = _data.Categories.FirstOrDefault(x => string.Equals(x, category?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (validCategory is null) return (null, "Select a valid category.");
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return (null, "Enter a name for the catalogue item.");
        if (name.Length > MaxServiceItemNameLength) return (null, $"The name is too long ({MaxServiceItemNameLength} characters at most).");
        if (_data.ServiceItems.Any(x => x.Id != id && string.Equals(x.Category, validCategory, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return (null, $"{validCategory} already has an item with that name.");
        if (CheckHelperLine(helperLine, out var helper) is { } helperError) return (null, helperError);
        var priority = string.Empty;
        if (!string.IsNullOrWhiteSpace(defaultPriority))
        {
            priority = _data.Priorities.FirstOrDefault(x => string.Equals(x, defaultPriority.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            if (priority.Length == 0) return (null, "Select a valid priority.");
        }
        // Keeps its place while it stays shown in the same category; otherwise it joins the end of the buttons it is entering.
        var old = _data.ServiceItems.FirstOrDefault(x => x.Id == id);
        var order = old is not null && old.ShowInPortal && showInPortal && string.Equals(old.Category, validCategory, StringComparison.OrdinalIgnoreCase)
            ? old.PortalOrder
            : NextPortalOrder(validCategory, id);
        return (new ServiceItem(id, validCategory, name, priority, helper, showInPortal, order), null);
    }
}
