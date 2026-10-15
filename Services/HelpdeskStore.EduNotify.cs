using System.Text.Json;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Settings → EduNotify, and the "Tell staff" panel on a ticket: a technician puts an outage notice on staff PCs through
// EduNotify, updates it as the ticket moves on, and it is taken down when the ticket is closed. EduNotifyClient makes the
// calls; this holds the settings, what EduNotify last said the key can send, and which tickets have a notice showing.
// The API key is a secret (whoever holds it can put messages on staff PCs): it is kept in Metadata, hidden from the raw
// data page for anyone but an Administrator (RawDatabase.IsSecretMetadata), left out of the audit log, and never shown
// again once saved.
public sealed partial class HelpdeskStore
{
    public sealed record EduNotifySettingsView(string Address, bool HasKey, bool Enabled)
    {
        public bool HasAddress => Address.Length > 0;
        // Sending (and recalling) happens only with an address, a key and the switch on.
        public bool Ready => HasAddress && HasKey && Enabled;
        public bool IsHttps => EduNotify.IsHttps(Address);
    }

    // A notice a ticket has showing on staff PCs. Kept until it is recalled - by hand, or because the ticket was closed or
    // deleted - so a restart in between still takes it down. Title and Body are what was sent (the template's own where
    // they were left blank), so the panel can show it and start an update from it.
    public sealed record StaffNotice(int TicketNumber, long MessageId, string TemplateId, string TemplateName, bool Everyone,
        List<string> Groups, string Title, string Body, DateTime SentAt, string SentBy, int Online, int Targeted)
    {
        public string Recipients => Everyone ? "everyone" : string.Join(", ", Groups);
    }

    // How the latest call to EduNotify went, for the settings page. Not saved: it is a status, and starts blank after a restart.
    public sealed record EduNotifyAttempt(DateTime At, bool Ok, string Message);

    private EduNotify.Catalogue? _eduNotifyCatalogue;
    private EduNotifyAttempt? _eduNotifyLast;

    // Read without taking the store's lock, as the Teams settings are: the client asks as it calls.
    public EduNotifySettingsView EduNotifySettings
    {
        get
        {
            var data = _data;
            return new(data.EduNotifyAddress, data.EduNotifyApiKey.Length > 0, data.EduNotifyEnabled);
        }
    }

    // The secret itself, for EduNotifyClient only.
    public string EduNotifyApiKey => _data.EduNotifyApiKey;

    public EduNotify.Catalogue? EduNotifyCatalogue => Volatile.Read(ref _eduNotifyCatalogue);
    public void SetEduNotifyCatalogue(EduNotify.Catalogue? catalogue) => Volatile.Write(ref _eduNotifyCatalogue, catalogue);
    public EduNotifyAttempt? EduNotifyLastAttempt => Volatile.Read(ref _eduNotifyLast);
    public void RecordEduNotifyAttempt(bool ok, string message) => Volatile.Write(ref _eduNotifyLast, new EduNotifyAttempt(DateTime.UtcNow, ok, message));

