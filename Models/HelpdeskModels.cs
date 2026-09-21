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
}
public record AssetComment(string Text, DateTime CreatedAt);
public record AssetActivity(string Action, string Details, DateTime CreatedAt);
public record AssetAttributeDefinition(Guid Id, string Name, string? AssetType, string FieldType = "single-line", string Choices = "");
public record AssetAttributeValue(Guid AssetId, Guid AttributeDefinitionId, string Value);
public record SlaDefinition(Guid Id, string Name, int Duration, string DurationUnit, string? Description = null)
{
    public List<string> Priorities { get; init; } = [];
    public List<string> Categories { get; init; } = [];
}
public record TicketAttributeDefinition(Guid Id, string Name, string? Category, string FieldType = "single-line", string Choices = "");
public record TicketAttributeValue(int TicketNumber, Guid AttributeDefinitionId, string Value);
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
}

public record TicketComment(string Text, DateTime CreatedAt);
public record TicketActivity(string Action, string Details, DateTime CreatedAt);

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
