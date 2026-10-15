using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → EduNotify: where EduNotify is, the API key the helpdesk sends with, the master switch, a Test button that asks
// EduNotify what the key may send, and the notices tickets have showing on staff PCs. Needs Settings: Edit, like the other
// pages under /Settings (Program.cs). The saved key is never sent back to the browser, so nothing here can leak it.
public class EduNotifyModel(HelpdeskStore store, EduNotifyClient client) : PageModel
{
    public HelpdeskStore.EduNotifySettingsView Settings => store.EduNotifySettings;
    public HelpdeskStore.EduNotifyAttempt? LastAttempt => store.EduNotifyLastAttempt;
    public EduNotify.Catalogue? Catalogue => store.EduNotifyCatalogue;
    public IReadOnlyList<HelpdeskStore.StaffNotice> Notices => store.StaffNotices;
    public HelpdeskStore.TechnicianPingSettingsView Ping { get; private set; } = null!;
    public IReadOnlyList<HelpdeskStore.TechnicianPing> Pings => store.TechnicianPings;
    public IReadOnlyList<string> Priorities => store.Priorities;
    // What EduNotify last said can be chosen, plus whatever is saved already (so the page still shows it before the list
    // has been fetched, or after EduNotify stopped offering it).
    public IReadOnlyList<(string Id, string Name)> PingTemplateChoices { get; private set; } = [];
    public IReadOnlyList<string> PingGroupChoices { get; private set; } = [];
    [TempData] public string? Message { get; set; }
    // Whether Message is a problem rather than good news, for the colour of the notice.
    [TempData] public bool Problem { get; set; }

    public void OnGet()
    {
        Ping = store.TechnicianPingSettings;
        var templates = (Catalogue?.Sendable ?? []).Select(x => (x.Id, x.Name)).ToList();
        if (Ping.TemplateId.Length > 0 && templates.All(x => x.Id != Ping.TemplateId)) templates.Add((Ping.TemplateId, $"{Ping.TemplateName} (not in EduNotify's latest list)"));
        PingTemplateChoices = templates;
        PingGroupChoices = (Catalogue?.Groups.Select(x => x.Name) ?? []).Concat(Ping.Groups).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Unticked boxes aren't sent, so blank lists mean nothing ticked.
    public IActionResult OnPostSavePing(bool enabled, string[]? priorities, string? templateId, string[]? groups)
    {
        var (ok, message) = store.SetTechnicianPingSettings(enabled, priorities, templateId, groups);
        Message = ok && enabled && !Settings.Ready ? $"{message} Nothing is sent until EduNotify's master switch above is on." : message;
        Problem = !ok;
        return RedirectToPage();
    }

    // An unticked box isn't sent, so enabled is simply whether it was ticked. A blank key keeps the saved one. A newly
    // saved key is tried straight away, so the page says at once whether EduNotify accepts it.
    public async Task<IActionResult> OnPostSaveAsync(string? address, string? apiKey, bool enabled, CancellationToken cancel)
    {
        var (ok, message) = store.SetEduNotifySettings(address, apiKey, removeKey: false, enabled);
        Message = message;
        Problem = !ok;
        if (ok && Settings is { HasAddress: true, HasKey: true } && (Catalogue is null || !string.IsNullOrWhiteSpace(apiKey)))
        {
            var test = await client.RefreshAsync(cancel);
            Message = $"Saved. {test.Message}";
            Problem = !test.Ok;
        }
        return RedirectToPage();
    }

    public IActionResult OnPostRemoveKey()
    {
        var (ok, message) = store.SetEduNotifySettings(Settings.Address, null, removeKey: true, false);
        Message = message;
        Problem = !ok;
        return RedirectToPage();
    }

    // Asks EduNotify what the key may send, whether or not sending is switched on: the point is to find out the key works
    // before anyone relies on it. Nothing is shown on any PC.
    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancel)
    {
        var outcome = await client.RefreshAsync(cancel);
        Message = outcome.Message;
        Problem = !outcome.Ok;
        return RedirectToPage();
    }

    // Takes a ticket's notice down from here, for when the ticket is fine but the notice shouldn't be showing.
    public async Task<IActionResult> OnPostRecallAsync(int number, CancellationToken cancel)
    {
        if (store.StaffNoticeFor(number) is null)
        {
            Message = $"Ticket #{number} has no notice showing.";
            return RedirectToPage();
        }
        var outcome = await client.RecallAsync(number, cancel);
        if (outcome.Ok) store.RecordStaffNoticeRecalled(number, outcome.NothingLive ? "from Settings → EduNotify (it had already gone from EduNotify)" : "from Settings → EduNotify");
        Message = outcome.Ok ? $"The notice for #{number} was recalled." : $"The notice for #{number} couldn't be recalled. {outcome.Message}";
        Problem = !outcome.Ok;
        return RedirectToPage();
    }
}
