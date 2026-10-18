namespace EduHelpdesk.Services;

// When a value in one of the lists can't be deleted because something uses it - a category with tickets, a room with
// people in it - Settings offers to move everything on it to another value and then take it off (Pages/Settings/
// _MergePrompt). Moving is the rename (HelpdeskStore.Options) with the new name being one already on the list, so
// everything a rename reaches, a merge reaches; the value moved to keeps its own settings.
public sealed partial class HelpdeskStore
{
    // What uses a value, in words - "12 tickets", "1 recurring ticket" - empty when nothing does. kind is the list's name
    // in either form ("Categories" or "Category").
    public IReadOnlyList<string> OptionUsage(string kind, string value)
    {
        lock (_sync) return CanonicalOptionKind(kind) is { } name ? OptionUsageCore(name, value.Trim()) : [];
    }

    // What everything on a value can move to: the rest of its list, less what its tickets couldn't take - a category or
    // team on another desk would move tickets between desks, and Closed would close them without closing them.
    public IReadOnlyList<string> MergeTargets(string kind, string value)
    {
        lock (_sync) return CanonicalOptionKind(kind) is { } name ? MergeTargetsCore(name, value.Trim()) : [];
    }

    public (bool Ok, string Message) MergeOption(string kind, string value, string? into)
    {
        lock (_sync)
        {
            if (CanonicalOptionKind(kind) is not { } name) return (false, "That list was not found.");
            var list = IsTicketOptionKind(name) ? GetOptions(name) : GetManagedOptions(name);
            var from = list.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (from is null) return (false, $"{name} was not found.");
            var target = MergeTargetsCore(name, from).FirstOrDefault(x => string.Equals(x, into?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target is null) return (false, $"Choose another {name.ToLowerInvariant()} to move them to.");
            var usage = OptionUsageCore(name, from);
            var result = IsTicketOptionKind(name) ? RenameTicketOptionCore(name, from, target, merge: true) : RenameManagedOptionCore(name, from, target, merge: true);
            if (!result.EndsWith(" updated.", StringComparison.Ordinal)) return (false, result);
            return (true, usage.Count == 0 ? $"{from} taken off the list." : $"{Joined(usage)} moved to {target}, and {from} taken off the list.");
        }
    }

    // Whether a value can be moved away at all: not Closed, or Expired, which the helpdesk itself depends on.
    public static bool CanMergeAway(string kind, string value) =>
        CanonicalOptionKind(kind) is { } name && !(name == "Status" && IsBuiltInStatus(value)) && !IsProtectedContractStatus(name, value);

    // The list's name for one of its values, as a sentence says it: "category", "asset make".
    public static string OptionNoun(string kind) => (CanonicalOptionKind(kind) ?? kind).ToLowerInvariant();

    private static string? CanonicalOptionKind(string kind) => (kind ?? "").Trim() switch
    {
        "Categories" or "Category" => "Category",
        "Statuses" or "Status" => "Status",
        "Priorities" or "Priority" => "Priority",
        var other when IsManagedOptionKind(other) => NormalizeManagedOptionKind(other),
        _ => null
    };

    private List<string> MergeTargetsCore(string kind, string value)
    {
        var list = IsTicketOptionKind(kind) ? GetOptions(kind) : GetManagedOptions(kind);
        IEnumerable<string> others = list.Where(x => !string.Equals(x, value, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x));
        others = kind switch
        {
            "Category" => others.Where(x => DeskForCategoryCore(x).Id == DeskForCategoryCore(value).Id),
            "Team" => others.Where(x => DeskForTeamCore(x)?.Id == DeskForTeamCore(value)?.Id),
            // A shared status's tickets are on every desk, so only another shared one fits them all; a desk's own can
            // also go to another of that desk's.
            "Status" => others.Where(x => !IsBuiltInStatus(x) && (DeskForStatusCore(x) is not { } desk || desk.Id == DeskForStatusCore(value)?.Id)),
            _ => others
        };
        return others.ToList();
    }

    private List<string> OptionUsageCore(string kind, string value)
    {
        bool Is(string? x) => string.Equals(x?.Trim(), value, StringComparison.OrdinalIgnoreCase);
        var counts = new List<(int Count, string One, string Many)>();
        void Add(int count, string one, string many) { if (count > 0) counts.Add((count, one, many)); }
        switch (kind)
        {
            case "Category":
                Add(_data.Tickets.Count(x => Is(x.Category)), "ticket", "tickets");
                Add(_data.RecurringTickets.Count(x => Is(x.Category)), "recurring ticket", "recurring tickets");
                Add(_data.TicketTemplates.Count(x => Is(x.Category)), "ticket template", "ticket templates");
                Add(_data.ServiceItems.Count(x => Is(x.Category)), "service catalogue item", "service catalogue items");
                Add(_data.TicketAttributeDefinitions.Count(x => x.Categories.Any(Is)), "custom field", "custom fields");
                break;
            case "Status":
                Add(_data.Tickets.Count(x => Is(x.Status)), "ticket", "tickets");
                break;
            case "Priority":
                Add(_data.Tickets.Count(x => Is(x.Priority)), "ticket", "tickets");
                Add(_data.RecurringTickets.Count(x => Is(x.Priority)), "recurring ticket", "recurring tickets");
                break;
            case "Team":
                Add(_data.Technicians.Count(x => Is(x.Team)), "technician", "technicians");
                Add(_data.Tickets.Count(x => Is(x.TeamName)), "ticket", "tickets");
                Add(_data.RecurringTickets.Count(x => Is(x.TeamName)), "recurring ticket", "recurring tickets");
                break;
            case "Location":
                Add(_data.Users.Count(x => Is(x.Location)), "person", "people");
                Add(_data.Assets.Count(x => Is(x.Location)), "asset", "assets");
                Add(_data.Tickets.Count(x => Is(x.Location)), "ticket", "tickets");
                Add(_data.CleaningRounds.Count(x => x.Rooms.Any(Is)), "cleaning round", "cleaning rounds");
                Add(_data.RecurringTickets.Count(x => Is(x.Location)), "recurring ticket", "recurring tickets");
                break;
            case "Department":
                Add(_data.Users.Count(x => Is(x.Department)), "person", "people");
                break;
            case "Person type":
                Add(_data.Users.Count(x => Is(x.PersonType)), "person", "people");
                break;
            case "Asset make":
                Add(_data.Assets.Count(x => Is(x.Make)), "asset", "assets");
                Add(_data.AssetModelMakes.Values.Count(Is), "asset model", "asset models");
                break;
            case "Asset type":
                Add(_data.Assets.Count(x => Is(x.Type)), "asset", "assets");
                Add(_data.AssetAttributeDefinitions.Count(x => x.AssetTypes.Any(Is)), "custom field", "custom fields");
                Add(_data.Parts.Count(x => x.AssetTypes.Any(Is)), "part", "parts");
                Add(_data.RecurringTickets.Count(x => x.Checklist.Any(c => Is(c.AssetType))), "checklist", "checklists");
                break;
            case "Asset model": Add(_data.Assets.Count(x => Is(x.Model)), "asset", "assets"); break;
            case "Asset status": Add(_data.Assets.Count(x => Is(x.Status)), "asset", "assets"); break;
            case "Building": Add(_data.Assets.Count(x => Is(x.Building)), "asset", "assets"); break;
            case "Asset condition": Add(_data.Assets.Count(x => Is(x.Condition)), "asset", "assets"); break;
            case "Contract type": Add(_data.Contracts.Count(x => Is(x.ContractType)), "contract", "contracts"); break;
            case "Spend category": Add(_data.Contracts.Count(x => Is(x.SpendCategory)), "contract", "contracts"); break;
            case "Contract duration": Add(_data.Contracts.Count(x => Is(x.Duration)), "contract", "contracts"); break;
            case "Contract status": Add(_data.Contracts.Count(x => Is(x.Status)), "contract", "contracts"); break;
            case "Access category": Add(_data.AccessResources.Count(x => Is(x.Category)), "system or area", "systems and areas"); break;
            case "Revoke reason": Add(_data.AccessGrants.Count(x => Is(x.RevokeReason)), "access record", "access records"); break;
            case "Part category": Add(_data.Parts.Count(x => Is(x.Category)), "part", "parts"); break;
            case "Part location": Add(_data.Parts.Count(x => Is(x.Location)), "part", "parts"); break;
            case "Loan reason": Add(_data.KitLoans.Count(x => Is(x.Reason)), "loan", "loans"); break;
            case "Budget code": Add(_data.TicketCosts.Count(x => Is(x.BudgetCode)), "cost line", "cost lines"); break;
            case "Cleaner": Add(_data.CleaningRounds.Count(x => Is(x.Cleaner)), "cleaning round", "cleaning rounds"); break;
            case "Purchasing requirement": Add(_data.Projects.Count(x => x.PurchasingRequirements.Any(Is)), "project", "projects"); break;
        }
        return counts.Select(x => $"{x.Count} {(x.Count == 1 ? x.One : x.Many)}").ToList();
    }

    // "12 tickets, 1 template and 2 custom fields".
    public static string Joined(IReadOnlyList<string> parts) =>
        parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
}
