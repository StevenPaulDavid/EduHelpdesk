using System.Globalization;
using System.Threading.Channels;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Settings → Teams channel: posts a short message to a Teams channel through a webhook when a requester submits a ticket in
// the portal and when a ticket becomes overdue. TeamsPoster sends them; this holds the settings, decides what is worth a
// post and queues it.
// Messages are generic on purpose, as the browser pings are: the ticket number and a link that opens it, never the title,
// the requester or the text, because a Teams channel is held by Microsoft and read on screens the helpdesk doesn't control.
// The webhook address is a secret (whoever holds it can post to the channel): it is kept in Metadata, hidden from the raw
// data page for anyone but an Administrator (RawDatabase.IsSecretMetadata), left out of the audit log, and never shown again
// once saved - the page only says which host it posts to.
public sealed partial class HelpdeskStore
{
    // Each desk posts to its own channel (HelpdeskStore.Desks): Hosts says which desks have an address and where it
    // posts. HasUrl and Host are the first desk's, which is the only one with a single desk.
    public sealed record TeamsSettingsView(bool HasUrl, string Host, bool Enabled, bool NewTicket, bool Overdue)
    {
        public IReadOnlyDictionary<Guid, string> Hosts { get; init; } = new Dictionary<Guid, string>();
        // Posting happens only with an address somewhere and the switch on.
        public bool Ready => Hosts.Count > 0 && Enabled;
        public bool DeskReady(Guid deskId) => Enabled && Hosts.ContainsKey(deskId);
    }

    // One message to send: a heading, a line under it, and optionally a button that opens a page. DeskId is the desk
    // whose channel it goes to; null is the first desk's.
    public sealed record TeamsPost(string Heading, string Text, string? LinkText, string? LinkUrl, Guid? DeskId = null);

    // How the latest post went, for the settings page. Not saved: it is a status, and starts blank after a restart.
    public sealed record TeamsAttempt(DateTime At, bool Ok, string Message);

    // More than this many tickets turning overdue at once (a morning after a holiday, say) is one message, not a flood.
    private const int OverdueMessagesBeforeSummary = 3;

    // A burst larger than this drops the oldest unsent post rather than growing without limit.
    private readonly Channel<TeamsPost> _teamsOutbox = Channel.CreateBounded<TeamsPost>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private TeamsAttempt? _teamsLast;
    // What has already been announced as overdue, by ticket and the due date it was overdue against, so a ticket is
    // announced once - and again only if it is given a new due date and passes that too. Kept in memory: after a restart
    // everything already overdue is taken as announced (the first scan seeds it silently).
    private readonly Dictionary<int, DateTime> _overdueAnnounced = [];
    private bool _overdueSeeded;

    public ChannelReader<TeamsPost> TeamsOutbox => _teamsOutbox.Reader;
    public TeamsAttempt? TeamsLastAttempt => Volatile.Read(ref _teamsLast);
    public void RecordTeamsAttempt(bool ok, string message) => Volatile.Write(ref _teamsLast, new TeamsAttempt(DateTime.UtcNow, ok, message));

    // Read without taking the store's lock: the poster asks as it sends, and shouldn't queue behind a long save. The
    // dictionary is replaced, never changed in place, so a copy taken here can't change under the reader.
    public TeamsSettingsView TeamsSettings
    {
        get
        {
            var data = _data;
            var hooks = data.DeskTeamsWebhooks;
            var first = data.Desks.Count > 0 ? hooks.GetValueOrDefault(data.Desks[0].Id, "") : "";
            return new(first.Length > 0, TeamsWebhook.HostOf(first), data.TeamsEnabled, data.TeamsNewTicket, data.TeamsOverdue)
            {
                Hosts = hooks.Where(x => x.Value.Length > 0).ToDictionary(x => x.Key, x => TeamsWebhook.HostOf(x.Value))
            };
        }
    }

    // The secret itself, for the poster only: the desk's channel, or the first desk's for null.
    public string TeamsWebhookAddress => TeamsWebhookFor(null);
    public string TeamsWebhookFor(Guid? deskId)
    {
        var data = _data;
        var desk = deskId ?? (data.Desks.Count > 0 ? data.Desks[0].Id : Guid.Empty);
        return data.DeskTeamsWebhooks.GetValueOrDefault(desk, "");
    }

