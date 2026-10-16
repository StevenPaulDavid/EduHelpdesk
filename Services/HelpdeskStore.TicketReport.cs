using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// What the ticket report PDF is made from, gathered in one go under the lock so the report is one consistent moment of
// the ticket. TicketReportPdf does the drawing; it never reaches back into the store.
public sealed partial class HelpdeskStore
{
    public TicketReportInput? TicketReportFor(int number, bool includeInternal, string? preparedBy)
    {
        lock (_sync)
        {
            var ticket = _data.Tickets.FirstOrDefault(x => x.Number == number);
            if (ticket is null) return null;
            var values = GetTicketAttributeValues(number);
            // Every custom field the ticket has an answer for, and the empty ones its category asks for.
            var attributes = TicketAttributeDefinitions
                .Where(x => values.ContainsKey(x.Id) || x.AppliesTo(ticket.Category))
                .Select(x => (x.Name, Value: values.TryGetValue(x.Id, out var value) ? value : ""))
                .ToList();
            // The copy without internal notes is the one that leaves the IT team, so files kept from the requester stay out too.
            var everyFile = GetTicketAttachments(number).OrderBy(x => x.UploadedAt).ToList();
            var attachments = includeInternal ? everyFile : everyFile.Where(x => x.VisibleToRequester || x.FromRequester).ToList();
            var files = attachments.Select(x => (x.Id, Path: AttachmentFile(x.Id))).Where(x => File.Exists(x.Path)).ToDictionary(x => x.Id, x => x.Path);
            return new TicketReportInput(
                ticket,
                _data.Branding.BrandName,
                _data.Branding.PrimaryColor,
                LogoFile,
                _data.Users.FirstOrDefault(x => x.Id == ticket.RequesterId),
                ticket.TechnicianId is { } tech ? _data.Technicians.FirstOrDefault(x => x.Id == tech)?.Name : null,
                ticket.SlaId is { } sla ? _data.Slas.FirstOrDefault(x => x.Id == sla) : null,
                attributes,
                _data.Assets.Where(x => ticket.AssetIds.Contains(x.Id)).ToList(),
                GetTicketParts(number),
                GetTicketRelations(number),
                attachments,
                everyFile.Count - attachments.Count,
                files,
                includeInternal,
                preparedBy,
                DateTime.Now);
        }
    }
}

public sealed record TicketReportInput(
    TicketRecord Ticket,
    string OrganisationName,
    string PrimaryColor,
    string? LogoPath,
    UserRecord? Requester,
    string? TechnicianName,
    SlaDefinition? Sla,
    IReadOnlyList<(string Name, string Value)> Attributes,
    IReadOnlyList<AssetRecord> Assets,
    IReadOnlyList<(PartRecord Part, int Quantity)> Parts,
    IReadOnlyList<HelpdeskStore.TicketRelation> Relations,
    IReadOnlyList<TicketAttachment> Attachments,
    int AttachmentsLeftOut,
    IReadOnlyDictionary<Guid, string> FilePaths,
    bool IncludeInternal,
    string? PreparedBy,
    DateTime PreparedAt);
