namespace EduHelpdesk.Models;

// The DfE access control register (merged from EduInventory): who has access to which systems and physical areas, who
// approved it, when it was last reviewed, and when and why it was taken away. People are the helpdesk's own People
// directory (UserRecord); the rules are in Services/AccessRules.cs.

public static class AccessKinds
{
    public const string System = "System";
    public const string Physical = "Physical";
    public static readonly string[] All = [System, Physical];
    public static string Find(string? value) => string.Equals(value?.Trim(), Physical, StringComparison.OrdinalIgnoreCase) ? Physical : System;
    public static string Label(string kind) => kind == Physical ? "Physical area or key" : "System";
}

// Something access is granted to: a system (the MIS, Microsoft 365, finance, the firewall's admin console) or a physical
// area or key (the server room, a master key, the alarm code).
public record AccessResource(Guid Id, string Name, DateTime CreatedAt)
{
    public string Kind { get; init; } = AccessKinds.System;
    // From Settings → System and area categories.
    public string Category { get; init; } = "";
    // Who signs off access to it. The cyber standard wants privileged changes approved by SLT or a trustee.
    public string Owner { get; init; } = "";
    // Cyber standard: MFA must be on for staff accounts with access to cloud services or remote access, and for every IT
    // administrative account. Ticking this makes every grant to this system need MFA, not just privileged ones.
    public bool MfaRequired { get; init; }
    public bool HoldsPersonalData { get; init; }
    // The contract on the contracts register it is bought under, if any.
    public Guid? ContractId { get; init; }
    public string Notes { get; init; } = "";
    // A retired system keeps its history but can't be granted any more.
    public bool IsRetired { get; init; }
    public bool IsSystem => Kind == AccessKinds.System;
}

public static class MfaStates
{
    public const string Enabled = "Enabled";
    public const string NotEnabled = "Not enabled";
    // The cyber standard: "Where MFA is not available, a more complex password should be used".
    public const string NotAvailable = "Not available (complex password)";
    public const string NotApplicable = "Not applicable";
    public static readonly string[] All = [Enabled, NotEnabled, NotAvailable, NotApplicable];
    public static string Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? NotEnabled;
}

// One person's access to one system or area. A grant is never deleted when access ends: it is revoked with a date and
// a reason, so the register shows who had access to what and when - the point of keeping it.
public record AccessGrant(Guid Id, Guid PersonId, Guid ResourceId, DateTime CreatedAt)
{
    public string AccessLevel { get; init; } = "";
    // A username for a system; a key or fob number for a physical grant.
    public string Identifier { get; init; } = "";
    public bool Privileged { get; init; }
    public string Mfa { get; init; } = MfaStates.NotApplicable;
    public DateOnly? GrantedOn { get; init; }
    public string GrantedBy { get; init; } = "";
    // Cyber standard: "a member of SLT or a trustee approves any changes to access levels or privileges before IT support
    // can action the change." Expected for privileged access.
    public string ApprovedBy { get; init; } = "";
    public DateOnly? ApprovedOn { get; init; }
    public DateOnly? LastReviewedOn { get; init; }
    public string LastReviewedBy { get; init; } = "";
    public DateOnly? RevokedOn { get; init; }
    public string RevokedBy { get; init; } = "";
    // From Settings → Reasons for removing access.
    public string RevokeReason { get; init; } = "";
    public string Notes { get; init; } = "";
    // Set when an onboarding task recorded it, so unticking the task can take it back.
    public int? OnboardingTicket { get; init; }

    public bool IsActive(DateOnly today) => RevokedOn is null || RevokedOn > today;
}

// A completed access review. The cyber standard asks for accounts to be reviewed "with your business professionals or
// the finance team every term"; this is the evidence it happened.
public record AccessReview(Guid Id, DateOnly ReviewedOn, DateTime CreatedAt)
{
    public string ReviewedBy { get; init; } = "";
    public string ReviewedWith { get; init; } = "";
    public string Scope { get; init; } = "";
    public int Confirmed { get; init; }
    public int Revoked { get; init; }
    public string Notes { get; init; } = "";
}

// Starting values for the register's Settings lists.
public static class AccessDefaults
{
    public const string Staff = "Staff";
    public static readonly string[] PersonTypes = [Staff, "Pupil", "Governor or trustee", "Contractor", "Volunteer", "Service account"];
    public static readonly string[] Categories = ["MIS", "Cloud service", "Finance", "Network and infrastructure", "Safeguarding", "Curriculum", "CCTV and security", "Building", "Room", "Key", "Fob or card", "Alarm code"];
    public const string Leaver = "Leaver";
    public const string RemovedAtReview = "Removed at review";
    public static readonly string[] RevokeReasons = [Leaver, "Changed role", "No longer required", "Returned", "Lost or stolen", RemovedAtReview];
    // Cyber standard: review accounts every term - about four months.
    public const int ReviewDays = 120;
}
