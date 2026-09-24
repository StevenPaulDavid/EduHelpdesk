using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Loans;

// Issues a loan of either kind from one place, because at the desk with a teacher in front of you the question is
// "what can I give them", not "which module am I in". Arriving with ?id=<kit> from the Kits page pre-selects that kit.
public class IssueModel(HelpdeskStore store) : PageModel
{
    public LoanKit? Kit { get; private set; }
    public IReadOnlyList<UserRecord> Users => store.Users.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<string> Reasons => store.LoanReasons;
    // Kits that are not retired and not already out.
    public IReadOnlyList<LoanKit> AvailableKits => store.LoanKits
        .Where(x => !x.IsRetired && !store.KitLoans.Any(l => l.KitId == x.Id && l.ReturnedAt is null))
        .OrderBy(x => x.Name, NaturalComparer.Instance).ToList();
    // Assets that can be lent on their own: not already held by someone, and not part of a kit (kit equipment only ever
    // goes out as part of its kit - see HelpdeskStore.LoanAsset).
    public IReadOnlyList<AssetRecord> AvailableAssets => store.Assets
        .Where(x => x.AssignedUserId is null && !HelpdeskStore.IsDisposed(x) && store.KitContaining(x.Id) is null)
        .OrderBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
    // Same day by default; the technician can push it out when someone needs it for longer.
    public DateOnly DefaultDueBack { get; } = AssetInsights.Today;
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(Guid? id)
    {
        if (id is not { } kitId) return Page();
        Kit = store.LoanKits.FirstOrDefault(x => x.Id == kitId);
        if (Kit is null) { TempData["Message"] = "Loan kit was not found."; return RedirectToPage("/Kits"); }
        if (store.KitLoans.Any(x => x.KitId == kitId && x.ReturnedAt is null))
        {
            TempData["Message"] = $"{Kit.Name} is already out on loan.";
            return RedirectToPage("/Kits");
        }
        return Page();
    }

    public IActionResult OnPost(string? what, Guid? kitId, Guid? assetId, Guid? borrowerUserId, string? borrowerName, string? reason, DateOnly? dueBack, string? notes)
    {
        var due = dueBack ?? DefaultDueBack;
        if (what == "asset")
        {
            // Individual asset loans are directory-only: an asset assigned to a free-typed name cannot be traced back
            // to anyone later. Someone not in People has to be given a kit instead.
            if (assetId is not { } asset) { ModelState.AddModelError("", "Choose a device to lend."); return Page(); }
            if (borrowerUserId is not { } borrower) { ModelState.AddModelError("", "Choose who is borrowing it. Only people in the directory can be lent an individual device - issue a kit instead for supply staff and visitors."); return Page(); }
            var (assetOk, assetMessage) = store.LoanAsset(asset, borrower, due, reason);
            if (assetOk)
            {
                TempData["Message"] = assetMessage;
                return RedirectToPage("/Loans");
            }
            ModelState.AddModelError("", assetMessage);
            return Page();
        }

        if (kitId is not { } chosenKit) { ModelState.AddModelError("", "Choose a kit to issue."); return Page(); }
        Kit = store.LoanKits.FirstOrDefault(x => x.Id == chosenKit);
        if (Kit is null) { TempData["Message"] = "Loan kit was not found."; return RedirectToPage("/Kits"); }

        var (ok, message) = store.IssueKit(chosenKit, borrowerUserId, borrowerName, reason, due, IssuedBy(), notes);
        if (ok)
        {
            TempData["Message"] = message;
            return RedirectToPage("/Loans");
        }
        ModelState.AddModelError("", message);
        return Page();
    }

    // Loans record who handed the kit over - useful at the desk, and unrelated to the generic audit log,
    // which deliberately records what changed rather than who.
    private string IssuedBy() => User.Identity?.Name ?? string.Empty;
}
