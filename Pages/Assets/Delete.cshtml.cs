using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Assets;
public class DeleteModel(HelpdeskStore store) : PageModel
{
    public EduHelpdesk.Models.AssetRecord? Asset { get; private set; }
    public IActionResult OnGet(Guid id) { Asset = store.Assets.FirstOrDefault(x => x.Id == id); return Asset is null ? NotFound() : Page(); }
    public IActionResult OnPost(Guid id) { TempData["Message"] = store.DeleteAsset(id) ?? "Asset deleted."; return RedirectToPage("/Assets"); }
}
