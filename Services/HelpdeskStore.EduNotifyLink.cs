using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// What EduNotify does for the helpdesk (Settings → EduNotify), beyond outage notices and the technician ping. Specced in
// the EduNotify project's docs/HELPDESK-EVENTS.md and docs/TECH-CALL.md:
//
// - Person matching: staff emails at the school's domain are their Windows logins (jsmith@school.org.uk is jsmith), so a
//   pop-up can go to one person on whichever PC they are logged on to.
// - Requester pop-ups and technician alerts: a fixed list of events, each switched on with a template and the desks it
//   applies to. A pop-up says only the ticket number and what happened (classroom screens get projected), with an Open
//   ticket button, and a newer one replaces the older on that PC. Someone not logged on misses it: it is not retried.
// - Technician calls: EduNotify's "Call technician" raises a ticket here through EduNotifyCallEndpoints. The reason the
//   teacher picked decides the category and priority; the first technician to press On my way is assigned; resolving
//   the ticket closes the call.
//
// Like the ping, the events are worked out by EduNotifyClient calling PlanEduNotifyEvents every few seconds, which compares
// the tickets with how they were last time (so every way of assigning or closing one is caught, wherever it happened), and
// reads the replies the browser notifications are told about.
public sealed partial class HelpdeskStore
{
    // The events, in the order the settings page lists them.
    public static class EduNotifyEvents
    {
        public const string Logged = "requester.logged";
        public const string Assigned = "requester.assigned";
        public const string StaffReplied = "requester.replied";
        public const string Resolved = "requester.resolved";
        public const string LoanDue = "requester.loan";
        public const string AssignedToYou = "tech.assigned";
        public const string SlaWarning = "tech.slaWarning";
        public const string SlaBreached = "tech.slaBreached";
        public const string RequesterReplied = "tech.requesterReplied";
        public const string Reopened = "tech.reopened";
        public const string CheckFailed = "tech.checkFailed";

        public static readonly IReadOnlyList<string> Requester = [Logged, Assigned, StaffReplied, Resolved, LoanDue];
        public static readonly IReadOnlyList<string> Technician = [AssignedToYou, SlaWarning, SlaBreached, RequesterReplied, Reopened, CheckFailed];
        public static readonly IReadOnlyList<string> All = [.. Requester, .. Technician];

        public static string Label(string key) => key switch
        {
            Logged => "Ticket logged",
            Assigned => "Ticket assigned",
            StaffReplied => "Technician replied",
            Resolved => "Ticket resolved",
            LoanDue => "Loan due back",
            AssignedToYou => "Assigned to you",
            SlaWarning => "SLA warning",
            SlaBreached => "SLA breached",
            RequesterReplied => "Requester replied",
            Reopened => "Ticket reopened",
            CheckFailed => "Premises check failed",
            _ => key
        };

        public static string When(string key) => key switch
        {
            Logged => "A ticket is made with them as the requester. Not for technician calls from EduNotify.",
            Assigned => "Someone is assigned, or the assignee changes.",
            StaffReplied => "A reply with Notify the requester ticked.",
            Resolved => "The ticket is resolved or closed.",
            LoanDue => "On the due date and while overdue, once a day at the start of the school day.",
            AssignedToYou => "To the new assignee, unless they assigned it to themselves.",
            SlaWarning => "To the assignee (else the desk's PCs) when a ticket is this many minutes from its due time.",
            SlaBreached => "To the assignee (else the desk's PCs) when the due time passes.",
            RequesterReplied => "To the assignee (else the desk's PCs) when the requester replies in the portal.",
            Reopened => "To the assignee (else the desk's PCs) when a closed ticket reopens.",
            CheckFailed => "To the desk's PCs when a premises check is saved with a fail.",
            _ => ""
        };

        public static bool HasDesks(string key) => key != LoanDue;
        public static bool HasMinutes(string key) => key == SlaWarning;
    }

    // One event's row. DeskIds empty means every desk. Minutes is only for the SLA warning.
    public sealed record EduNotifyEventRule(bool Enabled, string TemplateId, string TemplateName, List<Guid> DeskIds, int Minutes = 60);

    // A reason EduNotify's ticket-raising on-call templates offer, mapped to how its tickets are logged here. Blank category
    // or priorities use the fallback.
    public sealed record EduNotifyReasonMap(string Template, string Reason, string Category, string Priority, string UrgentPriority);

    // A reason EduNotify last listed (GET /api/v1/oncall/reasons), so the page can offer a row for each.
    public sealed record EduNotifyKnownReason(string Template, string Reason);

    // A ticket a technician call raised: the call's id in EduNotify (so a retry never makes two), and whether EduNotify has
    // been told it was resolved.
    public sealed record EduNotifyCallTicket(string ExternalId, int TicketNumber, DateTime RaisedAt, string RaisedBy)
    {
        public bool CloseSent { get; init; }
    }

    // A pop-up EduNotify is showing, so it can be taken down after the set time.
    public sealed record EduNotifyPopup(string Reference, int? TicketNumber, DateTime SentAt);

