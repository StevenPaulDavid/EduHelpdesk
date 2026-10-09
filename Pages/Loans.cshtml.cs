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

    // The filter bar over what is out now, the same as the other inventory lists: search, then kind, reason and overdue.
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "kind")] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "reason")] public string? Reason { get; set; }
    [BindProperty(SupportsGet = true, Name = "overdue")] public bool OverdueOnly { get; set; }
    public IReadOnlyList<string> Reasons { get; private set; } = [];
    // How many are out before the filters: the counts at the top are of everything out, not of what is showing.
    public int OutTotal { get; private set; }
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Kind) || !string.IsNullOrWhiteSpace(Reason) || OverdueOnly;
    public int PanelFilterCount => (string.IsNullOrWhiteSpace(Kind) ? 0 : 1) + (string.IsNullOrWhiteSpace(Reason) ? 0 : 1) + (OverdueOnly ? 1 : 0);
    public static string KindLabel(string? kind) => kind == LoanInsights.KitKind ? "Kits" : kind == LoanInsights.AssetKind ? "Individual devices" : kind ?? "";

    public void OnGet()
    {
        var all = store.AllLoans();
        var outNow = LoanInsights.CurrentlyOut(all);
        OutTotal = outNow.Count;
        OverdueCount = outNow.Count(x => x.IsOverdue(Today));
        KitCount = outNow.Count(x => x.Kind == LoanInsights.KitKind);
        AssetCount = outNow.Count(x => x.Kind == LoanInsights.AssetKind);
        Reasons = outNow.Select(x => x.ReasonLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        IEnumerable<LoanInsights.LoanEntry> query = outNow;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(x => terms.All(t => x.What.Contains(t, StringComparison.OrdinalIgnoreCase) || (x.BorrowerName ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)));
        }
        if (Kind is LoanInsights.KitKind or LoanInsights.AssetKind) query = query.Where(x => x.Kind == Kind);
        if (!string.IsNullOrWhiteSpace(Reason)) query = query.Where(x => string.Equals(x.ReasonLabel, Reason, StringComparison.OrdinalIgnoreCase));
        if (OverdueOnly) query = query.Where(x => x.IsOverdue(Today));
        Out = query.ToList();
        Recent = all.Where(x => !x.IsOut).OrderByDescending(x => x.ReturnedAt).Take(RecentLimit).ToList();
    }

    private Dictionary<string, object?> Route() => new()
    {
        ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["kind"] = string.IsNullOrWhiteSpace(Kind) ? null : Kind,
        ["reason"] = string.IsNullOrWhiteSpace(Reason) ? null : Reason,
        ["overdue"] = OverdueOnly ? "true" : null
    };

    // Every filter in force as a chip that takes just that one off.
    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        string? Without(string key) { var route = Route(); route[key] = null; return Url.Page("/Loans", route); }
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q")));
        if (!string.IsNullOrWhiteSpace(Kind)) chips.Add(new(KindLabel(Kind), Without("kind")));
        if (!string.IsNullOrWhiteSpace(Reason)) chips.Add(new($"Reason: {Reason}", Without("reason")));
        if (OverdueOnly) chips.Add(new("Overdue only", Without("overdue")));
        return chips;
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
