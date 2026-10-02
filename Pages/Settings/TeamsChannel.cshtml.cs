using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings â†’ Teams channel: the webhook that posts to a Teams channel when a requester submits a ticket or a ticket goes
// overdue, which of those post, and a test button. Needs Settings: Edit, like the other pages under /Settings (Program.cs).
// The saved address is never sent back to the browser - the page shows only the host it posts to - so nothing here can leak it.
public class TeamsChannelModel(HelpdeskStore store, TeamsPoster poster) : PageModel
{
    public HelpdeskStore.TeamsSettingsView Settings => store.TeamsSettings;
    public HelpdeskStore.TeamsAttempt? LastAttempt => store.TeamsLastAttempt;
    public string SiteAddress => store.SiteAddress;
    [TempData] public string? Message { get; set; }
    // Whether Message is a problem rather than good news, for the colour of the notice.
    [TempData] public bool Problem { get; set; }

    public void OnGet() { }

    // An unticked box isn't sent, so each flag is simply whether its box was ticked. A blank address keeps the saved one.
    public IActionResult OnPostSave(string? webhook, bool enabled, bool newTicket, bool overdue)
    {
        var (ok, message) = store.SetTeamsSettings(webhook, remove: false, enabled, newTicket, overdue);
        Message = message;
        Problem = !ok;
        return RedirectToPage();
    }

    public IActionResult OnPostRemove()
    {
        var settings = store.TeamsSettings;
        var (ok, message) = store.SetTeamsSettings(null, remove: true, false, settings.NewTicket, settings.Overdue);
        Message = message;
        Problem = !ok;
        return RedirectToPage();
    }

    // Sends one message now and says how it went, whether or not posting is switched on: the point is to find out that the
    // address works before relying on it.
    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancel)
    {
        var (ok, message) = await poster.SendAsync(store.TeamsTestPost(), retry: false, cancel);
        store.RecordTeamsAttempt(ok, message);
        Message = ok ? "The test message was sent. Look in the Teams channel for it." : $"The test message didn't go. {message}";
        Problem = !ok;
        return RedirectToPage();
    }
}

