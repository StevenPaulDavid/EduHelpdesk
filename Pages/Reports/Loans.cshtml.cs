using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// Lives with the other reports rather than in the Loans module: /Loans is the desk screen for issuing and booking kits
// back in, and this is a management report about the same data.
// It covers loans of both kinds - a kit handed over, and a single asset lent from its own page - because the question
// it answers is "how many times has this person been given a device", and the answer should not depend on which route
// the technician used.
public class LoanReportsModel(HelpdeskStore store) : PageModel
{
    // Taking CSV and print away is a separate decision from taking the report away, so the download links and the print
    // button hang off this rather than off the report's own flag.
    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);
    // "4 loan machines in the last 2 weeks" is the conversation this report exists for, so 14 days leads.
    public static readonly (string Key, string Label, int? Days)[] Periods =
    [
        ("14d", "Last 2 weeks", 14),
        ("30d", "Last 30 days", 30),
        ("term", "Last 3 months", 90),
        ("12m", "Last 12 months", 365),
        ("all", "All time", null)
    ];

    public static readonly (string Key, string Label)[] Kinds =
    [
        ("", "Kits and assets"),
        (LoanInsights.KitKind, "Kit loans only"),
        (LoanInsights.AssetKind, "Asset loans only")
    ];

    [BindProperty(SupportsGet = true, Name = "period")] public string? Period { get; set; }
    [BindProperty(SupportsGet = true, Name = "reason")] public string? Reason { get; set; }
    [BindProperty(SupportsGet = true, Name = "kind")] public string? Kind { get; set; }

    public string PeriodKey { get; private set; } = "14d";
    public string PeriodLabel { get; private set; } = "Last 2 weeks";
    public string KindLabel { get; private set; } = "Kits and assets";
    public DateTime Since { get; private set; }
    public DateOnly Today { get; } = AssetInsights.Today;
    public DateTime Now { get; } = DateTime.UtcNow;

    public IReadOnlyList<string> Reasons => store.LoanReasons;
    public int RepeatCount => store.LoanRepeatCount;
    public int RepeatDays => store.LoanRepeatDays;

    public IReadOnlyList<LoanInsights.LoanEntry> Loans { get; private set; } = [];
    public IReadOnlyList<LoanInsights.BorrowerRow> Borrowers { get; private set; } = [];
    public IReadOnlyList<LoanInsights.BorrowerRow> Flagged { get; private set; } = [];
    public IReadOnlyList<(string Reason, int Count)> ReasonBreakdown { get; private set; } = [];
    public IReadOnlyList<LoanInsights.LoanEntry> Out { get; private set; } = [];
    public int OverdueCount { get; private set; }
    // Loans with no reason recorded, so the note can explain why some rows say so.
    public int NotRecordedCount { get; private set; }

    public void OnGet()
    {
        var period = Periods.FirstOrDefault(x => x.Key == Period);
        if (period.Key is null) period = Periods[0];
        PeriodKey = period.Key;
        PeriodLabel = period.Label;
        Since = period.Days is { } days ? DateTime.UtcNow.AddDays(-days) : DateTime.MinValue;

        Kind = Kinds.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, Kind, StringComparison.OrdinalIgnoreCase) && x.Length > 0);
        KindLabel = Kinds.First(x => x.Key == (Kind ?? "")).Label;

        var all = Matching(store.AllLoans());
        var inPeriod = LoanInsights.InPeriod(all, Since);
        if (!string.IsNullOrWhiteSpace(Reason))
            inPeriod = inPeriod.Where(x => string.Equals(x.ReasonLabel, Reason, StringComparison.OrdinalIgnoreCase)).ToList();

        Loans = inPeriod;
        Borrowers = LoanInsights.Borrowers(inPeriod);
        ReasonBreakdown = LoanInsights.ByReason(inPeriod);
        NotRecordedCount = inPeriod.Count(x => x.Reason is null);

        // Flagging always uses the configured window, independent of the period being viewed, so the threshold
        // means the same thing however the report is filtered. Loans with no reason or no start date are shown in the
        // lists above but never counted here - a record that was never meant as a loan must not put someone on a list
        // a line manager acts on.
        var flagWindow = DateTime.UtcNow.AddDays(-RepeatDays);
        Flagged = LoanInsights.Borrowers(LoanInsights.InPeriod(all, flagWindow).Where(x => x.CountsTowardFlag))
            .Where(x => x.CountedLoans >= RepeatCount)
            .ToList();

        Out = LoanInsights.CurrentlyOut(all);
        OverdueCount = Out.Count(x => x.IsOverdue(Today));
    }

    private IEnumerable<LoanInsights.LoanEntry> Matching(IEnumerable<LoanInsights.LoanEntry> loans) =>
        string.IsNullOrEmpty(Kind) ? loans : loans.Where(x => x.Kind == Kind);

    public string Duration(LoanInsights.LoanEntry loan) => LoanInsights.Duration(loan, Now);
    public bool IsFlagged(LoanInsights.BorrowerRow row) => Flagged.Any(x => x.Name == row.Name && x.UserId == row.UserId);

    public IActionResult OnGetExport(string? table)
    {
        if (!CanExport) return Forbid();
        OnGet();
        var (name, csv) = (table ?? "").ToLowerInvariant() switch
        {
            // One row per loan, with the borrower repeated, so it pivots by person, kind or reason in a spreadsheet.
            "loans" => ("loans", Csv.Table(
                ["Borrower", "Kind", "What", "Reason", "Issued (local)", "Due back", "Returned (local)", "Kept", "Counts toward flag"],
                Loans.OrderByDescending(x => x.SortDate),
                x => [x.BorrowerName, x.Kind, x.What, x.ReasonLabel, x.IssuedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "",
                      x.DueBack?.ToString("yyyy-MM-dd") ?? "", x.ReturnedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", Duration(x), x.CountsTowardFlag ? "Yes" : "No"])),
            "borrowers" => ("loan-borrowers", Csv.Table(
                ["Person", "Loans", "Counted toward flag", "Reasons", "Most recent (local)", "Still out", "Flagged"],
                Borrowers,
                x => [x.Name, x.Count.ToString(), x.CountedLoans.ToString(), string.Join("; ", x.ByReason.Select(r => $"{r.Reason} ({r.Count})")),
                      x.LastIssued == DateTime.MinValue ? "" : x.LastIssued.ToLocalTime().ToString("yyyy-MM-dd"), x.StillOut.ToString(), IsFlagged(x) ? "Yes" : "No"])),
            "reasons" => ("loan-reasons", Csv.Table(
                ["Reason", "Loans", "Share %"],
                ReasonBreakdown,
                x => [x.Reason, x.Count.ToString(), Loans.Count == 0 ? "0" : (x.Count * 100.0 / Loans.Count).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)])),
            "out" => ("loans-still-out", Csv.Table(
                ["Kind", "What", "Borrower", "Reason", "Issued (local)", "Due back", "Overdue", "Out for"],
                Out,
                x => [x.Kind, x.What, x.BorrowerName, x.ReasonLabel, x.IssuedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "",
                      x.DueBack?.ToString("yyyy-MM-dd") ?? "", x.IsOverdue(Today) ? "Yes" : "No", Duration(x)])),
            _ => ("", "")
        };
        if (name.Length == 0) return NotFound();
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName(name, DateTime.Now));
    }
}
