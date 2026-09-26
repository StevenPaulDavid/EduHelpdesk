using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Branding & logo. Needs Settings: Edit, like every page under /Settings except the audit log (Program.cs).
public class BrandingModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public BrandingSettings Branding { get; set; } = new();
    [BindProperty] public IFormFile? Logo { get; set; }
    public string? LogoVersion => store.LogoVersion;
    [TempData] public string? Message { get; set; }

    public void OnGet() => Branding = store.Branding;

    public IActionResult OnPostSaveBranding()
    {
        if (!Hex(Branding.PrimaryColor) || !Hex(Branding.AccentColor) || !Hex(Branding.BackgroundColor))
        {
            Message = "Colours must be valid six-digit hex values, such as #067A78.";
            Branding = store.Branding;
            return Page();
        }
        if (!Enum.IsDefined(Branding.DefaultAppearance)) Branding.DefaultAppearance = store.Branding.DefaultAppearance;

        store.UpdateBranding(Branding);
        Message = "Branding saved.";
        return RedirectToPage();
    }

    public IActionResult OnPostUploadLogo()
    {
        if (Logo is null || Logo.Length == 0)
        {
            Message = "Choose a PNG file to upload.";
            return RedirectToPage(null, null, "logo");
        }
        using var stream = Logo.OpenReadStream();
        Message = store.SaveLogo(stream, Logo.Length).Message;
        return RedirectToPage(null, null, "logo");
    }

    public IActionResult OnPostRemoveLogo()
    {
        Message = store.RemoveLogo().Message;
        return RedirectToPage(null, null, "logo");
    }

    private static bool Hex(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$");
}