    // How a ticket stood at the last look, to spot what changed.
    public sealed record EduNotifyTicketState(Guid? TechnicianId, bool Closed);

    public sealed class EduNotifyLinkData
    {
        public string EmailDomain { get; set; } = "";
        public int TakeDownHours { get; set; } = 8;
        public Dictionary<string, EduNotifyEventRule> Events { get; set; } = [];

        public bool AcceptCalls { get; set; }
        public bool AssignOnMyWay { get; set; } = true;
        public bool CloseOnResolve { get; set; } = true;
        public string IncomingKeyHash { get; set; } = "";
        public string IncomingKeyPrefix { get; set; } = "";
        public DateTime? IncomingKeyLastUsed { get; set; }
        public List<EduNotifyReasonMap> Reasons { get; set; } = [];
        public string FallbackCategory { get; set; } = "";
        public string FallbackPriority { get; set; } = "";
        public string FallbackUrgentPriority { get; set; } = "";
        public List<EduNotifyKnownReason> KnownReasons { get; set; } = [];
        public DateTime? KnownReasonsAt { get; set; }

        public List<EduNotifyCallTicket> Calls { get; set; } = [];
        public List<EduNotifyPopup> Popups { get; set; } = [];
        public Dictionary<int, EduNotifyTicketState> Seen { get; set; } = [];
        public DateTime? LastScan { get; set; }
        public DateOnly? LoansRemindedOn { get; set; }
    }

    // The requester tickets from technician calls are logged against when EduNotify's login matches nobody here.
    public const string CallRequesterName = "EduNotify technician calls";
    public const string IncomingKeyStart = "ehk_";
    public const int MaxTakeDownHours = 72;
    // The longest gap a pass catches up on after the helpdesk was stopped: anything older is not news any more.
    private static readonly TimeSpan MaxCatchUp = TimeSpan.FromMinutes(10);

    // Replies the browser notifications were told about, waiting for the next pass (HelpdeskStore.Notifications). Only in
    // memory: after a restart they are stale anyway.
    private readonly List<(string Kind, int TicketNumber, Guid? RecipientId)> _eduNotifyReplies = [];
    private readonly Lock _eduNotifyRepliesSync = new();

    private EduNotifyLinkData Link => _data.EduNotifyLink;

    // ---- Settings ----

    public sealed record EduNotifyLinkView(string EmailDomain, int TakeDownHours, IReadOnlyDictionary<string, EduNotifyEventRule> Events,
        bool AcceptCalls, bool AssignOnMyWay, bool CloseOnResolve, bool HasIncomingKey, string IncomingKeyPrefix, DateTime? IncomingKeyLastUsed,
        IReadOnlyList<EduNotifyReasonMap> Reasons, string FallbackCategory, string FallbackPriority, string FallbackUrgentPriority,
        IReadOnlyList<EduNotifyKnownReason> KnownReasons, DateTime? KnownReasonsAt, IReadOnlyList<EduNotifyCallTicket> Calls)
    {
        public EduNotifyEventRule RuleFor(string key) => Events.GetValueOrDefault(key) ?? new(false, "", "", []);
    }

    public EduNotifyLinkView EduNotifyLinkSettings
    {
        get
        {
            lock (_sync)
            {
                var link = Link;
                return new(link.EmailDomain, link.TakeDownHours, new Dictionary<string, EduNotifyEventRule>(link.Events), link.AcceptCalls,
                    link.AssignOnMyWay, link.CloseOnResolve, link.IncomingKeyHash.Length > 0, link.IncomingKeyPrefix, link.IncomingKeyLastUsed,
                    link.Reasons.ToList(), link.FallbackCategory, link.FallbackPriority, link.FallbackUrgentPriority, link.KnownReasons.ToList(),
                    link.KnownReasonsAt, link.Calls.OrderByDescending(x => x.RaisedAt).Take(20).ToList());
            }
        }
    }

    // "school.org.uk" from "@School.org.uk". Null for something that isn't a domain; "" for blank.
    public static string? CleanEmailDomain(string? text)
    {
        var value = (text ?? "").Trim().TrimStart('@').ToLowerInvariant();
        if (value.Length == 0) return "";
        return value.Length <= 200 && value.Contains('.') && Uri.CheckHostName(value) == UriHostNameType.Dns ? value : null;
    }

    // jsmith@school.org.uk is jsmith, when school.org.uk is the domain. Null for anyone else.
    public static string? LoginFor(string? email, string domain)
    {
        if (string.IsNullOrWhiteSpace(email) || domain.Length == 0) return null;
        var at = email.Trim().LastIndexOf('@');
        if (at <= 0 || !email.Trim()[(at + 1)..].Equals(domain, StringComparison.OrdinalIgnoreCase)) return null;
        var login = email.Trim()[..at].ToLowerInvariant();
        return login.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_') ? login : null;
    }

