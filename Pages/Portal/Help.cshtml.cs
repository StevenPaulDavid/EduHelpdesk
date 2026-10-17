using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

// The staff portal's help (wwwroot/help/portal.md): short and plain, for teachers and office staff rather than
// technicians. Open to anyone who can reach the portal, signed in or not, since "how do I sign in" belongs here too.
// The school's note on the helpdesk's staff-portal article heads it - who to ring for something urgent, say.
public class HelpModel(HelpdeskStore store, HelpCentre help) : PageModel
{
    public HelpMarkdown.Result Rendered { get; private set; } = null!;
    public HelpMarkdown.Result? SchoolNote { get; private set; }

    public void OnGet()
    {
        Rendered = EduHelpdesk.Pages.Help.HelpLinks.Render(Url, help.PortalHelp);
        if (store.HelpNoteFor(HelpCentre.PortalArticle) is { } note) SchoolNote = EduHelpdesk.Pages.Help.HelpLinks.Render(Url, note.Text);
    }
}
