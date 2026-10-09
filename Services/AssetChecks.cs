using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// How far ahead the DfE asset checks look (Settings → Inventory rules).
public sealed record AssetCheckSettings(int DueSoonDays, int SupportWarningDays, int IntervalMonths);

// The DfE digital technology asset register rules, merged from EduInventory's Compliance class: when an asset's next
// check is overdue or due soon, and when it has lost (or is about to lose) security updates. These feed the Assets to
// review list, the asset page badges and the asset list filters.
public static class AssetChecks
{
    public const int DefaultDueSoonDays = 30;
    // Six months: long enough to budget for and order a replacement before the kit becomes a security risk.
    public const int DefaultSupportWarningDays = 180;
    public const int DefaultIntervalMonths = 12;

    public enum State { None, Ok, Soon, Overdue }

    public static State CheckState(AssetRecord asset, DateOnly today, int dueSoonDays)
    {
        if (asset.NextCheckDate is not { } next) return State.None;
        if (next < today) return State.Overdue;
        return next <= today.AddDays(Math.Max(0, dueSoonDays)) ? State.Soon : State.Ok;
    }

    public static State SupportState(AssetRecord asset, DateOnly today, int warningDays)
    {
        if (asset.EndOfSupport is not { } end) return State.None;
        if (end < today) return State.Overdue;
        return end <= today.AddDays(Math.Max(0, warningDays)) ? State.Soon : State.Ok;
    }

    // Leased and loaned kit goes back to its owner, so its end date is the end of the lease rather than of support.
    public static string CheckLabel(State state) => state switch
    {
        State.Overdue => "Check overdue",
        State.Soon => "Check due soon",
        State.Ok => "Checked",
        _ => "No check scheduled"
    };
    public static string SupportLabel(AssetRecord asset, State state)
    {
        var leased = !string.IsNullOrEmpty(asset.Ownership);
        return state switch
        {
            State.Overdue => leased ? "Lease ended" : "Unsupported",
            State.Soon => leased ? "Lease ending" : "Support ending",
            State.Ok => leased ? "Lease running" : "Supported",
            _ => "Not recorded"
        };
    }
    // The badge class for a state: red when overdue, amber when soon, green when fine.
    public static string Tone(State state) => state switch { State.Overdue => "badge-warning", State.Soon => "badge-soon", State.Ok => "badge-good", _ => "" };

    // "Record a check": the next one is due a set number of months after this one.
    public static DateOnly NextCheck(DateOnly checkedOn, int intervalMonths) => checkedOn.AddMonths(Math.Clamp(intervalMonths, 1, 120));
}
