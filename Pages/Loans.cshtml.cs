using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The desk screen for loans of both kinds: what is out right now, and what has come back recently. Kits themselves are
// managed under /Kits; this page is about who has what.
public class LoansModel(HelpdeskStore store) : PageModel
{
    public const int RecentLimit = 25;

    public DateOnly Today { get; } = AssetInsights.Today;
    public DateTime Now { get; } = DateTime.UtcNow;
    [TempData] public string? Message { get; set; }

    public IReadOnlyList<LoanInsights.LoanEntry> Out { get; private set; } = [];
    public IReadOnlyList<LoanInsights.LoanEntry> Recent { get; private set; } = [];
    public int OverdueCount { get; private set; }
    public int KitCount { get; private set; }
    public int AssetCount { get; private set; }

    public void OnGet()
    {
        var all = store.AllLoans();
        Out = LoanInsights.CurrentlyOut(all);
        OverdueCount = Out.Count(x => x.IsOverdue(Today));
        KitCount = Out.Count(x => x.Kind == LoanInsights.KitKind);
        AssetCount = Out.Count(x => x.Kind == LoanInsights.AssetKind);
        Recent = all.Where(x => !x.IsOut).OrderByDescending(x => x.ReturnedAt).Take(RecentLimit).ToList();
    }

    public string Duration(LoanInsights.LoanEntry loan) => LoanInsights.Duration(loan, Now);

    // Kits are booked back in as a unit; an individual asset comes back from its own page, where the status can be set
    // at the same time (back to stock, or straight into repair).
    public Guid? KitIdFor(LoanInsights.LoanEntry loan) =>
        loan.Kind == LoanInsights.KitKind ? store.LoanKits.FirstOrDefault(x => x.Name == loan.What)?.Id : null;

    public Guid? AssetIdFor(LoanInsights.LoanEntry loan) =>
        loan.Kind == LoanInsights.AssetKind ? store.Assets.FirstOrDefault(x => x.AssetTag == loan.What)?.Id : null;

    // The list opens at Access; booking a loan back in changes it, so it needs Edit.
    public IActionResult OnPostReturn(Guid kitId, string? notes)
    {
        if (!store.UserCan(User, Modules.Loans, ModulePermission.Edit)) return Forbid();
        Message = store.ReturnKit(kitId, notes).Message;
        return RedirectToPage();
    }
}
