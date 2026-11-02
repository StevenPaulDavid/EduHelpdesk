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
    // Person matching, the pop-up and alert rows, and technician calls (HelpdeskStore.EduNotifyLink).
    public HelpdeskStore.EduNotifyLinkView Link => store.EduNotifyLinkSettings;
    public int UnmatchedPeople => store.EduNotifyUnmatchedPeople;
    public IReadOnlyList<string> Categories => store.Categories;
    // Each event row's template: what EduNotify last offered, plus whatever a row has saved.
    public IReadOnlyList<(string Id, string Name)> EventTemplateChoices
    {
        get
        {
            var choices = (Catalogue?.Sendable ?? []).Select(x => (x.Id, x.Name)).ToList();
            foreach (var rule in Link.Events.Values.Where(x => x.TemplateId.Length > 0))
                if (choices.All(x => x.Id != rule.TemplateId)) choices.Add((rule.TemplateId, $"{rule.TemplateName} (not in EduNotify's latest list)"));
            return choices;
        }
    }
    // The reason rows: every reason EduNotify listed, then any mapped reason it no longer lists (greyed out on the page).
    public IReadOnlyList<(HelpdeskStore.EduNotifyReasonMap Row, bool Listed)> ReasonRows
    {
        get
        {
            var link = Link;
            var rows = link.KnownReasons.Select(x => (link.Reasons.FirstOrDefault(r => Same(r, x.Template, x.Reason))
                ?? new HelpdeskStore.EduNotifyReasonMap(x.Template, x.Reason, "", "", ""), true)).ToList();
            rows.AddRange(link.Reasons.Where(r => !link.KnownReasons.Any(x => Same(r, x.Template, x.Reason))).Select(r => (r, false)));
            return rows;
            static bool Same(HelpdeskStore.EduNotifyReasonMap row, string template, string reason) =>
                row.Template.Equals(template, StringComparison.OrdinalIgnoreCase) && row.Reason.Equals(reason, StringComparison.OrdinalIgnoreCase);
        }
    }
    // A key that has just been made, shown once.
    [TempData] public string? NewIncomingKey { get; set; }
    [TempData] public string? Message { get; set; }
    // Whether Message is a problem rather than good news, for the colour of the notice.
    [TempData] public bool Problem { get; set; }

    public void OnGet()
    {
        Ping = store.TechnicianPingSettings;
        var templates = (Catalogue?.Sendable ?? []).Select(x => (x.Id, x.Name)).ToList();
        if (Ping.TemplateId.Length > 0 && templates.All(x => x.Id != Ping.TemplateId)) templates.Add((Ping.TemplateId, $"{Ping.TemplateName} (not in EduNotify's latest list)"));
        PingTemplateChoices = templates;
        PingGroupChoices = (Catalogue?.Groups.Select(x => x.Name) ?? []).Concat(Ping.Groups).Concat(Ping.DeskGroups.Values.SelectMany(x => x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Unticked boxes aren't sent, so blank lists mean nothing ticked.
    // With more than one desk (HelpdeskStore.Desks), each desk's tickets ping that desk's own PCs: its boxes are
    // pinggroups-<desk id>, read here since every desk's list is posted, empty or not.
    public bool HasSeveralDesks => store.HasSeveralDesks;
    public IReadOnlyList<EduHelpdesk.Models.Desk> Desks => store.Desks;

    public IActionResult OnPostSavePing(bool enabled, string[]? priorities, string? templateId, string[]? groups)
    {
        Dictionary<Guid, IEnumerable<string>>? deskGroups = null;
        if (store.HasSeveralDesks)
            deskGroups = store.Desks.ToDictionary(x => x.Id, x => (IEnumerable<string>)Request.Form[$"pinggroups-{x.Id}"].OfType<string>().ToList());
        var (ok, message) = store.SetTechnicianPingSettings(enabled, priorities, templateId, groups, deskGroups);
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

    public IActionResult OnPostSavePeople(string? emailDomain, int takeDownHours)
    {
        var (ok, message) = store.SetEduNotifyPersonMatching(emailDomain, takeDownHours);
        Message = message;
        Problem = !ok;
        return RedirectToPage(null, null, "people");
    }

    // Each row posts on-<key>, template-<key>, desks-<key> (ticked desks) and, for the SLA warning, minutes-<key>.
    public IActionResult OnPostSaveEvents()
    {
        var rules = HelpdeskStore.EduNotifyEvents.All.ToDictionary(key => key, key => new HelpdeskStore.EduNotifyEventRule(
            Request.Form[$"on-{key}"] == "true",
            Request.Form[$"template-{key}"].ToString(),
            "",
            Request.Form[$"desks-{key}"].Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToList(),
            int.TryParse(Request.Form[$"minutes-{key}"], out var minutes) ? minutes : 60));
        var (ok, message) = store.SetEduNotifyEventRules(rules);
        Message = ok && rules.Values.Any(x => x.Enabled) && !Settings.Ready ? $"{message} Nothing is sent until EduNotify's master switch above is on." : message;
        Problem = !ok;
        return RedirectToPage(null, null, "events");
    }

    // The reason rows post reason-template, reason-reason, reason-category, reason-priority and reason-urgent, in step.
    public IActionResult OnPostSaveCalls(bool acceptCalls, bool assignOnMyWay, bool closeOnResolve, string? fallbackCategory, string? fallbackPriority, string? fallbackUrgentPriority)
    {
        string At(string field, int i) => Request.Form[field].ElementAtOrDefault(i) ?? "";
        var count = Request.Form["reason-reason"].Count;
        var reasons = Enumerable.Range(0, count).Select(i => new HelpdeskStore.EduNotifyReasonMap(At("reason-template", i), At("reason-reason", i),
            At("reason-category", i), At("reason-priority", i), At("reason-urgent", i))).ToList();
        var (ok, message) = store.SetEduNotifyCallSettings(new(acceptCalls, assignOnMyWay, closeOnResolve, reasons, fallbackCategory, fallbackPriority, fallbackUrgentPriority));
        Message = message;
        Problem = !ok;
        return RedirectToPage(null, null, "calls");
    }

    public IActionResult OnPostNewIncomingKey()
    {
        NewIncomingKey = store.NewEduNotifyIncomingKey();
        Message = "Made the incoming key. Copy it now and paste it into EduNotify under Settings → Integrations → EduHelpdesk: it isn't shown again.";
        return RedirectToPage(null, null, "calls");
    }

    public IActionResult OnPostRemoveIncomingKey()
    {
        store.RemoveEduNotifyIncomingKey();
        Message = "Removed the incoming key. EduNotify can't raise tickets until a new one is made and pasted there.";
        return RedirectToPage(null, null, "calls");
    }

    // Takes a ticket's notice down from here, for when the ticket is fine but the notice shouldn't be showing.
    public async Task<IActionResult> OnPostRecallAsync(int number, CancellationToken cancel)
    {
        if (store.StaffNoticeFor(number) is null)
        {
            Message = $"Ticket {store.Ref(number)} has no notice showing.";
            return RedirectToPage();
        }
        var outcome = await client.RecallAsync(number, cancel);
        if (outcome.Ok) store.RecordStaffNoticeRecalled(number, outcome.NothingLive ? "from Settings → EduNotify (it had already gone from EduNotify)" : "from Settings → EduNotify");
        Message = outcome.Ok ? $"The notice for {store.Ref(number)} was recalled." : $"The notice for {store.Ref(number)} couldn't be recalled. {outcome.Message}";
        Problem = !outcome.Ok;
        return RedirectToPage();
    }
}
