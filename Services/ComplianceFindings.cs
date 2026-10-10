using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// One thing on one record that needs someone to act, across the three DfE registers (merged from EduInventory's
// Findings). The compliance summary lists them, the Overview counts them, the reminders post them to the bell, and each
// links to the page that fixes it.
public sealed record ComplianceFinding(string Register, ComplianceFinding.Levels Level, string Issue, string Subject, string Detail, string Page, object Route, string Key)
{
    public enum Levels { Info = 0, Warning = 1, Danger = 2 }
    public string Tone => Level switch { Levels.Danger => "badge-warning", Levels.Warning => "badge-soon", _ => "" };
}

public static class ComplianceFindings
{
    public const string Assets = "Asset register";
    public const string Contracts = "Contracts register";
    public const string Access = "Access control register";

    private static ComplianceFinding.Levels Level(string tone) => tone switch
    {
        "badge-warning" => ComplianceFinding.Levels.Danger,
        "badge-soon" => ComplianceFinding.Levels.Warning,
        _ => ComplianceFinding.Levels.Info
    };
    private static string Day(DateOnly? value) => value is { } d ? AssetInsights.Format(d) : "not recorded";

    // Only the registers asked for: the caller passes what this person can see. Most serious first.
    public static List<ComplianceFinding> For(HelpdeskStore store, DateOnly today, bool assets = true, bool contracts = true, bool access = true)
    {
        var findings = new List<ComplianceFinding>();
        if (assets)
        {
            var checks = store.AssetCheckSettings;
            foreach (var asset in store.Assets.Where(x => !HelpdeskStore.IsDisposed(x)))
            {
                var subject = $"{asset.AssetTag} · {asset.Make} {asset.Model}".Trim();
                var route = new { id = asset.Id };
                var check = AssetChecks.CheckState(asset, today, checks.DueSoonDays);
                if (check is AssetChecks.State.Overdue or AssetChecks.State.Soon)
                    findings.Add(new(Assets, Level(AssetChecks.Tone(check)), AssetChecks.CheckLabel(check), subject, $"Next check {Day(asset.NextCheckDate)}", "/Asset", route, $"asset-check:{asset.Id}:{asset.NextCheckDate}"));
                var support = AssetChecks.SupportState(asset, today, checks.SupportWarningDays);
                if (support is AssetChecks.State.Overdue or AssetChecks.State.Soon)
                    findings.Add(new(Assets, Level(AssetChecks.Tone(support)), AssetChecks.SupportLabel(asset, support), subject, $"{(string.IsNullOrEmpty(asset.Ownership) ? "Support ends" : "Lease ends")} {Day(asset.EndOfSupport)}", "/Asset", route, $"asset-support:{asset.Id}:{asset.EndOfSupport}"));
                // DfE: a leased asset's details belong in the contracts register.
                if (asset.Ownership == AssetOwnership.Leased && asset.ContractId is null)
                    findings.Add(new(Assets, ComplianceFinding.Levels.Warning, "Lease not on the contracts register", subject, "Link it to its lease contract (Purchase & warranty tab)", "/Asset", route, $"asset-lease:{asset.Id}"));
            }
        }
        if (contracts)
        {
            foreach (var contract in store.Contracts)
            {
                var route = new { id = contract.Id };
                foreach (var flag in ContractRules.Flags(contract, today))
                    findings.Add(new(Contracts, Level(flag.Tone), flag.Text, contract.Name, flag.Kind switch
                    {
                        "Renewal" => $"Renews {Day(contract.NextRenewalDate)}{(contract.RenewalType == ContractRenewalTypes.Automatic ? " automatically" : "")}",
                        "Notice" => $"Give notice by {Day(ContractRules.NoticeDate(contract))}",
                        "End" => $"Ends {Day(contract.EndDate)}",
                        _ => "Academy Trust Handbook: report it before it starts or renews"
                    }, "/Contract", route, $"contract-{flag.Kind.ToLowerInvariant()}:{contract.Id}:{flag.Date}"));
                if (ContractRules.IsLive(contract) && string.Equals(contract.Status, "Expired but still using the service", StringComparison.OrdinalIgnoreCase))
                    findings.Add(new(Contracts, ComplianceFinding.Levels.Warning, "Expired but still in use", contract.Name, "Renew, replace or stop using it", "/Contract", route, $"contract-expired:{contract.Id}"));
            }
        }
        if (access)
        {
            var people = store.Users.ToDictionary(x => x.Id);
            var resources = store.AccessResources.ToDictionary(x => x.Id);
            foreach (var grant in store.AccessGrants.Where(x => x.IsActive(today)))
            {
                people.TryGetValue(grant.PersonId, out var person);
                resources.TryGetValue(grant.ResourceId, out var resource);
                foreach (var issue in AccessRules.Issues(grant, person, resource, today, store.AccessReviewDays))
                    findings.Add(new(Access, Level(issue.Tone), issue.Text, $"{person?.Name ?? "Unknown"} – {resource?.Name ?? "Unknown"}",
                        issue.Kind == "Review" ? $"Last reviewed {Day(AccessRules.LastReviewed(grant))}" : grant.AccessLevel,
                        "/Access/Grant", new { id = grant.Id }, $"access-{issue.Kind.ToLowerInvariant()}:{grant.Id}"));
            }
            // The termly review itself, once anyone has access to review.
            if (store.AccessGrants.Any(x => x.IsActive(today)) && AccessRules.NextReviewDue(store.AccessReviews, store.AccessReviewDays) is var due && (due is null || due < today))
                findings.Add(new(Access, ComplianceFinding.Levels.Danger, "Termly access review due", "Access control register",
                    due is null ? "No review recorded yet" : $"Was due {Day(due)}", "/Access/Review", new { }, $"access-review:{due}"));
        }
        return findings.OrderByDescending(x => x.Level).ThenBy(x => x.Register).ThenBy(x => x.Issue).ThenBy(x => x.Subject, NaturalComparer.Instance).ToList();
    }
}
