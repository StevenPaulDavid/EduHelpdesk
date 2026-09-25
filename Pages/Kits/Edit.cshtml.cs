using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Kits;

public class EditModel(HelpdeskStore store) : PageModel
{
    public const int SearchLimit = 20;

    public LoanKit? Kit { get; private set; }
    // The assets ticked into the kit, in tag order. Only these are rendered: the rest of the register is reached through
    // the search box, because a school with a thousand assets cannot scroll a checklist of all of them.
    public IReadOnlyList<AssetRecord> SelectedAssets { get; private set; } = [];
    public IReadOnlyList<KitLoan> History { get; private set; } = [];
    // Set while the kit is out. The page is then read-only for everyone - the store refuses changes either way.
    public KitLoan? OpenLoan { get; private set; }
    public bool CanBookIn => store.UserCan(User, Modules.Loans, ModulePermission.Edit);
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
        Load(Kit.AssetIds);
        return Page();
    }

    public string Duration(KitLoan loan) => LoanInsights.Duration(loan, Now);

    // What the asset search box asks for as you type. Every word has to appear somewhere in the tag, serial, make,
    // model, type or location, so "dell 7420" or "LAP-01" both narrow quickly. Only assets that could actually go in
    // the kit are returned - see HelpdeskStore.KitCandidates.
    public IActionResult OnGetSearch(Guid? id, string? q)
    {
        if (!store.UserCan(User, Modules.Kits, Needed(id))) return Forbid();
        var terms = (q ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return new JsonResult(Array.Empty<object>());
        var matches = store.KitCandidates(id)
            .Where(a => terms.All(t => Searchable(a).Any(f => f.Contains(t, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(a => a.AssetTag.StartsWith(terms[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(a => a.AssetTag, NaturalComparer.Instance)
            // One more than shown, so the page can say there are more and to keep typing.
            .Take(SearchLimit + 1)
            .Select(a => new { id = a.Id, tag = a.AssetTag, detail = Detail(a), url = Url.Page("/Asset", new { id = a.Id }) });
        return new JsonResult(matches);
    }

    private static string[] Searchable(AssetRecord a) => [a.AssetTag, a.SerialNumber, a.Make, a.Model, a.Type, a.Location];

    public static string Detail(AssetRecord a) =>
        string.Join(" · ", new[] { $"{a.Make} {a.Model}".Trim(), a.Type, a.Location }.Where(x => !string.IsNullOrWhiteSpace(x)));

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
        if (id.HasValue) Kit = store.LoanKits.FirstOrDefault(x => x.Id == id);
        // Show what was submitted, so a refused save does not throw away the rest of the changes.
        Load(assetIds ?? []);
        return Page();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        if (!store.UserCan(User, Modules.Kits, ModulePermission.Delete)) return Forbid();
        TempData["Message"] = store.DeleteLoanKit(id).Message;
        return RedirectToPage("/Kits");
    }

    private void Load(IEnumerable<Guid> assetIds)
    {
        var ids = assetIds.ToHashSet();
        SelectedAssets = store.Assets.Where(x => ids.Contains(x.Id)).OrderBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
        if (Kit is null) return;
        History = store.KitLoans.Where(x => x.KitId == Kit.Id).OrderByDescending(x => x.IssuedAt).ToList();
        OpenLoan = History.FirstOrDefault(x => x.ReturnedAt is null);
    }
}
