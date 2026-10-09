using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Asset, part & loan rules: the review window, academic year, reorder threshold and repeat-loan flag.
// Needs Settings: Edit (Program.cs).
public class InventoryRulesModel(HelpdeskStore store) : PageModel
{
    public int AssetReviewDays => store.AssetReviewDays;
    public AssetCheckSettings Checks => store.AssetCheckSettings;
    public int AcademicYearStartMonth => store.AcademicYearStartMonth;
    public static IReadOnlyList<(int Month, string Name)> AcademicMonths => AcademicYear.Months;
    public int PartsDefaultReorderThreshold => store.PartsDefaultReorderThreshold;
    public int LoanRepeatCount => store.LoanRepeatCount;
    public int LoanRepeatDays => store.LoanRepeatDays;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostSaveAssetReview(int days)
    {
        Message = store.SetAssetReviewDays(days);
        return RedirectToPage();
    }

    public IActionResult OnPostSaveAssetChecks(int dueSoonDays, int supportWarningDays, int intervalMonths)
    {
        Message = store.SetAssetCheckSettings(dueSoonDays, supportWarningDays, intervalMonths);
        return RedirectToPage();
    }

    public IActionResult OnPostSaveAcademicYear(int month)
    {
        Message = store.SetAcademicYearStartMonth(month);
        return RedirectToPage();
    }

    public IActionResult OnPostSavePartsReorderThreshold(int threshold)
    {
        Message = store.SetPartsDefaultReorderThreshold(threshold);
        return RedirectToPage();
    }

    public IActionResult OnPostSaveLoanThreshold(int count, int days)
    {
        Message = store.SetLoanRepeatThreshold(count, days);
        return RedirectToPage();
    }
}
