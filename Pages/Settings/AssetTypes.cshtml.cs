using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class AssetTypesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Types => store.AssetTypes;
    public IReadOnlyDictionary<string, int> Lifespans => store.AssetTypeLifespans;
    public IActionResult OnPost(string value, string? lifespan) => Respond(() => store.AddAssetType(value, ParseYears(lifespan, out var years) ? years : throw new FormatException()));
    public IActionResult OnPostSave(string currentValue, string value, string? lifespan) => Respond(() => store.UpdateAssetType(currentValue, value, ParseYears(lifespan, out var years) ? years : throw new FormatException()));
    public IActionResult OnPostDelete(string value) { TempData["Message"] = store.DeleteManagedOption("Asset type", value); return RedirectToPage(); }

    private IActionResult Respond(Func<string> action)
    {
        try { TempData["Message"] = action(); }
        catch (FormatException) { TempData["Message"] = "Enter the lifespan as a whole number of years, or leave it blank."; }
        return RedirectToPage();
    }

    // Blank means no lifespan. Returns false when the text is not a whole number.
    private static bool ParseYears(string? text, out int? years)
    {
        years = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text.Trim(), out var value)) return false;
        years = value;
        return true;
    }
}
