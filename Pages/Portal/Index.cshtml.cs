using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

public class IndexModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users.OrderBy(x => x.Name).ToList();
    public UserRecord? CurrentUser { get; private set; }
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        var id = PortalIdentity.Resolve(Request, store);
        CurrentUser = id is { } userId ? store.Users.FirstOrDefault(x => x.Id == userId) : null;
    }

    public IActionResult OnPostChoose(Guid userId)
    {
        if (!store.Users.Any(x => x.Id == userId))
        {
            Message = "Select your name from the list.";
            return RedirectToPage();
        }
        PortalIdentity.Set(Response, userId);
        return RedirectToPage();
    }

    public IActionResult OnPostSwitch()
    {
        PortalIdentity.Clear(Response);
        return RedirectToPage();
    }
}
