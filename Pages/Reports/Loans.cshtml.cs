using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// Lives with the other reports rather than in the Loans module: /Loans is the desk screen for issuing and booking kits
// back in, and this is a management report about the same data, so it belongs next to the asset, ticket and parts ones.
public class LoanReportsModel(HelpdeskStore store) : PageModel
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

    public IActionResult OnGetExport(string? table)
    {
        OnGet();
        var (name, csv) = (table ?? "").ToLowerInvariant() switch
        {
            // One row per loan, with the borrower repeated, so it pivots by person or by reason in a spreadsheet.
            "loans" => ("loans", Csv.Table(
                ["Borrower", "Kit", "Reason", "Issued (local)", "Due back", "Returned (local)", "Kept", "Issued by", "Notes"],
                Loans.OrderByDescending(x => x.IssuedAt),
                x => [x.BorrowerName, KitName(x.KitId), x.Reason, x.IssuedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), x.DueBack.ToString("yyyy-MM-dd"),
                      x.ReturnedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", Duration(x), x.IssuedBy, x.Notes])),
            "borrowers" => ("loan-borrowers", Csv.Table(
                ["Person", "Loans", "Reasons", "Most recent (local)", "Still out", "Flagged"],
                Borrowers,
                x => [x.Name, x.Count.ToString(), string.Join("; ", x.ByReason.Select(r => $"{r.Reason} ({r.Count})")),
                      x.LastIssued.ToLocalTime().ToString("yyyy-MM-dd"), x.StillOut.ToString(), IsFlagged(x) ? "Yes" : "No"])),
            "reasons" => ("loan-reasons", Csv.Table(
                ["Reason", "Loans", "Share %"],
                ReasonBreakdown,
                x => [x.Reason, x.Count.ToString(), Loans.Count == 0 ? "0" : (x.Count * 100.0 / Loans.Count).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)])),
            "out" => ("loans-still-out", Csv.Table(
                ["Kit", "Borrower", "Reason", "Issued (local)", "Due back", "Overdue", "Out for"],
                Out,
                x => [KitName(x.KitId), x.BorrowerName, x.Reason, x.IssuedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), x.DueBack.ToString("yyyy-MM-dd"),
                      x.DueBack < Today ? "Yes" : "No", Duration(x)])),
            _ => ("", "")
        };
        if (name.Length == 0) return NotFound();
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName(name, DateTime.Now));
    }

    public string KitName(Guid kitId) => Kits.FirstOrDefault(x => x.Id == kitId)?.Name ?? "Unknown kit";
    public string Duration(KitLoan loan) => LoanInsights.Duration(loan, Now);
    public bool IsFlagged(LoanInsights.BorrowerRow row) => Flagged.Any(x => x.Name == row.Name && x.UserId == row.UserId);
}
