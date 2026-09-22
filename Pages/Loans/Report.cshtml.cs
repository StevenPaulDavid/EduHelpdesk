using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Loans;

public class ReportModel(HelpdeskStore store) : PageModel
{
    // "4 loan machines in the last 2 weeks" is the conversation this report exists for, so 14 days leads.
    public static readonly (string Key, string Label, int? Days)[] Periods =
    [
        ("14d", "Last 2 weeks", 14),
        ("30d", "Last 30 days", 30),
        ("term", "Last 3 months", 90),
        ("12m", "Last 12 months", 365),
        ("all", "All time", null)
    ];

    [BindProperty(SupportsGet = true, Name = "period")] public string? Period { get; set; }
    [BindProperty(SupportsGet = true, Name = "reason")] public string? Reason { get; set; }

    public string PeriodKey { get; private set; } = "14d";
    public string PeriodLabel { get; private set; } = "Last 2 weeks";
    public DateTime Since { get; private set; }
    public DateOnly Today { get; } = AssetInsights.Today;
    public DateTime Now { get; } = DateTime.UtcNow;

    public IReadOnlyList<string> Reasons => store.LoanReasons;
    public IReadOnlyList<LoanKit> Kits => store.LoanKits;
    public int RepeatCount => store.LoanRepeatCount;
    public int RepeatDays => store.LoanRepeatDays;

    public IReadOnlyList<KitLoan> Loans { get; private set; } = [];
    public IReadOnlyList<LoanInsights.BorrowerRow> Borrowers { get; private set; } = [];
    public IReadOnlyList<LoanInsights.BorrowerRow> Flagged { get; private set; } = [];
    public IReadOnlyList<(string Reason, int Count)> ReasonBreakdown { get; private set; } = [];
    public IReadOnlyList<KitLoan> Out { get; private set; } = [];
    public int OverdueCount { get; private set; }

    public void OnGet()
    {
        var period = Periods.FirstOrDefault(x => x.Key == Period);
        if (period.Key is null) period = Periods[0];
        PeriodKey = period.Key;
        PeriodLabel = period.Label;
        Since = period.Days is { } days ? DateTime.UtcNow.AddDays(-days) : DateTime.MinValue;

        var all = store.KitLoans;
        var inPeriod = LoanInsights.InPeriod(all, Since);
        if (!string.IsNullOrWhiteSpace(Reason))
            inPeriod = inPeriod.Where(x => string.Equals(x.Reason, Reason, StringComparison.OrdinalIgnoreCase)).ToList();

        Loans = inPeriod;
        Borrowers = LoanInsights.Borrowers(inPeriod);
        ReasonBreakdown = LoanInsights.ByReason(inPeriod);

        // Flagging always uses the configured window, independent of the period being viewed, so the threshold
        // means the same thing however the report is filtered.
        var flagWindow = DateTime.UtcNow.AddDays(-RepeatDays);
        Flagged = LoanInsights.Borrowers(LoanInsights.InPeriod(all, flagWindow))
            .Where(x => x.Count >= RepeatCount)
            .ToList();

        Out = LoanInsights.CurrentlyOut(all);
        OverdueCount = Out.Count(x => x.DueBack < Today);
    }

    public string KitName(Guid kitId) => Kits.FirstOrDefault(x => x.Id == kitId)?.Name ?? "Unknown kit";
    public string Duration(KitLoan loan) => LoanInsights.Duration(loan, Now);
    public bool IsFlagged(LoanInsights.BorrowerRow row) => Flagged.Any(x => x.Name == row.Name && x.UserId == row.UserId);
}
