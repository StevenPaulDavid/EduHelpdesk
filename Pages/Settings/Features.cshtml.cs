using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Features: switching whole parts of the helpdesk off and on (Services/Features.cs). Needs Settings: Edit,
// like every page under /Settings (Program.cs).
public class FeaturesModel(HelpdeskStore store) : PageModel
{
    public bool IsOn(Features.Feature feature) => store.IsFeatureOn(feature.Key);
    [TempData] public string? Message { get; set; }

    public IActionResult OnPost(string? key, bool on)
    {
        Message = store.SetFeature(key, on).Message;
        return RedirectToPage(null, null, null, key);
    }
}