    public (bool Ok, string Message) SetEduNotifyPersonMatching(string? emailDomain, int takeDownHours)
    {
        if (CleanEmailDomain(emailDomain) is not { } domain) return (false, "That isn't an email domain. Enter the part after the @ in staff emails, e.g. school.org.uk.");
        if (takeDownHours is < 1 or > MaxTakeDownHours) return (false, $"Take pop-ups down after between 1 and {MaxTakeDownHours} hours.");
        lock (_sync)
        {
            if (Link.EmailDomain == domain && Link.TakeDownHours == takeDownHours) return (true, "Nothing changed.");
            Link.EmailDomain = domain;
            Link.TakeDownHours = takeDownHours;
            var matched = domain.Length == 0 ? 0 : _data.Users.Count(x => x.IsActive && LoginFor(x.Email, domain) is not null)
                + _data.Technicians.Count(x => x.IsActive && LoginFor(x.Email, domain) is not null);
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "EduNotify", "Person matching",
                $"Email domain {(domain.Length == 0 ? "none" : domain)}; pop-ups taken down after {takeDownHours} hours."));
            Save();
            return (true, domain.Length == 0 ? "Saved. With no domain, nobody can be sent a pop-up of their own."
                : $"Saved. {matched} active {(matched == 1 ? "person has" : "people have")} an email at {domain}.");
        }
    }

    // How many active people can't be sent anything personal, for the page.
    public int EduNotifyUnmatchedPeople
    {
        get
        {
            lock (_sync)
            {
                var domain = Link.EmailDomain;
                return domain.Length == 0 ? 0 : _data.Users.Count(x => x.IsActive && x.AnonymisedAt is null && LoginFor(x.Email, domain) is null);
            }
        }
    }

    // Each row's template must be one EduNotify last said the key may send (or the one already saved).
    public (bool Ok, string Message) SetEduNotifyEventRules(IReadOnlyDictionary<string, EduNotifyEventRule> rules)
    {
        lock (_sync)
        {
            var catalogue = EduNotifyCatalogue;
            var desks = _data.Desks.Select(x => x.Id).ToHashSet();
            var next = new Dictionary<string, EduNotifyEventRule>();
            foreach (var key in EduNotifyEvents.All)
            {
                var asked = rules.GetValueOrDefault(key) ?? new(false, "", "", []);
                var before = Link.Events.GetValueOrDefault(key);
                var id = asked.TemplateId.Trim();
                var name = "";
                if (id.Length > 0)
                {
                    if (catalogue?.Sendable.FirstOrDefault(x => x.Id == id) is { } template) name = template.Name;
                    else if (before is not null && before.TemplateId == id) name = before.TemplateName;
                    else return (false, $"{EduNotifyEvents.Label(key)}: that template isn't one the key may send. Use Test the connection to fetch the list again.");
                }
                if (asked.Enabled && id.Length == 0) return (false, $"{EduNotifyEvents.Label(key)}: choose a template to switch it on.");
                var minutes = EduNotifyEvents.HasMinutes(key) ? asked.Minutes : 60;
                if (minutes is < 5 or > 1440) return (false, $"{EduNotifyEvents.Label(key)}: the warning can be between 5 and 1440 minutes before the due time.");
                next[key] = new(asked.Enabled, id, name, asked.DeskIds.Where(desks.Contains).Distinct().ToList(), minutes);
            }

            Link.Events = next;
            var on = next.Where(x => x.Value.Enabled).Select(x => EduNotifyEvents.Label(x.Key)).ToList();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "EduNotify", "Pop-ups and alerts",
                on.Count == 0 ? "All off." : $"On: {string.Join(", ", on)}."));
            Save();
            return (true, on.Count == 0 ? "Saved. No pop-ups are switched on." : $"Saved. {on.Count} switched on.");
        }
    }

    public sealed record EduNotifyCallSettingsInput(bool AcceptCalls, bool AssignOnMyWay, bool CloseOnResolve,
        IReadOnlyList<EduNotifyReasonMap> Reasons, string? FallbackCategory, string? FallbackPriority, string? FallbackUrgentPriority);

    public (bool Ok, string Message) SetEduNotifyCallSettings(EduNotifyCallSettingsInput input)
    {
        lock (_sync)
        {
            string? Category(string? text) => string.IsNullOrWhiteSpace(text) ? "" : _data.Categories.FirstOrDefault(x => x.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
            string? Priority(string? text) => string.IsNullOrWhiteSpace(text) ? "" : _data.Priorities.FirstOrDefault(x => x.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));

            var fallbackCategory = Category(input.FallbackCategory);
            var fallbackPriority = Priority(input.FallbackPriority);
            var fallbackUrgent = Priority(input.FallbackUrgentPriority);
            if (fallbackCategory is null || fallbackPriority is null || fallbackUrgent is null) return (false, "Choose the unknown reasons' category and priorities from the lists.");
            if (input.AcceptCalls && (fallbackCategory.Length == 0 || fallbackPriority.Length == 0))
                return (false, "To accept calls, choose a category and priority for unknown reasons: every call needs somewhere to go.");

            var reasons = new List<EduNotifyReasonMap>();
            foreach (var row in input.Reasons.Where(x => x.Reason.Trim().Length > 0 || x.Template.Trim().Length > 0))
            {
                var category = Category(row.Category);
                var priority = Priority(row.Priority);
                var urgent = Priority(row.UrgentPriority);
                if (category is null || priority is null || urgent is null) return (false, $"{row.Template}: {row.Reason}: choose its category and priorities from the lists.");
                if (category.Length + priority.Length + urgent.Length == 0) continue; // all blank is the same as the fallback
                reasons.Add(new(row.Template.Trim(), row.Reason.Trim(), category, priority, urgent));
            }

            var link = Link;
            link.AcceptCalls = input.AcceptCalls;
            link.AssignOnMyWay = input.AssignOnMyWay;
            link.CloseOnResolve = input.CloseOnResolve;
            link.Reasons = reasons;
            link.FallbackCategory = fallbackCategory;
            link.FallbackPriority = fallbackPriority;
            link.FallbackUrgentPriority = fallbackUrgent;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "EduNotify", "Technician calls",
                $"Accept calls {(input.AcceptCalls ? "on" : "off")}; assign on On my way {(input.AssignOnMyWay ? "on" : "off")}; close the call when resolved {(input.CloseOnResolve ? "on" : "off")}; "
                + $"{reasons.Count} reason{(reasons.Count == 1 ? "" : "s")} mapped; unknown reasons to {(fallbackCategory.Length == 0 ? "nowhere" : fallbackCategory)}."));
            Save();
            return (true, input.AcceptCalls && link.IncomingKeyHash.Length == 0
                ? "Saved. Make the incoming key below and paste it into EduNotify, or no calls can reach the helpdesk."
                : "Saved.");
        }
    }

    // A new key for EduNotify to raise tickets with: shown once, kept as its hash, and the old one stops working.
    public string NewEduNotifyIncomingKey()
    {
        var key = IncomingKeyStart + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (_sync)
        {
            var replaced = Link.IncomingKeyHash.Length > 0;
            Link.IncomingKeyHash = KeyHash(key);
            Link.IncomingKeyPrefix = key[..(IncomingKeyStart.Length + 6)];
            Link.IncomingKeyLastUsed = null;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "EduNotify", replaced ? "Incoming key replaced" : "Incoming key made",
                "The key EduNotify raises technician-call tickets with. The key itself is never recorded."));
            Save();
        }
        return key;
    }

    public void RemoveEduNotifyIncomingKey()
    {
        lock (_sync)
        {
            if (Link.IncomingKeyHash.Length == 0) return;
            Link.IncomingKeyHash = "";
            Link.IncomingKeyPrefix = "";
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "EduNotify", "Incoming key removed", "EduNotify can no longer raise tickets."));
            Save();
        }
    }

    // Whether a bearer key is the incoming one. Compared as hashes in fixed time. Notes when it was last used (not saved
    // straight away: the next save carries it).
    public bool CheckEduNotifyIncomingKey(string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented)) return false;
        lock (_sync)
        {
            var stored = Link.IncomingKeyHash;
            if (stored.Length == 0) return false;
            var ok = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(KeyHash(presented.Trim())), Encoding.ASCII.GetBytes(stored));
            if (ok) Link.IncomingKeyLastUsed = DateTime.UtcNow;
            return ok;
        }
    }

    private static string KeyHash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public bool EduNotifyAcceptsCalls { get { lock (_sync) return Link.AcceptCalls; } }

    // What EduNotify listed (EduNotifyClient, on each Test and every ten minutes).
    public void SetEduNotifyKnownReasons(IReadOnlyList<EduNotifyKnownReason> reasons)
    {
        lock (_sync)
        {
            var changed = !reasons.SequenceEqual(Link.KnownReasons);
            Link.KnownReasons = reasons.ToList();
            Link.KnownReasonsAt = DateTime.UtcNow;
            if (changed) Save();
        }
    }

    // ---- Technician calls: raising and assigning (EduNotifyCallEndpoints) ----

    public sealed record CallTicketRequest(string? ExternalId, string? Subject, string? Description, string? RequesterEmail, string? RequesterName,
        string? Template, string? Reason, bool Urgent, string? Location);

    // Status is the HTTP status to answer with: 201 made, 200 made before (same call), 400 bad request, 503 not accepting.
    public sealed record CallTicketResult(int Status, string? Ref = null, int? Number = null, string? Error = null);

    public CallTicketResult RaiseCallTicket(CallTicketRequest request)
    {
        var externalId = (request.ExternalId ?? "").Trim();
        if (externalId.Length is 0 or > 100) return new(400, Error: "Send the call's externalId (up to 100 characters).");
        if (string.IsNullOrWhiteSpace(request.Subject)) return new(400, Error: "Send a subject.");
        lock (_sync)
        {
            var link = Link;
            if (link.Calls.FirstOrDefault(x => x.ExternalId == externalId) is { } existing && _data.Tickets.Any(x => x.Number == existing.TicketNumber))
                return new(200, RefCore(existing.TicketNumber), existing.TicketNumber);
            if (!link.AcceptCalls) return new(503, Error: "EduHelpdesk is not accepting calls from EduNotify.");

            var template = (request.Template ?? "").Trim();
            var reason = (request.Reason ?? "").Trim();
            var map = link.Reasons.FirstOrDefault(x => x.Template.Equals(template, StringComparison.OrdinalIgnoreCase) && x.Reason.Equals(reason, StringComparison.OrdinalIgnoreCase))
                ?? link.Reasons.FirstOrDefault(x => x.Template.Length == 0 && x.Reason.Equals(reason, StringComparison.OrdinalIgnoreCase));
            string Pick(string? chosen, string fallback, List<string> allowed) =>
                allowed.FirstOrDefault(x => x.Equals(chosen, StringComparison.OrdinalIgnoreCase))
                ?? allowed.FirstOrDefault(x => x.Equals(fallback, StringComparison.OrdinalIgnoreCase))
                ?? allowed.FirstOrDefault() ?? "";
            var category = Pick(map?.Category, link.FallbackCategory, _data.Categories);
            // Urgent uses the reason's urgent priority, else the fallback's urgent one, else the normal one.
            var normal = map is { Priority.Length: > 0 } ? map.Priority : link.FallbackPriority;
            var urgent = map is { UrgentPriority.Length: > 0 } ? map.UrgentPriority : link.FallbackUrgentPriority.Length > 0 ? link.FallbackUrgentPriority : normal;
            var priority = Pick(request.Urgent ? urgent : normal, link.FallbackPriority, _data.Priorities);
            if (category.Length == 0 || priority.Length == 0) return new(503, Error: "EduHelpdesk has no category or priority to log calls under.");

            var email = (request.RequesterEmail ?? "").Trim();
            var requester = email.Length == 0 ? null
                : _data.Users.FirstOrDefault(x => x.IsActive && x.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            var name = Clip((request.RequesterName ?? "").Trim(), 120);
            var description = Clip((request.Description ?? "").Trim(), MaxTicketTextLength - 300);
            if (requester is null)
            {
                requester = CallRequesterCore();
                description += $"\n\nRequester not matched to anyone here: {(name.Length > 0 ? name : "unknown")}{(email.Length > 0 ? $" ({email})" : "")}.";
            }

            var location = (request.Location ?? "").Trim();
            var knownLocation = _data.Locations.FirstOrDefault(x => x.Equals(location, StringComparison.OrdinalIgnoreCase));
            var now = DateTime.UtcNow;
            var sla = SlaFor(priority, category);
            var desk = DeskForCategoryCore(category);
            var how = $"Raised from EduNotify by {(name.Length > 0 ? name : "a member of staff")}" + (template.Length > 0 ? $" with {template}" : "") + (reason.Length > 0 ? $": {reason}" : "") + (request.Urgent ? " (urgent)" : "") + ".";
            var number = AddTicketCore(new TicketRecord(0, Clip(request.Subject!.Trim(), MaxTicketTitleLength), description.Trim(), requester.Id, [], null,
                priority, StartingStatusCore(desk.Id), category, now, null, sla, CalculateDueDate(sla, now, category),
                Location: knownLocation ?? (location.Length > 0 ? Clip(location, 100) : null)) { Type = TicketTypes.Incident }, how);
            link.Calls.Add(new(externalId, number, now, name));
            // Kept to the last 500 calls: older ones are long resolved, and EduNotify has stopped retrying them.
            if (link.Calls.Count > 500) link.Calls.RemoveRange(0, link.Calls.Count - 500);
            Save();
            return new(201, RefCore(number), number);
        }
    }

    // Status: 200 assigned (or ignored, or someone already had it), 404 no such call ticket, 409 no such technician.
    public sealed record CallAssignResult(int Status, string? AssignedTo = null, bool Ignored = false, string? Error = null);

    public CallAssignResult AssignCallTicket(string? reference, string? email)
    {
        TicketRecord ticket;
        TechnicianRecord technician;
        lock (_sync)
        {
            var (number, _) = FindTicketByRef(reference);
            if (number is not { } found || Link.Calls.All(x => x.TicketNumber != found) || _data.Tickets.FirstOrDefault(x => x.Number == found) is not { } current)
                return new(404, Error: $"No ticket {reference} came from a technician call.");
            if (!Link.AssignOnMyWay) return new(200, Ignored: true);
            if (current.TechnicianId is { } already)
                return new(200, _data.Technicians.FirstOrDefault(x => x.Id == already)?.Name ?? "someone else");
            if (_data.Technicians.FirstOrDefault(x => x.IsActive && x.Email.Equals((email ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) is not { } tech)
                return new(409, Error: $"EduHelpdesk has no technician with the email {email}.");
            ticket = current;
            technician = tech;
        }
        UpdateTicket(ticket with { TechnicianId = technician.Id });
        AddEduNotifyHistory(ticket.Number, "On my way in EduNotify", $"{technician.Name} pressed On my way on the call, so they were assigned.");
        return new(200, technician.Name);
    }

    private void AddEduNotifyHistory(int number, string action, string details)
    {
        lock (_sync)
        {
            AddStaffNoticeHistory(number, action, details);
            Save();
        }
    }

    // Called with the lock held. The requester for calls from someone EduNotify's login matches nobody here, made once.
    private UserRecord CallRequesterCore()
    {
        if (_data.Users.FirstOrDefault(x => x.Name == CallRequesterName && x.Email.Length == 0) is { } existing) return existing;
        var user = new UserRecord(Guid.NewGuid(), CallRequesterName, "", "", "") { PersonType = "" };
        _data.Users.Add(WithLeaverDates(user, null));
        return user;
    }

    // Called with the lock held. Tickets from technician calls never ping (EduNotify has already alerted the technicians)
    // and aren't "logged" news for the requester.
    private bool IsCallTicket(int number) => Link.Calls.Any(x => x.TicketNumber == number);

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();

    // ---- The events ----

    // Called by AddNotification for every reply, before its own switches are looked at: EduNotify's settings are its own.
    private void NoteReplyForEduNotify(string kind, int? ticketNumber, Guid? recipientId)
    {
        if (ticketNumber is not { } number || kind is not (NotificationKinds.Reply or NotificationKinds.StaffComment)) return;
        lock (_eduNotifyRepliesSync)
        {
            _eduNotifyReplies.Add((kind, number, recipientId));
            if (_eduNotifyReplies.Count > 1000) _eduNotifyReplies.RemoveRange(0, _eduNotifyReplies.Count - 1000);
        }
    }

    // One pass's work for EduNotifyClient: pop-ups to send, ones to take down (by reference), and technician calls to tell
    // EduNotify are resolved (by ticket ref). Personal pop-ups go to the person's login, group ones to their desk's PCs.
    public sealed record EduNotifyEventSend(string Reference, string EventKey, int? TicketNumber, EduNotify.Send Send);
    public sealed record EduNotifyEventPlan(IReadOnlyList<EduNotifyEventSend> Send, IReadOnlyList<string> Recall, IReadOnlyList<(int Number, string Ref)> CloseCalls);

    public EduNotifyEventPlan PlanEduNotifyEvents(bool sending, DateTime? at = null)
    {
        List<(string Kind, int TicketNumber, Guid? RecipientId)> replies;
        lock (_eduNotifyRepliesSync)
        {
            replies = _eduNotifyReplies.ToList();
            _eduNotifyReplies.Clear();
        }

        lock (_sync)
        {
            var now = at ?? DateTime.UtcNow;
            var link = Link;
            var sends = new List<EduNotifyEventSend>();
            var recall = new List<string>();
            var closeCalls = new List<(int, string)>();
            var since = link.LastScan is { } last && now - last < MaxCatchUp ? last : now - MaxCatchUp;
            var first = link.LastScan is null;
            var changed = false;

            var tickets = _data.Tickets.ToDictionary(x => x.Number);
            foreach (var ticket in _data.Tickets)
            {
                var closed = TicketInsights.IsClosed(ticket);
                var state = new EduNotifyTicketState(ticket.TechnicianId, closed);
                var known = link.Seen.TryGetValue(ticket.Number, out var before);
                if (!known || before != state) { link.Seen[ticket.Number] = state; changed = true; }
                // The first pass (switched on, or after an upgrade) only learns how things stand.
                if (first || !sending) continue;

                if (!known)
                {
                    if (ticket.CreatedAt >= since && !IsCallTicket(ticket.Number)) Personal(EduNotifyEvents.Logged, ticket, RequesterLogin(ticket), $"{RefCore(ticket)}: logged", "We've got it. You'll hear when someone picks it up.");
                    if (ticket.TechnicianId is not null && ticket.CreatedAt >= since) Assigned(ticket);
                    continue;
                }
                if (before!.TechnicianId != ticket.TechnicianId && ticket.TechnicianId is not null && !closed) Assigned(ticket);
                if (!before.Closed && closed)
                {
                    Personal(EduNotifyEvents.Resolved, ticket, RequesterLogin(ticket), $"{RefCore(ticket)}: resolved", "Open it to reopen it if it isn't fixed.");
                    recall.Add(DeskReference(ticket.Number));
                }
                if (before.Closed && !closed) ToAssigneeOrDesk(EduNotifyEvents.Reopened, ticket, $"{RefCore(ticket)}: reopened", "It's back open.");
            }
            foreach (var gone in link.Seen.Keys.Where(x => !tickets.ContainsKey(x)).ToList()) { link.Seen.Remove(gone); changed = true; }

            if (sending && !first)
            {
                foreach (var (kind, number, _) in replies.DistinctBy(x => (x.Kind, x.TicketNumber)))
                {
                    if (!tickets.TryGetValue(number, out var ticket)) continue;
                    if (kind == NotificationKinds.StaffComment)
                        Personal(EduNotifyEvents.StaffReplied, ticket, RequesterLogin(ticket), $"{RefCore(ticket)}: {TechnicianName(ticket) ?? "a technician"} replied", "Open it to read the reply.");
                    else ToAssigneeOrDesk(EduNotifyEvents.RequesterReplied, ticket, $"{RefCore(ticket)}: requester replied", "Open it to read the reply.");
                }

                // SLAs: crossed since the last look, so each one alerts once and a restart doesn't repeat them.
                var warning = Rule(EduNotifyEvents.SlaWarning);
                foreach (var ticket in _data.Tickets.Where(x => !TicketInsights.IsClosed(x) && !x.IsSlaPaused && x.DueDate is not null))
                {
                    var due = ticket.DueDate!.Value;
                    if (due > since && due <= now) ToAssigneeOrDesk(EduNotifyEvents.SlaBreached, ticket, $"{RefCore(ticket)}: overdue", "Its due time has passed.");
                    else if (warning is not null && due > now && due - TimeSpan.FromMinutes(warning.Minutes) > since && due - TimeSpan.FromMinutes(warning.Minutes) <= now)
                        ToAssigneeOrDesk(EduNotifyEvents.SlaWarning, ticket, $"{RefCore(ticket)}: due in {Minutes(warning.Minutes)}", "It's nearly due.");
                }

                // Premises checks saved with a fail since the last look: one alert per ticket, to its desk.
                foreach (var failed in _data.CheckLines.Where(x => x.Failed && x.AnsweredAt is { } answered && answered > since && answered <= now).GroupBy(x => x.TicketNumber))
                {
                    if (!tickets.TryGetValue(failed.Key, out var ticket)) continue;
                    Group(EduNotifyEvents.CheckFailed, ticket, $"Premises check failed: {failed.First().ScheduleName}", $"{failed.Count()} item{(failed.Count() == 1 ? "" : "s")} failed on {RefCore(ticket)}.");
                }

                LoanReminders(now);
            }

            // Technician calls whose ticket is now resolved: tell EduNotify, so the alert comes down.
            if (link.CloseOnResolve)
                foreach (var call in link.Calls.Where(x => !x.CloseSent))
                    if (!tickets.TryGetValue(call.TicketNumber, out var ticket) || TicketInsights.IsClosed(ticket))
                        closeCalls.Add((call.TicketNumber, ticket is null ? $"#{call.TicketNumber}" : RefCore(ticket)));

            // Pop-ups past their time come down.
            var cutoff = now.AddHours(-link.TakeDownHours);
            recall.AddRange(link.Popups.Where(x => x.SentAt < cutoff).Select(x => x.Reference));

            // Saved only when something changed: a save rewrites the database, and this runs every few seconds. After a
            // restart the scan picks up from the last save, at most MaxCatchUp ago.
            link.LastScan = now;
            if (changed || first) Save();
            return new(sends, recall.Distinct().ToList(), closeCalls);

            EduNotifyEventRule? Rule(string key) =>
                link.Events.GetValueOrDefault(key) is { Enabled: true, TemplateId.Length: > 0 } rule ? rule : null;

            bool OnDesk(EduNotifyEventRule rule, TicketRecord ticket) =>
                rule.DeskIds.Count == 0 || rule.DeskIds.Contains(DeskForCategoryCore(ticket.Category).Id);

            void Personal(string key, TicketRecord ticket, string? login, string title, string body)
            {
                if (Rule(key) is not { } rule || login is null || !OnDesk(rule, ticket)) return;
                var requester = key is EduNotifyEvents.Logged or EduNotifyEvents.Assigned or EduNotifyEvents.StaffReplied or EduNotifyEvents.Resolved;
                sends.Add(new(PersonalReference(ticket.Number, login), key, ticket.Number,
                    new EduNotify.Send(rule.TemplateId, false, [], title, body, PersonalReference(ticket.Number, login), [login], "Open ticket",
                        requester ? PortalTicketLink(ticket.Number) : TicketLink(ticket.Number, null))));
            }

            void Group(string key, TicketRecord ticket, string title, string body)
            {
                if (Rule(key) is not { } rule || !OnDesk(rule, ticket)) return;
                if (PingGroupsFor(DeskForCategoryCore(ticket.Category).Id) is not { Count: > 0 } groups) return;
                sends.Add(new(DeskReference(ticket.Number), key, ticket.Number,
                    new EduNotify.Send(rule.TemplateId, false, groups.ToList(), title, body, DeskReference(ticket.Number), null, "Open ticket", TicketLink(ticket.Number, null))));
            }

            void ToAssigneeOrDesk(string key, TicketRecord ticket, string title, string body)
            {
                if (TechnicianLogin(ticket) is { } login) Personal(key, ticket, login, title, body);
                else if (ticket.TechnicianId is null) Group(key, ticket, title, body);
            }

            void Assigned(TicketRecord ticket)
            {
                // A technician call's teacher and technician already know from EduNotify ("on their way", On my way).
                if (IsCallTicket(ticket.Number)) return;
                var name = TechnicianName(ticket) ?? "a technician";
                Personal(EduNotifyEvents.Assigned, ticket, RequesterLogin(ticket), $"{RefCore(ticket)}: {name} is dealing with it", "Open it to see how it's going.");
                // Not when they gave it to themselves: they know.
                if (ticket.History.LastOrDefault()?.By?.Id != ticket.TechnicianId)
                    Personal(EduNotifyEvents.AssignedToYou, ticket, TechnicianLogin(ticket), $"{RefCore(ticket)}: assigned to you", $"{ticket.Priority} priority.");
            }

            void LoanReminders(DateTime utcNow)
            {
                if (Rule(EduNotifyEvents.LoanDue) is not { } rule) return;
                var local = utcNow.ToLocalTime();
                var today = DateOnly.FromDateTime(local);
                var start = _data.Periods.Count > 0 ? _data.Periods.Min(x => x.Start) : new TimeOnly(8, 0);
                if (link.LoansRemindedOn == today || TimeOnly.FromDateTime(local) < start || !_data.SchoolDays.Contains(local.DayOfWeek)) return;
                link.LoansRemindedOn = today;
                changed = true;
                foreach (var loan in _data.KitLoans.Where(x => x.IsOut && x.DueBack <= today && x.BorrowerUserId is not null))
                {
                    var borrower = _data.Users.FirstOrDefault(x => x.Id == loan.BorrowerUserId);
                    if (LoginFor(borrower?.Email, link.EmailDomain) is not { } login) continue;
                    var kit = _data.LoanKits.FirstOrDefault(x => x.Id == loan.KitId)?.Name ?? "Your loan";
                    var reference = $"EH-loan-{loan.Id.ToString("N")[..8]}-to-{login}";
                    var title = loan.DueBack == today ? $"{kit}: due back today" : $"{kit}: overdue";
                    sends.Add(new(reference, EduNotifyEvents.LoanDue, null,
                        new EduNotify.Send(rule.TemplateId, false, [], title, "Please bring it back to IT.", reference, [login])));
                }
            }
        }
    }

    // Called with the lock held.
    private string? RequesterLogin(TicketRecord ticket) =>
        LoginFor(_data.Users.FirstOrDefault(x => x.Id == ticket.RequesterId)?.Email, Link.EmailDomain);

    private string? TechnicianLogin(TicketRecord ticket) =>
        ticket.TechnicianId is { } id ? LoginFor(_data.Technicians.FirstOrDefault(x => x.Id == id && x.IsActive)?.Email, Link.EmailDomain) : null;

    private string? TechnicianName(TicketRecord ticket) =>
        ticket.TechnicianId is { } id ? _data.Technicians.FirstOrDefault(x => x.Id == id)?.Name : null;

    private string? PortalTicketLink(int number) =>
        _data.SiteAddress.Length > 0 ? $"{_data.SiteAddress.TrimEnd('/')}/Portal/Ticket?number={number}" : null;

    private static string Minutes(int minutes) => minutes % 60 == 0 ? $"{minutes / 60} hour{(minutes == 60 ? "" : "s")}" : $"{minutes} minutes";

    // One pop-up per ticket per person (EduNotify replaces a live message with the same reference), and one per ticket for
    // its desk's PCs. Separate from the outage notice (EH-1234) and the ping (EH-1234-ping).
    public static string PersonalReference(int ticketNumber, string login) => $"EH-{ticketNumber}-to-{login}";
    public static string DeskReference(int ticketNumber) => $"EH-{ticketNumber}-desk";

    public void RecordEduNotifyPopupSent(EduNotifyEventSend sent, EduNotify.Sent result)
    {
        lock (_sync)
        {
            Link.Popups.RemoveAll(x => x.Reference == sent.Reference);
            Link.Popups.Add(new(sent.Reference, sent.TicketNumber, DateTime.UtcNow));
            if (sent.TicketNumber is { } number)
                AddStaffNoticeHistory(number, "EduNotify pop-up", $"{EduNotifyEvents.Label(sent.EventKey)}: \"{sent.Send.Title}\" to "
                    + (sent.Send.Users is { Count: > 0 } users ? string.Join(", ", users) : string.Join(", ", sent.Send.Groups))
                    + $". Shown on {result.Online} of {result.Targeted} PCs straight away.");
            Save();
        }
    }

    public void RecordEduNotifyPopupGone(string reference)
    {
        lock (_sync)
        {
            if (Link.Popups.RemoveAll(x => x.Reference == reference) > 0) Save();
        }
    }

    public void RecordEduNotifyCallClosed(int ticketNumber)
    {
        lock (_sync)
        {
            var index = Link.Calls.FindIndex(x => x.TicketNumber == ticketNumber);
            if (index < 0) return;
            Link.Calls[index] = Link.Calls[index] with { CloseSent = true };
            AddStaffNoticeHistory(ticketNumber, "Technician call closed", "EduNotify was told the ticket is resolved, so the call's alert came down.");
            Save();
        }
    }

    // ---- Storage (with the rest of EduNotify's) ----

    private static void ReadEduNotifyLink(SqliteConnection connection, StoreData data)
    {
        data.EduNotifyLink = new();
        if (ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'EduNotifyLink';") is string { Length: > 0 } json)
        {
            // As with the notices: unreadable settings start afresh rather than stopping the helpdesk starting.
            try { data.EduNotifyLink = JsonSerializer.Deserialize<EduNotifyLinkData>(json) ?? new(); }
            catch (JsonException) { }
        }
    }

    private static void WriteEduNotifyLink(SqliteConnection connection, SqliteTransaction transaction, StoreData data) =>
        SetMetadata(connection, transaction, "EduNotifyLink", JsonSerializer.Serialize(data.EduNotifyLink));
}
