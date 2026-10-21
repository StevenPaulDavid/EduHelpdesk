using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Sign-in security: whether passwords travel encrypted, the lockout rules, and two-step sign-in - required of
// everyone or left to each person, and who has it - plus the helpdesk's address and the EduDashboard allowed to show it
// inside itself. Needs Settings: Edit, like the other pages under /Settings.
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

    public bool RecoveryKeysAllowed => store.AllowRecoveryKeys;

    public IActionResult OnPostRecoveryKeys(bool allowed)
    {
        Message = store.SetAllowRecoveryKeys(allowed);
        return RedirectToPage(null, null, "recovery");
    }

    public string SiteAddress => store.SiteAddress;
    public string GuessedAddress => People.QuickStartModel.GuessAddress(Request);

    public IActionResult OnPostAddress(string? address)
    {
        Message = store.SetSiteAddress(address).Message;
        return RedirectToPage(null, null, "address");
    }

    public string DashboardAddress => store.DashboardAddress;

    // What will stop the dashboard showing the helpdesk even with the address set, worked out from how this page was
    // reached.
    public IEnumerable<string> DashboardWarnings
    {
        get
        {
            if (!Uri.TryCreate(DashboardAddress, UriKind.Absolute, out var dashboard)) yield break;
            if (dashboard.Scheme == Uri.UriSchemeHttps && !Request.IsHttps)
                yield return "The dashboard is on https:// and this page came over plain http://, so browsers will block the helpdesk inside the dashboard until the helpdesk has HTTPS too.";
            static string Site(string host) => host.Split('.') is { Length: >= 2 } parts && !System.Net.IPAddress.TryParse(host, out _) ? string.Join('.', parts[^2..]) : host;
            if (!string.Equals(Site(dashboard.Host), Site(Request.Host.Host), StringComparison.OrdinalIgnoreCase))
                yield return $"The dashboard ({dashboard.Host}) is on a different domain from this page ({Request.Host.Host}), so browsers may not keep technicians signed in inside it.";
        }
    }

    public IActionResult OnPostDashboard(string? address)
    {
        Message = store.SetDashboardAddress(address).Message;
        return RedirectToPage(null, null, "dashboard");
    }
}
