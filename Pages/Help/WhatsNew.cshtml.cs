using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Help;

// What changed in each release, in plain English (wwwroot/help/whats-new.md), so staff know without asking.
public class WhatsNewModel(HelpCentre help) : PageModel
{
    public HelpMarkdown.Result Rendered { get; private set; } = null!;
    public void OnGet() => Rendered = HelpLinks.Render(Url, help.WhatsNew);
}
