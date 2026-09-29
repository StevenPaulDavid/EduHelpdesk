using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// The finance policy's spending bands, shown on every project for reference. Gated like the rest of /Settings (Settings:
// Edit) by the folder convention in Program.cs.
public class SpendingBandsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<SpendingBand> Bands => store.SpendingBands;
    public bool IncludeVat => store.SpendingBandsIncludeVat;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostAdd(string? name, string? from, string? upTo, int quotesNeeded, string? requirements) =>
        Done(store.AddSpendingBand(name, from, upTo, quotesNeeded, requirements));

    public IActionResult OnPostUpdate(Guid id, string? name, string? from, string? upTo, int quotesNeeded, string? requirements) =>
        Done(store.UpdateSpendingBand(id, name, from, upTo, quotesNeeded, requirements));

    public IActionResult OnPostDelete(Guid id) => Done(store.DeleteSpendingBand(id));

    public IActionResult OnPostBasis(bool includeVat) => Done(store.SetSpendingBandsIncludeVat(includeVat));

    public bool PageIncludesVat => store.ProjectPageIncludesVat;
    public IActionResult OnPostPageVat(bool includeVat) => Done(store.SetProjectPageIncludesVat(includeVat));

    private IActionResult Done((bool Ok, string Message) result)
    {
        Message = result.Message;
        return RedirectToPage();
    }

    public static string Amount(decimal? value) => value?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
