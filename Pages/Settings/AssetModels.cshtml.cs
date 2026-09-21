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
    public IActionResult OnPostDelete(string value) { TempData["Message"] = store.DeleteManagedOption("Asset model", value); return RedirectToPage(); }
}
