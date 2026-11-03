using System.Security.Claims;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → EduNotify. Until linked it is one box: pair with EduNotify by the code it shows (the EduNotify project's
// docs/EASY-LINK.md). Once linked: the link's health and tests, then pop-ups and alerts, technician calls, desk PCs, the
// technician ping, notices showing, and Advanced. Needs Settings: Edit, like the other pages under /Settings (Program.cs).
// No key is ever sent back to the browser.
public class EduNotifyModel(HelpdeskStore store, EduNotifyClient client) : PageModel
{
    public HelpdeskStore.EduNotifySettingsView Settings => store.EduNotifySettings;
    public EduNotify.Catalogue? Catalogue => store.EduNotifyCatalogue;
    public IReadOnlyList<HelpdeskStore.StaffNotice> Notices => store.StaffNotices;
    public HelpdeskStore.TechnicianPingSettingsView Ping => store.TechnicianPingSettings;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => store.Categories;
    public HelpdeskStore.EduNotifyLinkView Link => store.EduNotifyLinkSettings;
    public HelpdeskStore.EduNotifyHealth Health => store.EduNotifyLinkHealth;
    public bool Linked => store.EduNotifyLinked;
    public bool RepairNeeded => store.EduNotifyRepairNeeded;
    public bool HasSeveralDesks => store.HasSeveralDesks;
    public IReadOnlyList<EduHelpdesk.Models.Desk> Desks => store.Desks;

    // The pairing box's suggestions.
    public string GuessedDomain => Link.EmailDomain.Length > 0 ? Link.EmailDomain : store.GuessEmailDomain();
    public string ReachUsAt => store.SiteAddress.Length > 0 ? store.SiteAddress.TrimEnd('/') : $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

    // Templates and groups EduNotify offers (from the last fetch), plus whatever is saved, for Advanced and desk PCs.
    public IReadOnlyList<(string Id, string Name)> TemplateChoices
    {
        get
        {
            var choices = (Catalogue?.Sendable ?? []).Select(x => (x.Id, x.Name)).ToList();
            foreach (var (id, name) in new[] { (Link.PopupTemplateId, Link.PopupTemplateName), (Ping.TemplateId, Ping.TemplateName) })
                if (id.Length > 0 && choices.All(x => x.Id != id)) choices.Add((id, $"{name} (not in EduNotify's latest list)"));
            return choices;
        }
    }
    public IReadOnlyList<string> GroupChoices => (Catalogue?.Groups.Select(x => x.Name) ?? []).Concat(Ping.DeskGroups.Values.SelectMany(x => x))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    // The reason rows: every reason EduNotify listed, then any mapped reason it no longer lists (greyed out).
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

    [TempData] public string? Message { get; set; }
    // Whether Message is a problem rather than good news, for the colour of the notice.
    [TempData] public bool Problem { get; set; }

    // Opening the page fetches EduNotify's templates, groups and reasons if they're more than a minute old, so the reason
    // table is current without a button.
    public async Task OnGetAsync(CancellationToken cancel)
    {
        if (Linked && Settings.Ready && (Catalogue is null || DateTime.UtcNow - Catalogue.FetchedAt > TimeSpan.FromMinutes(1)))
            await client.RefreshAsync(cancel);
    }

    private IActionResult Done(bool ok, string message, string section)
    {
        Message = message;
        Problem = !ok;
        return RedirectToPage(null, null, section);
    }

    public async Task<IActionResult> OnPostPairAsync(string? notifyAddress, string? code, string? emailDomain, string? reachUsAt, CancellationToken cancel)
    {
        var outcome = await client.PairAsync(notifyAddress, code, emailDomain, reachUsAt, cancel);
        return Done(outcome.Ok, outcome.Message, "link");
    }

    public async Task<IActionResult> OnPostUnlinkAsync(CancellationToken cancel)
    {
        var outcome = await client.UnlinkAsync(cancel);
        return Done(outcome.Ok, outcome.Message, "link");
    }

    // ---- The tests ----

    public async Task<IActionResult> OnPostTestLinkAsync(CancellationToken cancel)
    {
        var outcome = await client.RefreshAsync(cancel);
        return Done(outcome.Ok, outcome.Ok ? $"The link works both ways. {outcome.Message}" : outcome.Message, "link");
    }

    public async Task<IActionResult> OnPostTestPopupAsync(CancellationToken cancel)
    {
        var settingsLink = store.SiteAddress.Length > 0 ? $"{store.SiteAddress.TrimEnd('/')}/Settings/EduNotify" : null;
        var outcome = await client.TestPopupAsync(HelpdeskStore.LoginFor(MyEmail(), Link.EmailDomain), settingsLink, cancel);
        return Done(outcome.Ok, outcome.Message, "link");
    }

    public async Task<IActionResult> OnPostTestCallAsync(CancellationToken cancel)
    {
        var (ok, steps) = await client.TestCallAsync(MyEmail(), cancel);
        return Done(ok, (ok ? "Test technician call worked:" : "Test technician call failed:") + "\n" +
            string.Join("\n", steps.Select(x => $"{(x.Ok ? "✓" : "✗")} {x.Step}{(x.Detail.Length > 0 ? $": {x.Detail}" : "")}")), "link");
    }

    public async Task<IActionResult> OnPostTestDesksAsync(CancellationToken cancel)
    {
        var results = await client.TestDeskPcsAsync(cancel);
        return Done(results.All(x => x.Outcome.Ok), "Desk PCs test (nothing showed on screen):\n" +
            string.Join("\n", results.Select(x => $"{(x.Outcome.Ok ? "✓" : "✗")} {x.Desk}: {x.Outcome.Message}")), "link");
    }

    // The signed-in technician's email, for the tests that go to them.
    private string? MyEmail() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? store.Technicians.FirstOrDefault(x => x.Id == id)?.Email : null;

    // ---- The settings ----

    // Each row posts on-<key>, desks-<key> (ticked desks) and, for the SLA warning, minutes-<key>. Its template is the one
    // saved (the made pop-up template, unless changed under Advanced).
    public IActionResult OnPostSaveEvents(int takeDownHours)
    {
        var link = Link;
        var rules = HelpdeskStore.EduNotifyEvents.All.ToDictionary(key => key, key => new HelpdeskStore.EduNotifyEventRule(
            Request.Form[$"on-{key}"] == "true",
            link.RuleFor(key).TemplateId,
            "",
            Request.Form[$"desks-{key}"].Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToList(),
            int.TryParse(Request.Form[$"minutes-{key}"], out var minutes) ? minutes : 60));
        var (ok, message) = store.SetEduNotifyEventRules(rules);
        if (ok && takeDownHours != link.TakeDownHours) (ok, message) = store.SetEduNotifyPersonMatching(link.EmailDomain, takeDownHours);
        return Done(ok, message, "events");
    }

    // The reason rows post reason-template, reason-reason, reason-category, reason-priority and reason-urgent, in step.
    public IActionResult OnPostSaveCalls(bool acceptCalls, bool assignOnMyWay, bool closeOnResolve, string? fallbackCategory, string? fallbackPriority, string? fallbackUrgentPriority)
    {
        string At(string field, int i) => Request.Form[field].ElementAtOrDefault(i) ?? "";
        var reasons = Enumerable.Range(0, Request.Form["reason-reason"].Count).Select(i => new HelpdeskStore.EduNotifyReasonMap(At("reason-template", i), At("reason-reason", i),
            At("reason-category", i), At("reason-priority", i), At("reason-urgent", i))).ToList();
        var (ok, message) = store.SetEduNotifyCallSettings(new(acceptCalls, assignOnMyWay, closeOnResolve, reasons, fallbackCategory, fallbackPriority, fallbackUrgentPriority));
        return Done(ok, message, "calls");
    }

    // Each desk posts deskpcs-<desk id>, its ticked groups.
    public IActionResult OnPostSaveDesks()
    {
        var groups = store.Desks.ToDictionary(x => x.Id, x => (IEnumerable<string>)Request.Form[$"deskpcs-{x.Id}"].OfType<string>().ToList());
        var (ok, message) = store.SetEduNotifyDeskPcs(groups);
        return Done(ok, message, "desks");
    }

    public IActionResult OnPostSavePing(bool enabled, string[]? priorities)
    {
        var ping = Ping;
        var (ok, message) = store.SetTechnicianPingSettings(enabled, priorities, ping.TemplateId, ping.Groups,
            ping.DeskGroups.ToDictionary(x => x.Key, x => (IEnumerable<string>)x.Value));
        return Done(ok, message, "ping");
    }

    // Advanced: the email domain, and other templates for the pop-ups and the ping than the ones EduNotify made.
    public IActionResult OnPostSaveAdvanced(string? emailDomain, string? popupTemplateId, string? pingTemplateId)
    {
        var link = Link;
        var (ok, message) = store.SetEduNotifyPersonMatching(emailDomain, link.TakeDownHours);
        if (ok && !string.IsNullOrWhiteSpace(popupTemplateId))
        {
            var rules = HelpdeskStore.EduNotifyEvents.All.ToDictionary(key => key, key => link.RuleFor(key) with { TemplateId = popupTemplateId.Trim() });
            (ok, message) = store.SetEduNotifyEventRules(rules);
        }
        if (ok && !string.IsNullOrWhiteSpace(pingTemplateId) && pingTemplateId != Ping.TemplateId)
        {
            var ping = Ping;
            (ok, message) = store.SetTechnicianPingSettings(ping.Enabled, ping.Priorities, pingTemplateId, ping.Groups,
                ping.DeskGroups.ToDictionary(x => x.Key, x => (IEnumerable<string>)x.Value));
        }
        return Done(ok, ok ? "Saved." : message, "advanced");
    }

    // Takes a ticket's notice down from here, for when the ticket is fine but the notice shouldn't be showing.
    public async Task<IActionResult> OnPostRecallAsync(int number, CancellationToken cancel)
    {
        if (store.StaffNoticeFor(number) is null) return Done(false, $"Ticket {store.Ref(number)} has no notice showing.", "notices");
        var outcome = await client.RecallAsync(number, cancel);
        if (outcome.Ok) store.RecordStaffNoticeRecalled(number, outcome.NothingLive ? "from Settings → EduNotify (it had already gone from EduNotify)" : "from Settings → EduNotify");
        return Done(outcome.Ok, outcome.Ok ? $"The notice for {store.Ref(number)} was recalled." : $"The notice for {store.Ref(number)} couldn't be recalled. {outcome.Message}", "notices");
    }
}
