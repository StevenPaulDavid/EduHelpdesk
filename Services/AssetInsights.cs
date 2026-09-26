using System.Globalization;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Date-based calculations over the asset register, shared by the overview review list and the reports.
public static class AssetInsights
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static string Format(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);

    // The date typed on the asset wins; otherwise the purchase date plus the lifespan set for the asset type.
    public static DateOnly? ReplacementDate(AssetRecord asset, IReadOnlyDictionary<string, int> lifespanYears)
    {
        if (asset.ReplacementDate is { } typed) return typed;
        if (asset.PurchaseDate is { } purchased && lifespanYears.TryGetValue(asset.Type, out var years) && years > 0) return purchased.AddYears(years);
        return null;
    }

    public static double? AgeYears(AssetRecord asset, DateOnly today) =>
        asset.PurchaseDate is { } purchased && purchased <= today ? (today.DayNumber - purchased.DayNumber) / 365.25 : null;

    public sealed record Reason(string Kind, string Text, DateOnly Date, bool Overdue);

    public sealed record ReviewItem(AssetRecord Asset, IReadOnlyList<Reason> Reasons)
    {
        public DateOnly SortDate => Reasons.Min(x => x.Date);
    }

    // Assets a technician should look at: warranty ending within the window (or already over), replacement due within the window
    // (or overdue), and loans past their due-back date. Most urgent first.
    // departedHolders: people marked inactive. An asset still recorded against one of them is flagged, so a leaver's
    // laptop doesn't sit on their name for ever (HelpdeskStore.DepartedUserIds).
    public static List<ReviewItem> ReviewItems(IEnumerable<AssetRecord> assets, IReadOnlyDictionary<string, int> lifespanYears, int windowDays, DateOnly today, IReadOnlySet<Guid>? departedHolders = null)
    {
        var horizon = today.AddDays(Math.Max(0, windowDays));
        var items = new List<ReviewItem>();
        foreach (var asset in assets)
        {
            // Nothing to review about kit that has left the estate. Skipping here covers the overview review list, the
            // asset report and its exports, the print view and the ?flag=review list filter in one go.
            if (HelpdeskStore.IsDisposed(asset)) continue;
            var reasons = new List<Reason>();
            if (asset.WarrantyEnd is { } warranty && warranty <= horizon)
                reasons.Add(new("Warranty", warranty < today ? $"Ended {Format(warranty)}" : warranty == today ? "Ends today" : $"Ends {Format(warranty)} ({Days(warranty, today)})", warranty, warranty < today));
            if (ReplacementDate(asset, lifespanYears) is { } replacement && replacement <= horizon)
                reasons.Add(new("Replacement", replacement < today ? $"Overdue since {Format(replacement)}" : replacement == today ? "Due today" : $"Due {Format(replacement)} ({Days(replacement, today)})", replacement, replacement < today));
            if (asset.AssignedUserId is not null && asset.LoanDueDate is { } due && due < today)
                reasons.Add(new("Loan", $"Overdue: due back {Format(due)} ({today.DayNumber - due.DayNumber} day{(today.DayNumber - due.DayNumber == 1 ? "" : "s")} late)", due, true));
            if (asset.AssignedUserId is { } holder && departedHolders?.Contains(holder) == true)
                reasons.Add(new("Leaver", "Held by someone who has left", today, true));
            if (reasons.Count > 0) items.Add(new ReviewItem(asset, reasons));
        }
        return items.OrderBy(x => x.SortDate).ThenBy(x => x.Asset.AssetTag, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Days(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;
        return days == 1 ? "tomorrow" : $"in {days} days";
    }
}
