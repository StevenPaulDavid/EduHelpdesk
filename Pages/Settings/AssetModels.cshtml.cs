using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class AssetModelsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> Makes => store.AssetMakes;
    public IReadOnlyList<string> Models => store.AssetModels;
    public IReadOnlyDictionary<string, string> ModelMakes => store.AssetModelMakes;
    public IActionResult OnPost(string value, string? make) { TempData["Message"] = store.AddAssetModel(value, make); return RedirectToPage(); }
    public IActionResult OnPostSave(string currentValue, string value, string? make) { TempData["Message"] = store.UpdateAssetModel(currentValue, value, make); return RedirectToPage(); }
    public IActionResult OnPostDelete(string value) { MergeOffer.Delete(this, store, "Asset model", value, () => store.DeleteManagedOption("Asset model", value)); return RedirectToPage(); }
    // Moves what uses it to another, then takes it off (_MergePrompt).
    public IActionResult OnPostMerge(string value, string? into) { MergeOffer.Merge(this, store, "Asset model", value, into); return RedirectToPage(); }
}
