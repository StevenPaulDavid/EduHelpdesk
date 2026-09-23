using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The kits themselves: what exists, what is in each one and which are out. Recording and reporting loans is the Loans
// module's job - this one is about the equipment.
public class KitsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<LoanKit> Kits => store.LoanKits;
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public DateOnly Today { get; } = AssetInsights.Today;
    public DateTime Now { get; } = DateTime.UtcNow;
    [TempData] public string? Message { get; set; }

    private IReadOnlyList<KitLoan>? _openLoans;
    private IReadOnlyList<KitLoan> OpenLoans => _openLoans ??= store.KitLoans.Where(x => x.ReturnedAt is null).ToList();

    public KitLoan? CurrentLoan(Guid kitId) => OpenLoans.FirstOrDefault(x => x.KitId == kitId);
    public bool IsOut(Guid kitId) => CurrentLoan(kitId) is not null;
    public int OutCount => OpenLoans.Count;
    public int OverdueCount => OpenLoans.Count(x => x.DueBack < Today);
    public int AvailableCount => Kits.Count(x => !x.IsRetired && !IsOut(x.Id));

    public string KitContents(LoanKit kit)
    {
        var tags = kit.AssetIds
            .Select(id => Assets.FirstOrDefault(a => a.Id == id))
            .Where(a => a is not null)
            .Select(a => a!.AssetTag);
        return string.Join(", ", tags);
    }

    public string Duration(KitLoan loan) => LoanInsights.Duration(loan, Now);

    public void OnGet() { }

    public IActionResult OnPostReturn(Guid kitId, string? notes)
    {
        Message = store.ReturnKit(kitId, notes).Message;
        return RedirectToPage();
    }
}
