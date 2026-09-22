using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Loans;

public class ReportPrintModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "period")] public string? Period { get; set; }
    [BindProperty(SupportsGet = true, Name = "reason")] public string? Reason { get; set; }

    public string BrandName => store.Branding.BrandName;
    public string PeriodLabel { get; private set; } = "Last 2 weeks";
    public DateTime GeneratedAt { get; } = DateTime.Now;
    public DateOnly Today { get; } = AssetInsights.Today;
    public DateTime Now { get; } = DateTime.UtcNow;
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
        var period = ReportModel.Periods.FirstOrDefault(x => x.Key == Period);
        if (period.Key is null) period = ReportModel.Periods[0];
        PeriodLabel = period.Label;
        var since = period.Days is { } days ? DateTime.UtcNow.AddDays(-days) : DateTime.MinValue;

        var all = store.KitLoans;
        var inPeriod = LoanInsights.InPeriod(all, since);
        if (!string.IsNullOrWhiteSpace(Reason))
            inPeriod = inPeriod.Where(x => string.Equals(x.Reason, Reason, StringComparison.OrdinalIgnoreCase)).ToList();

        Loans = inPeriod;
        Borrowers = LoanInsights.Borrowers(inPeriod);
        ReasonBreakdown = LoanInsights.ByReason(inPeriod);
        Flagged = LoanInsights.Borrowers(LoanInsights.InPeriod(all, DateTime.UtcNow.AddDays(-RepeatDays)))
            .Where(x => x.Count >= RepeatCount).ToList();
        Out = LoanInsights.CurrentlyOut(all);
        OverdueCount = Out.Count(x => x.DueBack < Today);
    }

    public string KitName(Guid kitId) => store.LoanKits.FirstOrDefault(x => x.Id == kitId)?.Name ?? "Unknown kit";
    public string Duration(KitLoan loan) => LoanInsights.Duration(loan, Now);
    public bool IsFlagged(LoanInsights.BorrowerRow row) => Flagged.Any(x => x.Name == row.Name && x.UserId == row.UserId);
}
