using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Where a switched-off feature's pages lead (Services/Features.cs, Program.cs). Open to anyone, since staff portal
// pages can lead here too; it names the feature and nothing else.
[AllowAnonymous]
public class FeatureOffModel(HelpdeskStore store) : PageModel
{
    public Features.Feature? Feature { get; private set; }
    public bool CanSwitchOn => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public bool IsStaff => User.Identity?.IsAuthenticated == true;

    public void OnGet(string? feature)
    {
        Feature = Features.Find(feature);
        Response.StatusCode = StatusCodes.Status404NotFound;
    }
}