    // newUrl blank keeps the address already saved; remove takes it away. deskId is whose address it is (null: the first
    // desk's); the switches are shared by every desk.
    public (bool Ok, string Message) SetTeamsSettings(string? newUrl, bool remove, bool enabled, bool newTicket, bool overdue, Guid? deskId = null)
    {
        lock (_sync)
        {
            var before = TeamsSettings;
            var desk = _data.Desks.FirstOrDefault(x => x.Id == (deskId ?? _data.Desks[0].Id));
            if (desk is null) return (false, "That desk was not found.");
            var url = _data.DeskTeamsWebhooks.GetValueOrDefault(desk.Id, "");
            string? urlChange = null;
            if (remove)
            {
                if (url.Length > 0) urlChange = "removed";
                url = "";
            }
            else if (!string.IsNullOrWhiteSpace(newUrl))
            {
                var check = TeamsWebhook.Check(newUrl);
                if (!check.Ok) return (false, check.Problem);
                if (check.Url != url) urlChange = url.Length == 0 ? "set" : "changed";
                url = check.Url;
            }
            var hooks = new Dictionary<Guid, string>(_data.DeskTeamsWebhooks);
            if (url.Length > 0) hooks[desk.Id] = url; else hooks.Remove(desk.Id);
            // Nothing to post to means nothing to switch on.
            if (!hooks.Values.Any(x => x.Length > 0)) enabled = false;
            if (urlChange is null && before.Enabled == enabled && before.NewTicket == newTicket && before.Overdue == overdue) return (true, "Nothing changed.");

            _data.DeskTeamsWebhooks = hooks;
            _data.TeamsEnabled = enabled;
            _data.TeamsNewTicket = newTicket;
            _data.TeamsOverdue = overdue;
            // Switching overdue posts on must not announce everything that is already overdue: take them as read now.
            var after = TeamsSettings;
            if (after.Ready && after.Overdue && !(before.Ready && before.Overdue)) SeedOverdue(DateTime.UtcNow);
            else if (!after.Ready || !after.Overdue) { _overdueSeeded = false; _overdueAnnounced.Clear(); }

            var events = new List<string>();
            if (newTicket) events.Add("new tickets");
            if (overdue) events.Add("overdue tickets");
            var summary = !after.Ready ? (after.Hosts.Count == 0 ? "No webhook set" : "Switched off") : $"Posting {string.Join(" and ", events.DefaultIfEmpty("nothing"))}";
            // The address itself is a secret and is never written down here.
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "Teams channel", summary,
                $"{desk.Name} desk's webhook address {urlChange ?? "unchanged"}; posting {(enabled ? "on" : "off")}; new tickets {(newTicket ? "post" : "don't post")}; overdue tickets {(overdue ? "post" : "don't post")}."));
            Save();
            return (true, urlChange == "removed" ? (after.Hosts.Count == 0 ? "The webhook was removed and posting is off." : $"The {desk.Name} desk's webhook was removed; its tickets no longer post.")
                : after.Ready ? $"Saved. {summary} to Teams."
                : $"Saved. {summary}.");
        }
    }

    // ---- What gets posted ----

    private string TeamsBrand => _data.Branding is { BrandName: { Length: > 0 } brand } ? brand : "EduHelpdesk";

    // The address of a ticket for a Teams button: the helpdesk address set in Sign-in security, or failing that the one
    // the request came in on (only a request that is happening now can say). Null when neither is known - the message is
    // then sent without a button rather than with a wrong one.
    private string? TicketLink(int number, string? fallbackAddress) =>
        (_data.SiteAddress.Length > 0 ? _data.SiteAddress : fallbackAddress) is { Length: > 0 } address ? $"{address.TrimEnd('/')}/Job?number={number}" : null;

    public TeamsPost TeamsTestPost(Guid? deskId = null) => new($"Test message from {TeamsBrand}", "Teams is set up to receive helpdesk messages.", null, null, deskId);

    // A requester has submitted a ticket in the staff portal. Called from there and nowhere else, so tickets that arrive
    // another way (a technician logging one, an import, an onboarding's tasks) never post.
    public void QueueTeamsNewTicket(int number, string? fallbackAddress)
    {
        var settings = TeamsSettings;
        if (!settings.Ready || !settings.NewTicket) return;
        string? link;
        string brand;
        Guid deskId;
        lock (_sync)
        {
            link = TicketLink(number, fallbackAddress);
            brand = TeamsBrand;
            deskId = _data.Tickets.FirstOrDefault(x => x.Number == number) is { } ticket ? DeskForCategoryCore(ticket.Category).Id : _data.Desks[0].Id;
        }
        // To the ticket's own desk's channel, and nowhere if that desk hasn't one.
        if (!settings.DeskReady(deskId)) return;
        _teamsOutbox.Writer.TryWrite(new TeamsPost($"New ticket {Ref(number)}", $"Submitted in the {brand} staff portal.", link is null ? null : "Open ticket", link, deskId));
    }

    // Queues a post for every ticket that has become overdue since the last look, and returns how many tickets that was.
    // The first look after starting (or after switching this on) only notes what is already overdue.
    public int AnnounceOverdue(DateTime now)
    {
        lock (_sync)
        {
            var settings = TeamsSettings;
            if (!settings.Ready || !settings.Overdue)
            {
                _overdueSeeded = false;
                _overdueAnnounced.Clear();
                return 0;
            }
            if (!_overdueSeeded) { SeedOverdue(now); return 0; }
            var fresh = FindNewlyOverdue(now);
            if (fresh.Count == 0) return 0;
            var brand = TeamsBrand;
            // Each desk's channel hears about its own tickets; a desk without a channel is noted as announced all the same.
            foreach (var group in fresh.GroupBy(number => _data.Tickets.FirstOrDefault(x => x.Number == number) is { } ticket ? DeskForCategoryCore(ticket.Category) : _data.Desks[0]))
            {
                var desk = group.Key;
                if (!settings.DeskReady(desk.Id)) continue;
                var numbers = group.ToList();
                if (numbers.Count > OverdueMessagesBeforeSummary)
                {
                    var list = _data.SiteAddress.Length > 0 ? $"{_data.SiteAddress.TrimEnd('/')}/Jobs?view=overdue{(_data.Desks.Count > 1 ? $"&desk={Uri.EscapeDataString(desk.Name)}" : "")}" : null;
                    _teamsOutbox.Writer.TryWrite(new TeamsPost($"{numbers.Count} tickets are now overdue", $"They have passed their due dates in {brand}.", list is null ? null : "Open the overdue list", list, desk.Id));
                }
                else
                {
                    foreach (var number in numbers)
                    {
                        var link = TicketLink(number, null);
                        _teamsOutbox.Writer.TryWrite(new TeamsPost($"Ticket {RefCore(number)} is overdue", $"It has passed its due date in {brand}.", link is null ? null : "Open ticket", link, desk.Id));
                    }
                }
            }
            return fresh.Count;
        }
    }

    // Takes everything now overdue as already announced, without posting. Called with the lock held.
    private void SeedOverdue(DateTime now)
    {
        _overdueAnnounced.Clear();
        foreach (var ticket in _data.Tickets)
            if (TicketInsights.IsOverdue(ticket, now)) _overdueAnnounced[ticket.Number] = ticket.DueDate!.Value;
        _overdueSeeded = true;
    }

    // Tickets that are overdue against a due date not announced yet. Forgets closed and deleted tickets as it goes, so a
    // reopened ticket that passes its due date again is announced again. A ticket paused while overdue stays remembered,
    // so putting it On Hold and back doesn't announce it twice. Called with the lock held.
    private List<int> FindNewlyOverdue(DateTime now)
    {
        var fresh = new List<int>();
        var open = new HashSet<int>();
        foreach (var ticket in _data.Tickets)
        {
            if (TicketInsights.IsClosed(ticket)) continue;
            open.Add(ticket.Number);
            if (!TicketInsights.IsOverdue(ticket, now)) continue;
            var due = ticket.DueDate!.Value;
            if (_overdueAnnounced.TryGetValue(ticket.Number, out var announced) && announced == due) continue;
            _overdueAnnounced[ticket.Number] = due;
            fresh.Add(ticket.Number);
        }
        foreach (var number in _overdueAnnounced.Keys.Where(x => !open.Contains(x)).ToList()) _overdueAnnounced.Remove(number);
        return fresh;
    }

    // On load: the single address from before desks becomes the first desk's channel, once.
    private void PrepareTeams()
    {
        _data.DeskTeamsWebhooks ??= [];
        if (_data.TeamsWebhookUrl.Length == 0 || _data.Desks.Count == 0) return;
        if (!_data.DeskTeamsWebhooks.ContainsKey(_data.Desks[0].Id))
            _data.DeskTeamsWebhooks = new Dictionary<Guid, string>(_data.DeskTeamsWebhooks) { [_data.Desks[0].Id] = _data.TeamsWebhookUrl };
        _data.TeamsWebhookUrl = "";
    }

    // ---- Storage ----

    private static void ReadTeams(SqliteConnection connection, StoreData data)
    {
        string? Value(string key) => ExecuteScalar(connection, $"SELECT Value FROM Metadata WHERE Key = '{key}';") as string;
        data.TeamsWebhookUrl = Value("TeamsWebhookUrl") ?? "";
        // Each desk's own channel (HelpdeskStore.Desks), keyed TeamsWebhookUrl:<desk id>. The old single address above
        // becomes the first desk's on load (PrepareTeams).
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Key, Value FROM Metadata WHERE Key LIKE 'TeamsWebhookUrl:%';";
            using var reader = command.ExecuteReader();
            var hooks = new Dictionary<Guid, string>();
            while (reader.Read())
                if (Guid.TryParse(reader.GetString(0)["TeamsWebhookUrl:".Length..], out var deskId) && !reader.IsDBNull(1) && reader.GetString(1).Length > 0)
                    hooks[deskId] = reader.GetString(1);
            data.DeskTeamsWebhooks = hooks;
        }
        data.TeamsEnabled = Value("TeamsEnabled") == "1";
        data.TeamsNewTicket = Value("TeamsNewTicket") != "0";
        data.TeamsOverdue = Value("TeamsOverdue") != "0";
    }

    private static void WriteTeams(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "TeamsWebhookUrl", data.TeamsWebhookUrl);
        foreach (var desk in data.Desks)
            SetMetadata(connection, transaction, $"TeamsWebhookUrl:{desk.Id}", data.DeskTeamsWebhooks.GetValueOrDefault(desk.Id, ""));
        SetMetadata(connection, transaction, "TeamsEnabled", data.TeamsEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "TeamsNewTicket", data.TeamsNewTicket ? "1" : "0");
        SetMetadata(connection, transaction, "TeamsOverdue", data.TeamsOverdue ? "1" : "0");
    }
}
