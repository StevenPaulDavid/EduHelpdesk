using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The top bar's desk switcher (HelpdeskStore.DeskSwitch): saves the desk, or desks, chosen against the account, then
// goes back to the page it was pressed on. The layout takes any ?desk= off that address first, so the page opens on
// the desk just chosen rather than the one it had been asked for.
public class DeskSwitchModel(HelpdeskStore store) : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public IActionResult OnPost(string? desk, string? returnUrl)
    {
        store.ChooseWorkingDesk(User, desk);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
