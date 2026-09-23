using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class ReportsModel(HelpdeskStore store) : PageModel
{
    // How many rows each long table shows. Virtual so the print view can show every row - a report handed to someone
    // should not stop at 100 with no way to see the rest.
    public virtual int RowLimit => 100;
    public static readonly int[] WarrantyWindows = [30, 60, 90, 180, 365];
    public static readonly (string Key, string Label, int? Months)[] Periods = [("all", "All time", null), ("12m", "Last 12 months", 12), ("6m", "Last 6 months", 6), ("3m", "Last 3 months", 3)];

    // -1 means "use the review window from Settings".
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true, Name = "warranty")] public int WarrantyDays { get; set; } = -1;
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true, Name = "period")] public string? Period { get; set; }

    public sealed record CountRow(string Name, int Count);
    public sealed record AgeRow(string Type, int Count, int WithPurchaseDate, double? AverageYears, double? OldestYears, int DueOrOverdue);
    public sealed record ReplacementRow(AssetRecord Asset, DateOnly Due, double? AgeYears);
    public sealed record WarrantyRow(AssetRecord Asset, DateOnly Ends);
    public sealed record ProblemRow(AssetRecord Asset, int Tickets, int Open, DateTime? LastTicket);

    public DateOnly Today { get; } = AssetInsights.Today;
    public int TotalAssets { get; private set; }
    public IReadOnlyList<(string Title, IReadOnlyList<CountRow> Rows)> CountTables { get; private set; } = [];
    public IReadOnlyList<AssetInsights.ReviewItem> Review { get; private set; } = [];
    public int ReviewWindowDays { get; private set; }
    public IReadOnlyList<AgeRow> Age { get; private set; } = [];
    public int MissingPurchaseDate { get; private set; }
    public double? FleetAverageYears { get; private set; }
    public int ReplacementCount { get; private set; }
    public IReadOnlyList<ReplacementRow> Replacements { get; private set; } = [];
    public int EffectiveWarrantyDays { get; private set; }
    public IReadOnlyList<int> WarrantyOptions { get; private set; } = WarrantyWindows;
    public int ExpiringCount { get; private set; }
    public IReadOnlyList<WarrantyRow> Expiring { get; private set; } = [];
    public int ExpiredCount { get; private set; }
    public IReadOnlyList<WarrantyRow> Expired { get; private set; } = [];
    public string PeriodKey { get; private set; } = "all";
    public IReadOnlyList<ProblemRow> Problems { get; private set; } = [];

    public void OnGet()
    {
        var assets = store.Assets;
        var lifespans = store.AssetTypeLifespans;
        var today = Today;
        TotalAssets = assets.Count;
        ReviewWindowDays = store.AssetReviewDays;

        // Counts breakdown
        static IReadOnlyList<CountRow> Count(IEnumerable<AssetRecord> items, Func<AssetRecord, string> key) =>
            items.GroupBy(x => string.IsNullOrWhiteSpace(key(x)) ? "(none)" : key(x), StringComparer.OrdinalIgnoreCase)
                .Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        CountTables =
        [
            ("By type", Count(assets, x => x.Type)),
            ("By make", Count(assets, x => x.Make)),
            ("By location", Count(assets, x => x.Location)),
            ("By status", Count(assets, x => x.Status))
        ];

        // Review list (same rules as the overview)
        Review = AssetInsights.ReviewItems(assets, lifespans, ReviewWindowDays, today);

        // Fleet age and refresh
        var dated = assets.Where(x => x.PurchaseDate.HasValue).ToList();
        MissingPurchaseDate = assets.Count - dated.Count;
        FleetAverageYears = dated.Count == 0 ? null : dated.Average(x => AssetInsights.AgeYears(x, today) ?? 0);
        Age = assets.GroupBy(x => string.IsNullOrWhiteSpace(x.Type) ? "(none)" : x.Type, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ages = g.Select(x => AssetInsights.AgeYears(x, today)).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                var due = g.Count(x => AssetInsights.ReplacementDate(x, lifespans) is { } d && d <= today);
                return new AgeRow(g.Key, g.Count(), ages.Count, ages.Count == 0 ? null : ages.Average(), ages.Count == 0 ? null : ages.Max(), due);
            })
            .OrderByDescending(x => x.DueOrOverdue).ThenByDescending(x => x.AverageYears ?? -1).ThenBy(x => x.Type, StringComparer.OrdinalIgnoreCase).ToList();
        var planningHorizon = today.AddYears(1);
        var replacements = assets
            .Select(x => (Asset: x, Due: AssetInsights.ReplacementDate(x, lifespans)))
            .Where(x => x.Due.HasValue && x.Due.Value <= planningHorizon)
            .OrderBy(x => x.Due).ThenBy(x => x.Asset.AssetTag, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReplacementCount = replacements.Count;
        Replacements = replacements.Take(RowLimit).Select(x => new ReplacementRow(x.Asset, x.Due!.Value, AssetInsights.AgeYears(x.Asset, today))).ToList();

        // Warranty
        EffectiveWarrantyDays = WarrantyDays >= 0 ? Math.Min(WarrantyDays, 3650) : ReviewWindowDays;
        WarrantyOptions = WarrantyWindows.Append(EffectiveWarrantyDays).Distinct().OrderBy(x => x).ToList();
        var horizon = today.AddDays(EffectiveWarrantyDays);
        var expiring = assets.Where(x => x.WarrantyEnd is { } w && w >= today && w <= horizon).OrderBy(x => x.WarrantyEnd).ThenBy(x => x.AssetTag, StringComparer.OrdinalIgnoreCase).ToList();
        var expired = assets.Where(x => x.WarrantyEnd is { } w && w < today).OrderByDescending(x => x.WarrantyEnd).ThenBy(x => x.AssetTag, StringComparer.OrdinalIgnoreCase).ToList();
        ExpiringCount = expiring.Count;
        Expiring = expiring.Take(RowLimit).Select(x => new WarrantyRow(x, x.WarrantyEnd!.Value)).ToList();
        ExpiredCount = expired.Count;
        Expired = expired.Take(RowLimit).Select(x => new WarrantyRow(x, x.WarrantyEnd!.Value)).ToList();

        // Problem devices: assets with the most tickets in the chosen period
        var period = Periods.FirstOrDefault(x => x.Key == Period);
        if (period.Key is null) period = Periods[0];
        PeriodKey = period.Key;
        var since = period.Months is { } months ? DateTime.UtcNow.AddMonths(-months) : DateTime.MinValue;
        var byAsset = new Dictionary<Guid, List<TicketRecord>>();
        foreach (var ticket in store.Tickets.Where(x => x.CreatedAt >= since))
            foreach (var assetId in ticket.AssetIds.Distinct())
            {
                if (!byAsset.TryGetValue(assetId, out var list)) byAsset[assetId] = list = [];
                list.Add(ticket);
            }
        var assetsById = assets.ToDictionary(x => x.Id);
        Problems = byAsset
            .Where(x => assetsById.ContainsKey(x.Key))
            .Select(x => new ProblemRow(assetsById[x.Key], x.Value.Count, x.Value.Count(t => t.ClosedAt is null && !string.Equals(t.Status, "Closed", StringComparison.OrdinalIgnoreCase)), x.Value.Max(t => t.CreatedAt)))
            .OrderByDescending(x => x.Tickets).ThenByDescending(x => x.LastTicket).ThenBy(x => x.Asset.AssetTag, StringComparer.OrdinalIgnoreCase)
            .Take(15).ToList();
    }

    // One CSV per table rather than one per report: a report has several unrelated tables, and stacking them into one
    // file makes something no spreadsheet can pivot. OnGet is called explicitly because Razor Pages runs only the
    // matched handler, so the filters in the query string apply to the export exactly as they do on screen.
    public IActionResult OnGetExport(string? table)
    {
        OnGet();
        var (name, csv) = (table ?? "").ToLowerInvariant() switch
        {
            "review" => ("asset-review", Csv.Table(
                ["Asset tag", "Make", "Model", "Type", "Held by", "Reason", "Detail", "Overdue"],
                Review.SelectMany(item => item.Reasons.Select(reason => (item.Asset, reason))),
                x => [x.Asset.AssetTag, x.Asset.Make, x.Asset.Model, x.Asset.Type, HolderName(x.Asset) ?? "", x.reason.Kind, x.reason.Text, x.reason.Overdue ? "Yes" : "No"])),
            "counts" => ("asset-counts", Csv.Table(
                ["Breakdown", "Name", "Assets", "Share %"],
                CountTables.SelectMany(t => t.Rows.Select(r => (t.Title, r))),
                x => [x.Title, x.r.Name, x.r.Count.ToString(), TotalAssets == 0 ? "0" : (x.r.Count * 100.0 / TotalAssets).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)])),
            "age" => ("asset-age", Csv.Table(
                ["Type", "Assets", "With a purchase date", "Average age (years)", "Oldest (years)", "Due or overdue for replacement"],
                Age,
                x => [x.Type, x.Count.ToString(), x.WithPurchaseDate.ToString(), Number(x.AverageYears), Number(x.OldestYears), x.DueOrOverdue.ToString()])),
            "replacements" => ("asset-replacements", Csv.Table(
                ["Asset tag", "Type", "Make", "Model", "Age (years)", "Replacement date", "Overdue"],
                Replacements,
                x => [x.Asset.AssetTag, x.Asset.Type, x.Asset.Make, x.Asset.Model, Number(x.AgeYears), x.Due.ToString("yyyy-MM-dd"), x.Due < Today ? "Yes" : "No"])),
            "warranty" => ("asset-warranty", Csv.Table(
                ["State", "Asset tag", "Type", "Make", "Model", "Held by", "Warranty end"],
                Expiring.Select(x => ("Expiring", x)).Concat(Expired.Select(x => ("Expired", x))),
                x => [x.Item1, x.x.Asset.AssetTag, x.x.Asset.Type, x.x.Asset.Make, x.x.Asset.Model, HolderName(x.x.Asset) ?? "", x.x.Ends.ToString("yyyy-MM-dd")])),
            "problems" => ("asset-problems", Csv.Table(
                ["Asset tag", "Make", "Model", "Type", "Tickets", "Open now", "Most recent ticket"],
                Problems,
                x => [x.Asset.AssetTag, x.Asset.Make, x.Asset.Model, x.Asset.Type, x.Tickets.ToString(), x.Open.ToString(), x.LastTicket?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? ""])),
            _ => ("", "")
        };
        if (name.Length == 0) return NotFound();
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName(name, DateTime.Now));
    }

    private static string Number(double? value) => value?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public string? HolderName(AssetRecord asset) => asset.AssignedUserId is { } id ? store.Users.FirstOrDefault(x => x.Id == id)?.Name : null;

    public static string Years(double? value) => value.HasValue ? $"{value.Value:0.0} yrs" : "-";
    public static string DaysFrom(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;
        return days == 0 ? "today" : days > 0 ? $"in {days} day{(days == 1 ? "" : "s")}" : $"{-days} day{(days == -1 ? "" : "s")} ago";
    }
}
