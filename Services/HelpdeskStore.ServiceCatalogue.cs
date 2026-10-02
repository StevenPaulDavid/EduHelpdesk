using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The service catalogue: common problems and requests, filed under a ticket category, that staff pick in the portal
// instead of typing a title. Anything not listed is still typed by hand - the catalogue never limits what can be reported.
public sealed partial class HelpdeskStore
{
    public const int MaxServiceItemNameLength = 120;
    public const int MaxHelperLineLength = 120;

    // One button in the staff portal: a catalogue item or a template ticked "Show in portal". Key is the item's name
    // or the template's id, whichever Kind says it is.
    public sealed record PortalChoice(string Kind, string Key, string Label, string HelperLine, string Category, int Order = 0)
    {
        public const string ItemKind = "item";
        public const string TemplateKind = "template";
    }

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

    // ---- What the staff portal offers ----

    // Every button staff can press, in one category or all of them: shown catalogue items and shown templates mixed
    // together, because to staff they are the same thing. A template whose category was deleted has nowhere to sit.
    // In the order set in Settings; buttons never ordered by hand tie, and so sort alphabetically.
    public IReadOnlyList<PortalChoice> PortalChoices(string? category = null)
    {
        lock (_sync)
        {
            var categories = _data.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool InScope(string value) => categories.Contains(value) && (category is null || string.Equals(value, category.Trim(), StringComparison.OrdinalIgnoreCase));
            return _data.ServiceItems.Where(x => x.ShowInPortal && InScope(x.Category))
                .Select(x => new PortalChoice(PortalChoice.ItemKind, x.Name, x.Name, x.HelperLine, x.Category, x.PortalOrder))
                .Concat(_data.TicketTemplates.Where(x => x.ShowInPortal && InScope(x.Category))
                    .Select(x => new PortalChoice(PortalChoice.TemplateKind, x.Id.ToString(), x.Name, x.HelperLine, x.Category, x.PortalOrder)))
                .OrderBy(x => x.Order)
                .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
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

    // Moves a portal button one place up (-1) or down inside its category. Items and templates share the numbering, so a
    // template can sit between two items. Every shown button in the category is renumbered, so a category that has only
    // ever been sorted alphabetically becomes an explicit order the first time one moves.
    public string? MovePortalChoice(string? category, string? kind, string? key, int direction)
    {
        lock (_sync)
        {
            var list = PortalChoices(category).ToList();
            var index = list.FindIndex(x => x.Kind == kind && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "That button was not found.";
            var other = index + (direction < 0 ? -1 : 1);
            if (other < 0 || other >= list.Count) return null;
            (list[index], list[other]) = (list[other], list[index]);
            for (var i = 0; i < list.Count; i++) SetPortalOrder(list[i], i);
            Save();
            return null;
        }
    }

    private void SetPortalOrder(PortalChoice choice, int order)
    {
        if (choice.Kind == PortalChoice.ItemKind)
        {
            var i = _data.ServiceItems.FindIndex(x => string.Equals(x.Category, choice.Category, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, choice.Key, StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && _data.ServiceItems[i].PortalOrder != order) _data.ServiceItems[i] = _data.ServiceItems[i] with { PortalOrder = order };
        }
        else if (Guid.TryParse(choice.Key, out var id))
        {
            var i = _data.TicketTemplates.FindIndex(x => x.Id == id);
            if (i >= 0 && _data.TicketTemplates[i].PortalOrder != order) _data.TicketTemplates[i] = _data.TicketTemplates[i] with { PortalOrder = order };
        }
    }

    // The place at the end of a category's portal buttons, for something new there (or newly shown, or moved in).
    private int NextPortalOrder(string category, Guid? exceptId)
    {
        var orders = _data.ServiceItems.Where(x => x.ShowInPortal && x.Id != exceptId && string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)).Select(x => x.PortalOrder)
            .Concat(_data.TicketTemplates.Where(x => x.ShowInPortal && x.Id != exceptId && string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)).Select(x => x.PortalOrder))
            .ToList();
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

    // A template staff may use: ticked for the portal and still sitting in a category that exists.
    public TicketTemplate? FindPortalTemplate(Guid id)
    {
        lock (_sync)
            return _data.TicketTemplates.FirstOrDefault(x => x.Id == id && x.ShowInPortal && _data.Categories.Contains(x.Category, StringComparer.OrdinalIgnoreCase));
    }

    // By name, for "Report it again": a ticket raised from a template keeps the template's name as its sub-category.
    public TicketTemplate? FindPortalTemplateByName(string? category, string? name)
    {
        lock (_sync)
            return _data.TicketTemplates.FirstOrDefault(x => x.ShowInPortal && string.Equals(x.Category, category?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
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

    // Shared with templates, which carry the same hint under their button.
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
