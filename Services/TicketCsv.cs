using System.Globalization;
using System.Text;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

public static class TicketCsv
{
    public static string FileName(DateTime now) => $"tickets-{now:yyyyMMdd-HHmm}.csv";

    // One row per ticket: its own fields first, then one column per custom attribute that applies to any of the exported tickets.
    // Dates are written in local time as yyyy-MM-dd HH:mm.
    public static string Build(
        IEnumerable<TicketRecord> tickets,
        IReadOnlyList<UserRecord> users,
        IReadOnlyList<TechnicianRecord> technicians,
        IReadOnlyList<AssetRecord> assets,
        IReadOnlyList<SlaDefinition> slas,
        IReadOnlyList<TicketAttributeDefinition> attributeDefinitions,
        IReadOnlyList<TicketAttributeValue> attributeValues)
    {
        var rows = tickets.ToList();
        var userById = users.ToDictionary(x => x.Id);
        var technicianNames = technicians.ToDictionary(x => x.Id, x => x.Name);
        var assetTags = assets.ToDictionary(x => x.Id, x => x.AssetTag);
        var slaNames = slas.ToDictionary(x => x.Id, x => x.Name);
        var valuesByTicket = attributeValues.GroupBy(x => x.TicketNumber).ToDictionary(g => g.Key, g => g.ToDictionary(v => v.AttributeDefinitionId, v => v.Value));
        var attributes = attributeDefinitions.Where(d => rows.Any(t => d.AppliesTo(t.Category))).ToList();

        var headers = new List<string>
        {
            "Ticket number", "Title", "Description", "Type", "Status", "Priority", "Category", "Location", "Requester", "Requester email", "Department",
            "Technician", "Team", "Assets", "SLA", "Created", "Due", "Closed", "Last updated", "Comments"
        };
        // Two attributes can share a name (they apply to different categories); keep the columns distinct.
        var attributeHeaders = new List<string>();
        foreach (var attribute in attributes)
        {
            var name = attribute.Name;
            var header = name;
            for (var n = 2; headers.Contains(header, StringComparer.OrdinalIgnoreCase) || attributeHeaders.Contains(header, StringComparer.OrdinalIgnoreCase); n++) header = $"{name} ({n})";
            attributeHeaders.Add(header);
        }

        var csv = new StringBuilder();
        AppendRow(csv, headers.Concat(attributeHeaders));
        foreach (var ticket in rows)
        {
            userById.TryGetValue(ticket.RequesterId, out var requester);
            var values = valuesByTicket.GetValueOrDefault(ticket.Number);
            var cells = new List<string>
            {
                ticket.Number.ToString(CultureInfo.InvariantCulture), ticket.Title, ticket.Description, ticket.Type, ticket.Status, ticket.Priority, ticket.Category, ticket.Location ?? "",
                requester?.Name ?? "", requester?.Email ?? "", requester?.Department ?? "",
                ticket.TechnicianId is { } technician ? technicianNames.GetValueOrDefault(technician, "") : "",
                ticket.TeamName ?? "",
                string.Join("; ", ticket.AssetIds.Select(id => assetTags.GetValueOrDefault(id)).Where(x => !string.IsNullOrEmpty(x))),
                ticket.SlaId is { } sla ? slaNames.GetValueOrDefault(sla, "") : "",
                Stamp(ticket.CreatedAt), Stamp(ticket.DueDate), Stamp(ticket.ClosedAt), Stamp(ticket.LastModifiedAt),
                ticket.Comments.Count.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var attribute in attributes)
                cells.Add(attribute.AppliesTo(ticket.Category) && values is not null && values.TryGetValue(attribute.Id, out var value) ? value : "");
            AppendRow(csv, cells);
        }
        return csv.ToString();
    }

    private static string Stamp(DateTime? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

    private static void AppendRow(StringBuilder csv, IEnumerable<string> cells) =>
        csv.Append(string.Join(",", cells.Select(AssetCsv.Escape))).Append("\r\n");
}
