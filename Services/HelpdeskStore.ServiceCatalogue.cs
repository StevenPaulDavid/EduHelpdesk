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
    public sealed record PortalChoice(string Kind, string Key, string Label, string HelperLine, string Category)
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
    public IReadOnlyList<PortalChoice> PortalChoices(string? category = null)
    {
        lock (_sync)
        {
            var categories = _data.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool InScope(string value) => categories.Contains(value) && (category is null || string.Equals(value, category.Trim(), StringComparison.OrdinalIgnoreCase));
            return _data.ServiceItems.Where(x => x.ShowInPortal && InScope(x.Category))
                .Select(x => new PortalChoice(PortalChoice.ItemKind, x.Name, x.Name, x.HelperLine, x.Category))
                .Concat(_data.TicketTemplates.Where(x => x.ShowInPortal && InScope(x.Category))
                    .Select(x => new PortalChoice(PortalChoice.TemplateKind, x.Id.ToString(), x.Name, x.HelperLine, x.Category)))
                .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
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
        return (new ServiceItem(id, validCategory, name, priority, helper, showInPortal), null);
    }
}
