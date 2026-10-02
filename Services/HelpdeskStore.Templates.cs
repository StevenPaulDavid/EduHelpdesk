using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Ticket templates: saved starting points chosen when a ticket is logged.
public sealed partial class HelpdeskStore
{
    public IReadOnlyList<TicketTemplate> TicketTemplates
    {
        get { lock (_sync) return _data.TicketTemplates.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    public TicketTemplate? GetTicketTemplate(Guid id)
    {
        lock (_sync) return _data.TicketTemplates.FirstOrDefault(x => x.Id == id);
    }

    public string AddTicketTemplate(string? name, string? type, string? title, string? description, string? category, string? priority, Guid? slaId, IReadOnlyDictionary<Guid, string>? attributeValues, string? helperLine = null, bool showInPortal = false)
    {
        lock (_sync)
        {
            var (template, error) = BuildTemplate(Guid.NewGuid(), name, type, title, description, category, priority, slaId, attributeValues, helperLine, showInPortal);
            if (template is null) return error!;
            _data.TicketTemplates.Add(template);
            Save();
            return "Template added.";
        }
    }

    public string UpdateTicketTemplate(Guid id, string? name, string? type, string? title, string? description, string? category, string? priority, Guid? slaId, IReadOnlyDictionary<Guid, string>? attributeValues, string? helperLine = null, bool showInPortal = false)
    {
        lock (_sync)
        {
            var index = _data.TicketTemplates.FindIndex(x => x.Id == id);
            if (index < 0) return "Template was not found.";
            var (template, error) = BuildTemplate(id, name, type, title, description, category, priority, slaId, attributeValues, helperLine, showInPortal);
            if (template is null) return error!;
            _data.TicketTemplates[index] = template;
            Save();
            return "Template saved.";
        }
    }

    public string DeleteTicketTemplate(Guid id)
    {
        lock (_sync)
        {
            var removed = _data.TicketTemplates.RemoveAll(x => x.Id == id);
            if (removed == 0) return "Template was not found.";
            Save();
            return "Template deleted.";
        }
    }

    private (TicketTemplate? Template, string? Error) BuildTemplate(Guid id, string? name, string? type, string? title, string? description, string? category, string? priority, Guid? slaId, IReadOnlyDictionary<Guid, string>? attributeValues, string? helperLine, bool showInPortal)
    {
        if (CheckHelperLine(helperLine, out var helper) is { } helperError) return (null, helperError);
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return (null, "Enter a name for the template.");
        if (name.Length > 100) return (null, "The template name is too long (100 characters at most).");
        if (_data.TicketTemplates.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return (null, "A template with that name already exists.");
        var old = _data.TicketTemplates.FirstOrDefault(x => x.Id == id);
        var validCategory = _data.Categories.FirstOrDefault(x => string.Equals(x, category?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (validCategory is null) return (null, "Select a valid category.");
        var validPriority = _data.Priorities.FirstOrDefault(x => string.Equals(x, priority?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (validPriority is null) return (null, "Select a valid priority.");
        if (slaId.HasValue && !_data.Slas.Any(x => x.Id == slaId.Value)) return (null, "Select a valid SLA.");
        title = title?.Trim() ?? string.Empty;
        if (title.Length > 200) return (null, "The ticket title is too long (200 characters at most).");
        var values = (attributeValues ?? new Dictionary<Guid, string>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Value) && _data.TicketAttributeDefinitions.Any(d => d.Id == x.Key))
            .ToDictionary(x => x.Key, x => x.Value.Trim());
        return (new TicketTemplate(id, name, TicketTypes.Normalize(type), title, description?.Trim() ?? string.Empty, validCategory, validPriority, slaId)
        {
            AttributeValues = values, HelperLine = helper, ShowInPortal = showInPortal,
            // Keeps its place while it stays shown in the same category; otherwise it joins the end of the buttons it is entering.
            PortalOrder = old is not null && old.ShowInPortal && showInPortal && string.Equals(old.Category, validCategory, StringComparison.OrdinalIgnoreCase) ? old.PortalOrder : NextPortalOrder(validCategory, id)
        }, null);
    }
}
