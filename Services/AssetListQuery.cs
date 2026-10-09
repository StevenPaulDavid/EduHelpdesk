using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Compares text so that numbers inside it sort by value: LT-2 comes before LT-10, ignoring case.
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[startX..i].TrimStart('0');
                var b = y[startY..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1;
                var byDigits = string.CompareOrdinal(a, b);
                if (byDigits != 0) return byDigits;
            }
            else
            {
                var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (byChar != 0) return byChar;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}

// The search, filters and sort order of the asset list. The list page, the bulk actions and the CSV export all use it, so
// "the assets matching the current filters" means the same thing everywhere.
public sealed class AssetListQuery
{
    // Filter values that mean "blank" or "nobody".
    public const string None = "(none)";
    public static readonly string[] SortColumns = ["tag", "model", "type", "serial", "holder", "building", "location", "status", "warranty", "purchased", "nextcheck", "support"];
    public static readonly string[] Flags = ["review", "loan", "overdue", "check", "support", "disposed"];

    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? Make { get; set; }
    public string? Building { get; set; }
    public string? Location { get; set; }
    // The DfE check windows behind the check and support flags and the review list; defaults if not set.
    public AssetCheckSettings? Checks { get; set; }
    // "none" for unassigned assets, or a user id.
    public string? Holder { get; set; }
    // review (needs review), loan (on loan) or overdue (loan overdue)
    public string? Flag { get; set; }
    public string Sort { get; set; } = "tag";
    public bool Descending { get; set; }

    public List<AssetRecord> Run(IEnumerable<AssetRecord> assets, IReadOnlyList<UserRecord> users, IReadOnlyDictionary<string, int> lifespanYears, int reviewDays, DateOnly today)
    {
        var names = users.ToDictionary(x => x.Id, x => x.Name);
        var all = assets as IReadOnlyCollection<AssetRecord> ?? assets.ToList();
        IEnumerable<AssetRecord> query = all;

        // Disposed assets stay in the register for audit but are not part of the working estate, so they are hidden
        // unless they are actually being asked for - either by the Disposed flag or by selecting that status directly.
        var wantsDisposed = string.Equals(Flag?.Trim(), "disposed", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(Status) && Matches(HelpdeskStore.DisposedStatus, Status));
        if (!wantsDisposed) query = query.Where(x => !HelpdeskStore.IsDisposed(x));

        if (!string.IsNullOrWhiteSpace(Status)) query = query.Where(x => Matches(x.Status, Status));
        if (!string.IsNullOrWhiteSpace(Type)) query = query.Where(x => Matches(x.Type, Type));
        if (!string.IsNullOrWhiteSpace(Make)) query = query.Where(x => Matches(x.Make, Make));
        if (!string.IsNullOrWhiteSpace(Building)) query = query.Where(x => Matches(x.Building, Building));
        if (!string.IsNullOrWhiteSpace(Location)) query = query.Where(x => Matches(x.Location, Location));
        var checks = Checks ?? new(AssetChecks.DefaultDueSoonDays, AssetChecks.DefaultSupportWarningDays, AssetChecks.DefaultIntervalMonths);
        if (!string.IsNullOrWhiteSpace(Holder))
        {
            if (string.Equals(Holder, "none", StringComparison.OrdinalIgnoreCase)) query = query.Where(x => x.AssignedUserId is null);
            else if (Guid.TryParse(Holder, out var holderId)) query = query.Where(x => x.AssignedUserId == holderId);
        }
        switch (Flag?.Trim().ToLowerInvariant())
        {
            case "review":
                var departed = users.Where(x => !x.IsActive).Select(x => x.Id).ToHashSet();
                var review = AssetInsights.ReviewItems(all, lifespanYears, reviewDays, today, departed, checks).Select(x => x.Asset.Id).ToHashSet();
                query = query.Where(x => review.Contains(x.Id));
                break;
            // DfE checks overdue or due soon - the list the termly walk-round works from.
            case "check":
                query = query.Where(x => AssetChecks.CheckState(x, today, checks.DueSoonDays) is AssetChecks.State.Overdue or AssetChecks.State.Soon);
                break;
            case "support":
                query = query.Where(x => AssetChecks.SupportState(x, today, checks.SupportWarningDays) is AssetChecks.State.Overdue or AssetChecks.State.Soon);
                break;
            case "loan":
                query = query.Where(x => x.AssignedUserId is not null && x.LoanDueDate is not null);
                break;
            case "overdue":
                query = query.Where(x => x.AssignedUserId is not null && x.LoanDueDate is { } due && due < today);
                break;
            case "disposed":
                query = query.Where(HelpdeskStore.IsDisposed);
                break;
        }
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(x =>
            {
                var holder = x.AssignedUserId is { } id && names.TryGetValue(id, out var name) ? name : string.Empty;
                var text = string.Join('\n', x.AssetTag, x.Make, x.Model, x.Type, x.SerialNumber, x.Building, x.Location, x.Status, x.PurchaseOrder, x.QuoteReference, x.OperatingSystem, x.DisposalCertificate, holder);
                return terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
        }
        return Order(query.ToList(), names);
    }

    private static bool Matches(string? value, string filter) =>
        filter == None ? string.IsNullOrWhiteSpace(value) : string.Equals(value?.Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private List<AssetRecord> Order(List<AssetRecord> list, IReadOnlyDictionary<Guid, string> names)
    {
        var natural = NaturalComparer.Instance;
        IOrderedEnumerable<AssetRecord> By(Func<AssetRecord, string?> key) => Descending ? list.OrderByDescending(key, natural) : list.OrderBy(key, natural);
        // Assets with no value for the column always go last, whichever way it is sorted.
        IOrderedEnumerable<AssetRecord> ByDate(Func<AssetRecord, DateOnly?> key) => Descending
            ? list.OrderBy(x => key(x) is null).ThenByDescending(key)
            : list.OrderBy(x => key(x) is null).ThenBy(key);
        IOrderedEnumerable<AssetRecord> ByHolder() => Descending
            ? list.OrderBy(x => x.AssignedUserId is null).ThenByDescending(x => HolderName(x, names), natural)
            : list.OrderBy(x => x.AssignedUserId is null).ThenBy(x => HolderName(x, names), natural);

        var ordered = Sort switch
        {
            "model" => By(x => $"{x.Make} {x.Model}"),
            "type" => By(x => x.Type),
            "serial" => By(x => x.SerialNumber),
            "holder" => ByHolder(),
            "building" => By(x => x.Building),
            "location" => By(x => x.Location),
            "status" => By(x => x.Status),
            "warranty" => ByDate(x => x.WarrantyEnd),
            "purchased" => ByDate(x => x.PurchaseDate),
            "nextcheck" => ByDate(x => x.NextCheckDate),
            "support" => ByDate(x => x.EndOfSupport),
            _ => By(x => x.AssetTag)
        };
        return ordered.ThenBy(x => x.AssetTag, natural).ToList();
    }

    private static string HolderName(AssetRecord asset, IReadOnlyDictionary<Guid, string> names) =>
        asset.AssignedUserId is { } id && names.TryGetValue(id, out var name) ? name : string.Empty;
}
