using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Kits;

public class EditModel(HelpdeskStore store) : PageModel
{
    public LoanKit? Kit { get; private set; }
    // Disposed assets, and assets someone already holds, are not offered - the store refuses both. One already ticked
    // into this kit still shows so it can be removed, which also covers the kit's own equipment while it is out.
    public IReadOnlyList<AssetRecord> Assets => store.Assets
        .Where(x => (!HelpdeskStore.IsDisposed(x) && x.AssignedUserId is null) || SelectedAssetIds.Contains(x.Id) || (Kit?.AssetIds.Contains(x.Id) ?? false))
        .OrderBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
    public Guid[] SelectedAssetIds { get; private set; } = [];
    public IReadOnlyList<KitLoan> History { get; private set; } = [];
    public DateTime Now { get; } = DateTime.UtcNow;
    [TempData] public string? Message { get; set; }

    // One page serves adding and editing, so the convention lets in anyone with either permission and the handlers
    // sort out which of the two this actually is. See Program.cs.
    private ModulePermission Needed(Guid? id) => id.HasValue ? ModulePermission.Edit : ModulePermission.New;

    public IActionResult OnGet(Guid? id)
    {
        if (!store.UserCan(User, Modules.Kits, Needed(id))) return Forbid();
        if (!id.HasValue) return Page();
        Kit = store.LoanKits.FirstOrDefault(x => x.Id == id);
        if (Kit is null) return Page();
        SelectedAssetIds = Kit.AssetIds.ToArray();
        History = store.KitLoans.Where(x => x.KitId == Kit.Id).OrderByDescending(x => x.IssuedAt).ToList();
        return Page();
    }

    public string Duration(KitLoan loan) => LoanInsights.Duration(loan, Now);

    public IActionResult OnPost(Guid? id, string name, string? notes, Guid[]? assetIds, bool retired)
    {
        if (!store.UserCan(User, Modules.Kits, Needed(id))) return Forbid();
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
        if (!store.UserCan(User, Modules.Kits, ModulePermission.Delete)) return Forbid();
        TempData["Message"] = store.DeleteLoanKit(id).Message;
        return RedirectToPage("/Kits");
    }
}
