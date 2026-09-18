using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class AssetAttributesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<AssetAttributeDefinition> Attributes => store.AssetAttributeDefinitions;
    public IActionResult OnPost(string name, string assetType, string fieldType, string? choices) { TempData["Message"] = store.AddAssetAttributeDefinition(name, assetType, fieldType, choices); return RedirectToPage(); }
    public IActionResult OnPostDelete(Guid id) { TempData["Message"] = store.DeleteAssetAttributeDefinition(id); return RedirectToPage(); }
}
