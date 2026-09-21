namespace EduHelpdesk.Models;

public record UserRecord(Guid Id, string Name, string Email, string Department, string Location);
public record TechnicianRecord(Guid Id, string Name, string Email, string Team);
public record SupplierRecord(Guid Id, string Name, string? ContactName, string? Email, string? Phone, string? AddressLine1, string? AddressLine2, string? City, string? StateRegion, string? PostalCode, string? Country, string? Website, string? Notes, DateTime CreatedAt);
public record PartRecord(Guid Id, string Name, string? Sku, string? Category, int QuantityOnHand, DateTime CreatedAt);
public record TicketPartAssignment(int TicketNumber, Guid PartId, int Quantity);
public record AssetRecord(Guid Id, string AssetTag, string Make, string Model, string Type, string SerialNumber, string Location, Guid? AssignedUserId, Guid? SupplierId = null)
{
    public List<AssetComment> Comments { get; init; } = [];
    public List<AssetActivity> History { get; init; } = [];
    public List<AssetAssignment> Assignments { get; init; } = [];
    public string Status { get; init; } = "In use";
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchasePrice { get; init; }
    public string PurchaseOrder { get; init; } = "";
    public DateOnly? WarrantyEnd { get; init; }
    // A replacement date typed on the asset. When blank, the date comes from the lifespan of the asset type (see AssetInsights).
    public DateOnly? ReplacementDate { get; init; }
    // Set while the asset is on loan to AssignedUserId.
    public DateOnly? LoanDueDate { get; init; }
}
// One period in which an asset was held by someone. StartedAt is null for holders recorded before assignments were tracked.
public record AssetAssignment(Guid? UserId, string UserName, DateTime? StartedAt, DateTime? EndedAt, DateOnly? DueBack);
public record AssetComment(string Text, DateTime CreatedAt);
public record AssetActivity(string Action, string Details, DateTime CreatedAt);
public record AssetAttributeDefinition(Guid Id, string Name, string FieldType = "single-line", string Choices = "")
{
    public List<string> AssetTypes { get; init; } = [];
    public bool AppliesTo(string? assetType) => AssetTypes.Count == 0 || AssetTypes.Contains(assetType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
public record AssetAttributeValue(Guid AssetId, Guid AttributeDefinitionId, string Value);
public record SlaDefinition(Guid Id, string Name, int Duration, string DurationUnit, string? Description = null)
{
    public List<string> Priorities { get; init; } = [];
    public List<string> Categories { get; init; } = [];
}
public record TicketAttributeDefinition(Guid Id, string Name, string FieldType = "single-line", string Choices = "")
{
    public List<string> Categories { get; init; } = [];
    public bool AppliesTo(string? category) => Categories.Count == 0 || Categories.Contains(category ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
public record TicketAttributeValue(int TicketNumber, Guid AttributeDefinitionId, string Value);
// The two kinds of ticket: something that is broken, and something that is being asked for. Used to separate them in lists and reports.
public static class TicketTypes
{
    public const string Incident = "Incident";
    public const string Request = "Request";
    public static readonly string[] All = [Incident, Request];
    // The matching type, or Incident for anything unknown or blank.
    public static string Normalize(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Incident;
}
// A saved starting point for a ticket. A blank SlaId means the SLA is worked out from the priority and category as usual.
public record TicketTemplate(Guid Id, string Name, string Type, string Title, string Description, string Category, string Priority, Guid? SlaId)
{
    // Default answers for ticket custom attributes, by attribute definition id.
    public Dictionary<Guid, string> AttributeValues { get; init; } = [];
}
public record TicketRecord(
    int Number,
    string Title,
    string Description,
    Guid RequesterId,
    IReadOnlyList<Guid> AssetIds,
    Guid? TechnicianId,
    string Priority,
    string Status,
    string Category,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    Guid? SlaId = null,
    DateTime? DueDate = null,
    bool DueDateOverridden = false,
    bool SlaOverridden = false,
    string? TeamName = null)
{
    public List<TicketComment> Comments { get; init; } = [];
    public List<TicketActivity> History { get; init; } = [];
    public string Type { get; init; } = TicketTypes.Incident;

    // The newest of creation, any history entry (field changes, parts, attributes, merges, asset links) and any comment.
    public DateTime LastModifiedAt => Comments.Select(x => x.CreatedAt).Concat(History.Select(x => x.CreatedAt)).Append(CreatedAt).Max();
}

// One line of the system audit. EntityType and EntityKey identify the record it concerns (for linking); they are null for lists and settings.
public record AuditEntry(DateTime At, string Area, string? EntityType, string? EntityKey, string Entity, string Action, string Details);

// An internal note is for technicians: it is left off the printed ticket.
public record TicketComment(string Text, DateTime CreatedAt, bool IsInternal = false);
public record TicketActivity(string Action, string Details, DateTime CreatedAt);
// A file uploaded to a ticket. The file itself is kept on disk under App_Data/attachments, named by Id.
public record TicketAttachment(Guid Id, int TicketNumber, string FileName, string ContentType, long Size, DateTime UploadedAt);
// Kind is "related" (either direction) or "follow-up" (TicketNumber is the original ticket, LinkedNumber the follow-up).
public record TicketLink(int TicketNumber, int LinkedNumber, string Kind);

public sealed class BrandingSettings
{
    public string BrandName { get; set; } = "EduHelpdesk";
    public string DashboardEyebrow { get; set; } = "EDUHELPDESK / TECHNICIAN WORKSPACE";
    public string DashboardTitle { get; set; } = "Keep every school device moving.";
    public string DashboardDescription { get; set; } = "Log jobs, link them to assets, and keep a complete repair history without exposing a staff-facing portal.";
    public string PrimaryColor { get; set; } = "#067A78";
    public string AccentColor { get; set; } = "#E8F0EF";
    public string BackgroundColor { get; set; } = "#F5F8F8";
    public bool DarkMode { get; set; }
}
