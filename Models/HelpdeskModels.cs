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
    Guid? TechnicianId,
    string Priority,
    string Status,
    string Category,
    DateTime CreatedAt,
    DateTime? ClosedAt)
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
