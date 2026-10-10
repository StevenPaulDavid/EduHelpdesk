using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// What is wrong with an access grant, merged from EduInventory's Compliance class. Each problem names the requirement
// it fails, most serious first, so the register, the person's page and the review all agree.
public static class AccessRules
{
    // Tone is the badge class: red (badge-warning) for something to fix now, amber (badge-soon) for something due.
    public sealed record Issue(string Kind, string Text, string Tone);

    public static DateOnly? LastReviewed(AccessGrant grant) => grant.LastReviewedOn ?? grant.GrantedOn;

    // Cyber standard: MFA must be enabled for accounts with access to cloud services or remote access, and for all IT
    // administrative accounts. Means nothing for a key.
    public static bool NeedsMfa(AccessGrant grant, AccessResource? resource) =>
        resource?.IsSystem == true && (resource.MfaRequired || grant.Privileged);

    public static List<Issue> Issues(AccessGrant grant, UserRecord? person, AccessResource? resource, DateOnly today, int reviewDays)
    {
        var issues = new List<Issue>();
        if (!grant.IsActive(today)) return issues;
        if (person is { IsActive: false }) issues.Add(new("Leaver", "Leaver still has access", "badge-warning"));
        if (resource?.IsRetired == true) issues.Add(new("Retired", "System retired but access remains", "badge-warning"));
        if (grant.Privileged && grant.ApprovedBy.Length == 0) issues.Add(new("Approval", "Privileged access not approved", "badge-warning"));
        if (NeedsMfa(grant, resource) && grant.Mfa is not (MfaStates.Enabled or MfaStates.NotAvailable)) issues.Add(new("Mfa", "MFA not enabled", "badge-warning"));
        if (LastReviewed(grant) is not { } reviewed || reviewed.AddDays(reviewDays) < today) issues.Add(new("Review", "Review overdue", "badge-soon"));
        return issues;
    }

    // The review the standard asks for each term is due once the last one is older than the review interval.
    public static DateOnly? NextReviewDue(IEnumerable<AccessReview> reviews, int reviewDays) =>
        reviews.Select(x => (DateOnly?)x.ReviewedOn).Max() is { } last ? last.AddDays(reviewDays) : null;

    public static string PersonType(UserRecord person) => string.IsNullOrWhiteSpace(person.PersonType) ? AccessDefaults.Staff : person.PersonType;

    public static DateOnly? LeaveDate(UserRecord person) => person.LeftAt is { } left ? DateOnly.FromDateTime(left.ToLocalTime()) : null;
}