    // newKey blank keeps the key already saved; removeKey takes it away (and so switches sending off). A blank address
    // clears it too. A new address or key forgets what the old one could send.
    public (bool Ok, string Message) SetEduNotifySettings(string? address, string? newKey, bool removeKey, bool enabled)
    {
        lock (_sync)
        {
            var before = EduNotifySettings;
            var newAddress = "";
            if (!string.IsNullOrWhiteSpace(address))
            {
                var check = EduNotify.CheckAddress(address);
                if (!check.Ok) return (false, check.Problem);
                newAddress = check.Address;
            }
            var key = _data.EduNotifyApiKey;
            string? keyChange = null;
            if (removeKey)
            {
                if (key.Length > 0) keyChange = "removed";
                key = "";
            }
            else if (!string.IsNullOrWhiteSpace(newKey))
            {
                var check = EduNotify.CheckKey(newKey);
                if (!check.Ok) return (false, check.Problem);
                if (check.Key != key) keyChange = key.Length == 0 ? "set" : "changed";
                key = check.Key;
            }
            // Nothing to call means nothing to switch on.
            if (newAddress.Length == 0 || key.Length == 0) enabled = false;
            var addressChanged = !string.Equals(newAddress, before.Address, StringComparison.OrdinalIgnoreCase);
            if (keyChange is null && !addressChanged && before.Enabled == enabled) return (true, "Nothing changed.");

            _data.EduNotifyAddress = newAddress;
            _data.EduNotifyApiKey = key;
            _data.EduNotifyEnabled = enabled;
            if (keyChange is not null || addressChanged)
            {
                SetEduNotifyCatalogue(null);
                Volatile.Write(ref _eduNotifyLast, null);
            }

            var after = EduNotifySettings;
            var summary = after.Ready ? "Sending on" : !after.HasAddress ? "No address set" : !after.HasKey ? "No API key set" : "Switched off";
            // The key itself is a secret and is never written down here; the address isn't.
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "EduNotify", summary,
                $"Address {(newAddress.Length == 0 ? "not set" : newAddress)}; API key {keyChange ?? "unchanged"}; sending {(enabled ? "on" : "off")}."));
            Save();
            return (true, keyChange == "removed" ? "The API key was removed and sending is off."
                : after.Ready ? "Saved. Use Test the connection to check EduNotify accepts the key."
                : $"Saved. {summary}.");
        }
    }

    // ---- Notices on tickets ----

    public StaffNotice? StaffNoticeFor(int ticketNumber)
    {
        lock (_sync) return _data.StaffNotices.FirstOrDefault(x => x.TicketNumber == ticketNumber);
    }

    public IReadOnlyList<StaffNotice> StaffNotices
    {
        get { lock (_sync) return _data.StaffNotices.OrderByDescending(x => x.SentAt).ToList(); }
    }

    // EduNotify has shown the notice: remember it against the ticket (replacing an earlier one, as EduNotify did) and say
    // so in the ticket's history.
    public void RecordStaffNoticeSent(StaffNotice notice)
    {
        lock (_sync)
        {
            var updated = _data.StaffNotices.RemoveAll(x => x.TicketNumber == notice.TicketNumber) > 0;
            _data.StaffNotices.Add(notice);
            AddStaffNoticeHistory(notice.TicketNumber, updated ? "Staff notice updated" : "Staff told through EduNotify",
                $"\"{notice.Title}\" to {notice.Recipients}, using the {notice.TemplateName} template. Shown on {notice.Online} of {notice.Targeted} PCs straight away.");
            Save();
        }
    }

    // The notice is no longer showing - recalled here, or EduNotify says it had already gone. why finishes the sentence
    // "Recalled …" in the ticket's history.
    public void RecordStaffNoticeRecalled(int ticketNumber, string why)
    {
        lock (_sync)
        {
            var notice = _data.StaffNotices.FirstOrDefault(x => x.TicketNumber == ticketNumber);
            if (notice is null) return;
            _data.StaffNotices.Remove(notice);
            AddStaffNoticeHistory(ticketNumber, "Staff notice recalled", $"\"{notice.Title}\" was taken off staff PCs {why}.");
            Save();
        }
    }

    // Notices whose ticket has been closed or deleted, for EduNotifyClient to recall. However the ticket was closed - its
    // page, the bulk actions, a merge, a requester's portal - this catches it within a few seconds.
    public IReadOnlyList<int> StaffNoticesToRecall()
    {
        lock (_sync)
        {
            return _data.StaffNotices
                .Where(notice => _data.Tickets.FirstOrDefault(x => x.Number == notice.TicketNumber) is not { } ticket || TicketInsights.IsClosed(ticket))
                .Select(x => x.TicketNumber)
                .ToList();
        }
    }

    // Called with the lock held. A deleted ticket has no history to add to.
    private void AddStaffNoticeHistory(int ticketNumber, string action, string details)
    {
        var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
        if (index < 0) return;
        var history = _data.Tickets[index].History.ToList();
        history.Add(new(action, details, DateTime.UtcNow) { By = CurrentActor() });
        _data.Tickets[index] = _data.Tickets[index] with { History = history };
    }

    // ---- Storage ----

    private static void ReadEduNotify(SqliteConnection connection, StoreData data)
    {
        string? Value(string key) => ExecuteScalar(connection, $"SELECT Value FROM Metadata WHERE Key = '{key}';") as string;
        data.EduNotifyAddress = Value("EduNotifyAddress") ?? "";
        data.EduNotifyApiKey = Value("EduNotifyApiKey") ?? "";
        data.EduNotifyEnabled = Value("EduNotifyEnabled") == "1";
        data.StaffNotices = [];
        if (Value("EduNotifyNotices") is { Length: > 0 } notices)
        {
            // A notice list that can't be read is dropped rather than stopping the helpdesk starting. The notices stay on
            // staff PCs until they are recalled in EduNotify itself.
            try { data.StaffNotices = JsonSerializer.Deserialize<List<StaffNotice>>(notices) ?? []; }
            catch (JsonException) { }
        }
        ReadTechnicianPing(connection, data);
    }

    private static void WriteEduNotify(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "EduNotifyAddress", data.EduNotifyAddress);
        SetMetadata(connection, transaction, "EduNotifyApiKey", data.EduNotifyApiKey);
        SetMetadata(connection, transaction, "EduNotifyEnabled", data.EduNotifyEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "EduNotifyNotices", data.StaffNotices.Count == 0 ? "" : JsonSerializer.Serialize(data.StaffNotices.OrderBy(x => x.TicketNumber)));
        WriteTechnicianPing(connection, transaction, data);
    }
}
