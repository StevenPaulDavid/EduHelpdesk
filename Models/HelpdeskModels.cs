namespace EduHelpdesk.Models;

public record UserRecord(Guid Id, string Name, string Email, string Department, string Location);
public record TechnicianRecord(Guid Id, string Name, string Email, string Team);
public record AssetRecord(Guid Id, string AssetTag, string Type, string Model, string SerialNumber, string Location, Guid? AssignedUserId);
public record TicketRecord(
    int Number,
    string Title,
    string Description,
    Guid RequesterId,
    Guid? AssetId,
    Guid TechnicianId,
    string Priority,
    string Status,
    string Category,
    DateTime CreatedAt,
    DateTime? ClosedAt);
