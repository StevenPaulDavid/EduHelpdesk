using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Signing out ends the session on the server (HelpdeskStore.Sessions), not just the cookie in this browser.
public class LogoutModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await TechnicianSession.SignOutAsync(HttpContext);
        // "Open staff portal" signs a technician into the portal as themselves. Leaving that behind on a shared PC would
        // let the next person raise tickets in their name, so signing out of the helpdesk signs out of the portal too.
        portal.SignOut(HttpContext, store);
        return RedirectToPage("/Login");
    }
}