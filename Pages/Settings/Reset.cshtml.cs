using EduHelpdesk.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Go live & reset. Needs Settings: Edit (Program.cs) - both of these were once forms on the Settings page
// itself, which only asks for Settings: Access.
public class ResetModel(HelpdeskStore store) : PageModel
{
    public bool HasDemoData => store.HasDemoData;
    public IReadOnlyList<(string Kind, string Name)> DemoRecords => store.DemoDataSummary();
    public string BackupFolder => store.BackupFolder;
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

    public IActionResult OnPostRemoveDemoData()
    {
        Message = store.RemoveDemoData().Message;
        return RedirectToPage();
    }

    // A successful reset replaces every account with the bootstrap administrator, so the signed-in user no longer
    // exists and has to be signed out rather than left holding a cookie for a deleted account.
    public async Task<IActionResult> OnPostResetFactoryAsync(string? confirmation, bool keepBackup, bool eraseAudit)
    {
        var (ok, message) = store.ResetFactory(confirmation, keepBackup, eraseAudit);
        Message = message;
        if (!ok) return RedirectToPage();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}
