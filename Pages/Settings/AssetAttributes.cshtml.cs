using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Settings;
public class AssetAttributesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<AssetAttributeDefinition> Attributes => store.AssetAttributeDefinitions;
    public IActionResult OnPost(string name, string[]? assetTypes, string fieldType, string? choices) { TempData["Message"] = store.AddAssetAttributeDefinition(name, assetTypes, fieldType, choices); return RedirectToPage(); }
    public IActionResult OnPostSaveScope(Guid id, string[]? assetTypes) { TempData["Message"] = store.SetAssetAttributeAssetTypes(id, assetTypes); return RedirectToPage(); }
    public IActionResult OnPostDelete(Guid id) { TempData["Message"] = store.DeleteAssetAttributeDefinition(id); return RedirectToPage(); }
}
