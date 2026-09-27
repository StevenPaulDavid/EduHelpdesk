using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Sign-in security: whether passwords travel encrypted, the lockout rules, and two-step sign-in - required of
// everyone or left to each person, and who has it. Needs Settings: Edit, like the other pages under /Settings.
public class SignInModel(HelpdeskStore store) : PageModel
{
    public bool IsHttps => Request.IsHttps;
    public bool Required => store.RequireTwoFactor;
    public IReadOnlyList<TechnicianRecord> Staff { get; private set; } = [];
    public int RecentLockouts { get; private set; }
    public bool CanEditStaff => store.UserCan(User, Modules.StaffAccounts, ModulePermission.Edit);
    public bool CanSeeAudit => store.UserCan(User, Modules.AuditLog, ModulePermission.Access);
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        Staff = store.Technicians.Where(x => x.IsActive).OrderBy(x => x.TwoFactor is not null).ThenBy(x => x.Name).ToList();
        RecentLockouts = store.CountLockouts(DateTime.UtcNow.AddDays(-7));
    }

    public IActionResult OnPostRequire(bool required)
    {
        Message = store.SetRequireTwoFactor(required);
        return RedirectToPage();
    }
}
