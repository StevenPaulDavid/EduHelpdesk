using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Kits;

public class EditModel(HelpdeskStore store) : PageModel
{
    public LoanKit? Kit { get; private set; }
    // Disposed assets are not offered, but one already ticked into this kit still shows so it can be removed.
    public IReadOnlyList<AssetRecord> Assets => store.Assets
        .Where(x => !HelpdeskStore.IsDisposed(x) || SelectedAssetIds.Contains(x.Id))
        .OrderBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
    public Guid[] SelectedAssetIds { get; private set; } = [];
    public IReadOnlyList<KitLoan> History { get; private set; } = [];
    public DateTime Now { get; } = DateTime.UtcNow;
    [TempData] public string? Message { get; set; }

    public void OnGet(Guid? id)
    {
        if (!id.HasValue) return;
        Kit = store.LoanKits.FirstOrDefault(x => x.Id == id);
        if (Kit is null) return;
        SelectedAssetIds = Kit.AssetIds.ToArray();
        History = store.KitLoans.Where(x => x.KitId == Kit.Id).OrderByDescending(x => x.IssuedAt).ToList();
    }

    public string Duration(KitLoan loan) => LoanInsights.Duration(loan, Now);

    public IActionResult OnPost(Guid? id, string name, string? notes, Guid[]? assetIds, bool retired)
    {
        var (ok, message) = id.HasValue
            ? store.UpdateLoanKit(id.Value, name, notes, assetIds, retired)
            : store.AddLoanKit(name, notes, assetIds);

        if (ok)
        {
            TempData["Message"] = message;
            return RedirectToPage("/Kits");
        }

        ModelState.AddModelError("", message);
        if (id.HasValue)
        {
            Kit = store.LoanKits.FirstOrDefault(x => x.Id == id);
            History = store.KitLoans.Where(x => x.KitId == id).OrderByDescending(x => x.IssuedAt).ToList();
        }
        SelectedAssetIds = (assetIds ?? []).ToArray();
        return Page();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        TempData["Message"] = store.DeleteLoanKit(id).Message;
        return RedirectToPage("/Kits");
    }
}
