using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class AssetTypesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Types => store.AssetTypes;
    public IReadOnlyDictionary<string, int> Lifespans => store.AssetTypeLifespans;
    // Each type belongs to a desk (HelpdeskStore.Desks) - boilers and extinguishers to Site - once there is more than one.
    public bool HasSeveralDesks => store.HasSeveralDesks;
    public IReadOnlyList<EduHelpdesk.Models.Desk> Desks => store.Desks;
    public EduHelpdesk.Models.Desk DeskFor(string type) => store.DeskForAssetType(type);

    public IActionResult OnPost(string value, string? lifespan, Guid? deskId) => Respond(() =>
    {
        var message = store.AddAssetType(value, ParseYears(lifespan, out var years) ? years : throw new FormatException());
        if (deskId is { } desk && message == "Asset type added.") store.SetAssetTypeDesk(value, desk);
        return message;
    });
    public IActionResult OnPostDesk(string value, Guid deskId) { TempData["Message"] = store.SetAssetTypeDesk(value, deskId).Message; return RedirectToPage(); }
    public IActionResult OnPostSave(string currentValue, string value, string? lifespan) => Respond(() => store.UpdateAssetType(currentValue, value, ParseYears(lifespan, out var years) ? years : throw new FormatException()));
    public IActionResult OnPostDelete(string value) { MergeOffer.Delete(this, store, "Asset type", value, () => store.DeleteManagedOption("Asset type", value)); return RedirectToPage(); }
    // Moves what uses it to another, then takes it off (_MergePrompt).
    public IActionResult OnPostMerge(string value, string? into) { MergeOffer.Merge(this, store, "Asset type", value, into); return RedirectToPage(); }

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
