using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // "Open staff portal" signs a technician into the portal as themselves. Leaving that behind on a shared PC would
        // let the next person raise tickets in their name, so signing out of the helpdesk signs out of the portal too.
        EduHelpdesk.Services.PortalIdentity.Clear(Response);
        return RedirectToPage("/Login");
    }
}
