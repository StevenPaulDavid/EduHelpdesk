using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduHelpdesk.AgentContract;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The EduHelpdesk Agent on school PCs (agent/EduHelpdesk.Agent, calling Services/AgentEndpoints.cs). A PC enrols once with
// the school's enrolment key and gets a token of its own; from then on it reports its specs at start-up and every day.
//
// Where a report goes:
// - A PC already enrolled reports into its asset. The asset's make, model, type, serial and operating system change only
//   when the PC's own answer changes, so a model name someone tidied by hand isn't put back every morning. Specs (memory,
//   disks, TPM...) are kept with the PC and shown on the asset's Device tab; a change to one goes in the asset's History.
// - A new PC whose serial matches nothing gets a new asset with the next asset number (Settings → Asset numbers).
// - A new PC whose serial matches an asset the agent has never reported into waits on the Device agent page until
//   someone links it or adds it as a new asset: it might be that asset, or a mistyped serial.
// - A PC that enrols again (Windows reinstalled) with a serial or board UUID the agent already knows carries on into
//   the same asset, with "Agent reinstalled" in its History.
//
// The PCs live in their own table, written directly like Phones: most reports change nothing on an asset, and a school's
// worth of PCs starting at 8:40 shouldn't each rewrite the database. Only a report that changes an asset saves the store.
// Only a hash of each token is kept, so a copy of the database can't pretend to be any PC.
public sealed partial class HelpdeskStore
{
    public const string AgentTokenPrefix = "eha_";
    public const string AgentKeyPrefix = "ehe_";
    // How often a PC sends a full report after the one at start-up.
    public const int AgentReportHours = 24;
    public static readonly Actor AgentActor = new(null, "EduHelpdesk Agent");

    // Status of a PC on the Device agent page.
    public static class AgentDeviceStatus
    {
        public const string Linked = "linked";
        public const string Waiting = "waiting";
    }

    // AssetId: the asset it reports into. MatchAssetId: while waiting, the asset whose serial it shares.
    public sealed record AgentDevice(Guid Id, Guid? AssetId, Guid? MatchAssetId, string SerialNumber, string Uuid, string ComputerName,
        DateTime EnrolledAt, DateTime LastSeenAt, string? LastAddress, string AgentVersion, AgentReport Report)
    {
        public string Status => AssetId is null ? AgentDeviceStatus.Waiting : AgentDeviceStatus.Linked;
    }

    private sealed record AgentRow(AgentDevice Device, string TokenHash);

    public sealed record AgentResult(int Status, CheckInAnswer? Answer = null, string? Error = null);

    // By token hash. Guarded by _sync: a report can change an asset, so it takes that lock anyway.
    private readonly Dictionary<string, AgentRow> _agents = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions AgentJson = new(JsonSerializerDefaults.Web);

    // ---- Set-up (Device agent page) ----

    public bool AgentEnabled { get { lock (_sync) return _data.AgentEnabled; } }
    public string AgentEnrolmentKey { get { lock (_sync) return _data.AgentEnrolmentKey; } }

    public string SetAgentEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_data.AgentEnabled == enabled && (!enabled || _data.AgentEnrolmentKey.Length > 0)) return "Nothing changed.";
            _data.AgentEnabled = enabled;
            if (enabled && _data.AgentEnrolmentKey.Length == 0) _data.AgentEnrolmentKey = NewAgentKeyText();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Device agent", null, null, "Device agent", enabled ? "Turned on" : "Turned off",
                enabled ? "PCs with the agent installed can enrol and report in." : "PCs stop reporting in until it is turned back on. They stay enrolled."));
            Save();
            return enabled ? "The device agent is on. Install it on a PC with the command below." : "The device agent is off. PCs keep trying daily and carry on when it is turned back on.";
        }
    }

    public string NewAgentEnrolmentKey()
    {
        lock (_sync)
        {
            _data.AgentEnrolmentKey = NewAgentKeyText();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Device agent", null, null, "Device agent", "New enrolment key",
                "The old key no longer enrols a PC. PCs already enrolled carry on."));
            Save();
            return "New enrolment key made. Use the new install command from now on; PCs already enrolled carry on.";
        }
    }

    private static string NewAgentKeyText() => AgentKeyPrefix + Base64Url(RandomNumberGenerator.GetBytes(24));

    // ---- What the pages show ----

    public IReadOnlyList<AgentDevice> AgentDevices
    {
        get { lock (_sync) return _agents.Values.Select(x => x.Device).OrderBy(x => x.ComputerName, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    public AgentDevice? AgentDeviceFor(Guid assetId)
    {
        lock (_sync) return _agents.Values.Select(x => x.Device).Where(x => x.AssetId == assetId).OrderByDescending(x => x.LastSeenAt).FirstOrDefault();
    }

    // The person in People that Windows says last signed in, matched by name or by the account's user name against the
    // start of their email address. Null when nobody matches - the page still shows what Windows said.
    public UserRecord? AgentDeviceUser(AgentReport report)
    {
        var name = AgentFacts.UserDisplayName(report);
        var account = AgentFacts.UserAccount(report);
        lock (_sync)
        {
            if (name is not null && _data.Users.Where(x => string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList() is [var byName]) return byName;
            if (account is null) return null;
            return _data.Users.Where(x => x.Email.Contains('@') && string.Equals(x.Email[..x.Email.IndexOf('@')], account, StringComparison.OrdinalIgnoreCase)).ToList() is [var byAccount] ? byAccount : null;
        }
    }

    // ---- Enrolling and reporting (AgentEndpoints) ----

    // Why no PC may report in just now, or null when they may. These are the school's choices, so a PC keeps its token.
    private string? AgentRefusal()
    {
        if (!_data.AgentEnabled) return "The device agent is turned off on this helpdesk.";
        if (_data.FeaturesOff.Contains(Features.Assets)) return "Assets are switched off on this helpdesk (Settings → Features).";
        return null;
    }

    public AgentResult EnrolAgent(string? key, AgentReport? report, IPAddress? address)
    {
        if (report is null) return new AgentResult(StatusCodes.Status400BadRequest, Error: "Send the PC's report with the enrolment.");
        report = Tidy(report);
        lock (_sync)
        {
            if (AgentRefusal() is { } refusal) return new AgentResult(StatusCodes.Status403Forbidden, Error: refusal);
            var expected = _data.AgentEnrolmentKey;
            if (string.IsNullOrEmpty(key) || expected.Length == 0
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key.Trim()), Encoding.UTF8.GetBytes(expected)))
                return new AgentResult(StatusCodes.Status401Unauthorized, Error: "That enrolment key isn't this helpdesk's current key. Copy the install command from the Device agent page again.");

            var now = DateTime.UtcNow;
            var serial = AgentFacts.Serial(report);
            var uuid = AgentFacts.Uuid(report);
            var device = new AgentDevice(Guid.NewGuid(), null, null, serial, uuid, AgentFacts.Clean(report.ComputerName) ?? "", now, now,
                address?.ToString(), AgentFacts.Clean(report.AgentVersion) ?? "", report);

            // The same PC enrolled before - Windows reinstalled, or the agent removed and put back. Its old token goes.
            var before = _agents.Values
                .Where(x => (serial.Length > 0 && string.Equals(x.Device.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)) || (uuid.Length > 0 && x.Device.Uuid == uuid))
                .OrderByDescending(x => x.Device.LastSeenAt).ToList();
            foreach (var old in before) RemoveAgentRow(old);
            var changed = false;
            if (before.FirstOrDefault() is { } previous && previous.Device.AssetId is { } assetId && _data.Assets.FindIndex(x => x.Id == assetId) is var index and >= 0)
            {
                device = device with { AssetId = assetId, EnrolledAt = previous.Device.EnrolledAt };
                ApplyAgentReport(index, previous.Device.Report, report, ("Agent reinstalled", $"{Named(device)} enrolled again and carries on reporting into this asset."));
                changed = true;
            }
            else if (before.FirstOrDefault() is { Device.MatchAssetId: { } matchId } && _data.Assets.Any(x => x.Id == matchId))
                device = device with { MatchAssetId = matchId, EnrolledAt = before[0].Device.EnrolledAt };
            else
                (device, changed) = PlaceAgentDevice(device);

            var token = AgentTokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
            var row = new AgentRow(device, HashToken(token));
            WriteAgentRow(row);
            _agents[row.TokenHash] = row;
            if (!changed)
                _pendingAudit.Add(new AuditEntry(now, "Device agent", null, null, Named(device), "PC enrolled",
                    device.MatchAssetId is { } m ? $"Its serial number matches {TagOf(m)}, so it waits on the Device agent page to be linked." : "Enrolled."));
            Save();
            return new AgentResult(StatusCodes.Status200OK, Answer(device, token));
        }
    }

    public AgentResult AgentReportIn(string? token, AgentReport? report, IPAddress? address)
    {
        if (report is null) return new AgentResult(StatusCodes.Status400BadRequest, Error: "Send the PC's report.");
        if (string.IsNullOrEmpty(token) || !token.StartsWith(AgentTokenPrefix, StringComparison.Ordinal))
            return new AgentResult(StatusCodes.Status401Unauthorized, Error: "This PC isn't enrolled.");
        report = Tidy(report);
        lock (_sync)
        {
            if (AgentRefusal() is { } refusal) return new AgentResult(StatusCodes.Status403Forbidden, Error: refusal);
            if (!_agents.TryGetValue(HashToken(token), out var row))
                return new AgentResult(StatusCodes.Status401Unauthorized, Error: "This PC isn't enrolled. It enrols again with its key.");
            var previous = row.Device;
            var device = previous with
            {
                Report = report, LastSeenAt = DateTime.UtcNow, LastAddress = address?.ToString(),
                AgentVersion = AgentFacts.Clean(report.AgentVersion) ?? "", ComputerName = AgentFacts.Clean(report.ComputerName) ?? previous.ComputerName,
                SerialNumber = AgentFacts.Serial(report), Uuid = AgentFacts.Uuid(report)
            };
            var changed = false;
            if (device.AssetId is { } assetId)
            {
                var index = _data.Assets.FindIndex(x => x.Id == assetId);
                // Its asset was deleted: the PC is still here, so it is placed again as if new.
                if (index < 0) (device, changed) = PlaceAgentDevice(device with { AssetId = null });
                else changed = ApplyAgentReport(index, previous.Report, report);
            }
            else if (device.MatchAssetId is { } matchId && !_data.Assets.Any(x => x.Id == matchId))
                (device, changed) = PlaceAgentDevice(device with { MatchAssetId = null });

            row = row with { Device = device };
            _agents[row.TokenHash] = row;
            if (changed) Save();
            // The asset is saved; the PC's own row is a convenience, and losing it till the next report is no harm.
            try { WriteAgentRow(row); }
            catch (Exception exception) when (exception is SqliteException or IOException) { LogProblem(exception, "A PC's report couldn't be recorded"); }
            return new AgentResult(StatusCodes.Status200OK, Answer(device, null));
        }
    }

    // Why this token may not download the agent (to update itself), or null when it may: an enrolled PC, while the
    // device agent is on.
    public (int Status, string Message)? AgentDownloadRefusal(string? token)
    {
        lock (_sync)
        {
            if (AgentRefusal() is { } refusal) return (StatusCodes.Status403Forbidden, refusal);
            if (string.IsNullOrEmpty(token) || !token.StartsWith(AgentTokenPrefix, StringComparison.Ordinal) || !_agents.ContainsKey(HashToken(token)))
                return (StatusCodes.Status401Unauthorized, "This PC isn't enrolled.");
            return null;
        }
    }

    private CheckInAnswer Answer(AgentDevice device, string? token) => device.AssetId is { } assetId
        ? new CheckInAnswer(token, AgentDeviceStatus.Linked, TagOf(assetId), AgentReportHours, null)
        : new CheckInAnswer(token, AgentDeviceStatus.Waiting, null, AgentReportHours,
            $"This PC's serial number matches {TagOf(device.MatchAssetId)}. IT will confirm it on the helpdesk.");

    // A PC that isn't reporting into an asset yet: waits if an asset the agent doesn't report into has its serial,
    // otherwise gets an asset of its own. Called with the lock held; true when it added an asset.
    private (AgentDevice Device, bool Added) PlaceAgentDevice(AgentDevice device)
    {
        if (device.SerialNumber.Length > 0)
        {
            var reported = _agents.Values.Where(x => x.Device.Id != device.Id && x.Device.AssetId is not null).Select(x => x.Device.AssetId!.Value).ToHashSet();
            var match = _data.Assets
                .Where(x => !reported.Contains(x.Id) && string.Equals(x.SerialNumber.Trim(), device.SerialNumber, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => IsDisposed(x)).FirstOrDefault();
            if (match is not null) return (device with { AssetId = null, MatchAssetId = match.Id }, false);
        }
        return (device with { AssetId = AddAgentAsset(device).Id, MatchAssetId = null }, true);
    }

    // A new asset made from what the PC says, numbered like the import numbers rows with no tag. Make, type and model
    // the school's lists don't have yet are added to them, as the import does.
    private AssetRecord AddAgentAsset(AgentDevice device)
    {
        var report = device.Report;
        var numbering = NewAssetNumbering();
        var tag = numbering.Take();
        _data.LastAssetNumber = Math.Max(_data.LastAssetNumber, numbering.Highest);
        var make = AgentOption(_data.AssetMakes, AgentFacts.Make(report));
        var asset = new AssetRecord(Guid.NewGuid(), tag, make, AgentModel(report, make), AgentOption(_data.AssetTypes, AgentFacts.Type(report)),
            device.SerialNumber, "", null)
        {
            OperatingSystem = AgentFacts.OperatingSystem(report),
            History = [new AssetActivity("Added by the device agent", $"{Named(device)} reported in for the first time.", DateTime.UtcNow) { By = CurrentActor() }]
        };
        AddAssetCore(asset);
        return asset;
    }

    // Brings an asset up to date with a PC's new report. previous is the PC's last report: a field changes only when the
    // PC's answer for it changed. extra is a history line to add regardless (a reinstall). True when anything changed.
    private bool ApplyAgentReport(int index, AgentReport previous, AgentReport next, (string Action, string Details)? extra = null)
    {
        var asset = _data.Assets[index];
        var item = asset;
        bool Moved(Func<AgentReport, string> read, out string value)
        {
            value = read(next);
            return value.Length > 0 && !string.Equals(read(previous), value, StringComparison.Ordinal);
        }
        if (Moved(AgentFacts.Make, out var make)) item = item with { Make = AgentOption(_data.AssetMakes, make) };
        if (Moved(AgentFacts.Model, out _)) item = item with { Model = AgentModel(next, item.Make) };
        if (Moved(AgentFacts.Type, out var type)) item = item with { Type = AgentOption(_data.AssetTypes, type) };
        if (Moved(AgentFacts.Serial, out var serial)) item = item with { SerialNumber = serial };
        if (Moved(AgentFacts.OperatingSystem, out var os)) item = item with { OperatingSystem = os };
        var lines = AgentFacts.Changes(previous, next).Select(x => new AssetActivity($"{x.Label} changed", $"{x.From} -> {x.To}", DateTime.UtcNow)).ToList();
        if (extra is { } line) lines.Insert(0, new AssetActivity(line.Action, line.Details, DateTime.UtcNow));
        if (ReferenceEquals(item, asset) && lines.Count == 0) return false;
        ApplyAssetUpdate(index, item);
        if (lines.Count > 0)
        {
            var by = CurrentActor();
            var updated = _data.Assets[index];
            _data.Assets[index] = updated with { History = [.. updated.History, .. lines.Select(x => x with { By = by })] };
        }
        return true;
    }

    // A value from the PC as the school's list spells it, adding it when the list hasn't got it. "" stays "".
    private static string AgentOption(List<string> list, string value)
    {
        if (value.Length == 0) return value;
        if (list.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) is { } known) return known;
        list.Add(value);
        return value;
    }

    private string AgentModel(AgentReport report, string make)
    {
        var spellings = AgentFacts.ModelSpellings(report).ToList();
        if (spellings.Count == 0) return string.Empty;
        foreach (var spelling in spellings)
            if (_data.AssetModels.FirstOrDefault(x => string.Equals(x, spelling, StringComparison.OrdinalIgnoreCase)) is { } known) return known;
        _data.AssetModels.Add(spellings[0]);
        if (make.Length > 0) _data.AssetModelMakes[spellings[0]] = make;
        return spellings[0];
    }

    // ---- Waiting PCs (Device agent page, Devices/Review) ----

    // The asset fields a PC can fill in, for the side-by-side review before linking. The machine is the authority on its
    // serial number and Windows, so those default to the PC; make, model and type are often typed the school's own way,
    // so they default to what the asset already says - unless it says nothing.
    public static class AgentMergeFields
    {
        public const string Make = "make";
        public const string Model = "model";
        public const string Type = "type";
        public const string Serial = "serial";
        public const string OperatingSystem = "os";
        public static readonly string[] All = [Make, Model, Type, Serial, OperatingSystem];
    }

    // One field: what the asset says, what the PC says (as the school's lists would spell it), and which is ticked first.
    public sealed record AgentMergeRow(string Key, string Label, string OnAsset, string FromPc, bool PcByDefault)
    {
        public bool Differs => !string.Equals(OnAsset.Trim(), FromPc.Trim(), StringComparison.OrdinalIgnoreCase);
        // A PC that says nothing for a field can't be chosen for it.
        public bool PcHasValue => FromPc.Length > 0;
    }

    // Candidates: every asset sharing the PC's serial that no other PC reports into - usually one, but a register can
    // hold the same serial twice. Asset is the one being compared.
    public sealed record AgentMergePreview(AgentDevice Device, AssetRecord Asset, IReadOnlyList<AssetRecord> Candidates, IReadOnlyList<AgentMergeRow> Rows);

    public AgentMergePreview? AgentMergePreviewFor(Guid deviceId, Guid? assetId = null)
    {
        lock (_sync)
        {
            if (_agents.Values.FirstOrDefault(x => x.Device.Id == deviceId)?.Device is not { AssetId: null } device) return null;
            var candidates = AgentCandidates(device);
            var asset = candidates.FirstOrDefault(x => x.Id == assetId) ?? candidates.FirstOrDefault(x => x.Id == device.MatchAssetId) ?? candidates.FirstOrDefault();
            if (asset is null) return null;
            var report = device.Report;
            AgentMergeRow Row(string key, string label, string onAsset, string fromPc, bool machineKnowsBest) =>
                new(key, label, onAsset ?? "", fromPc, fromPc.Length > 0 && (machineKnowsBest || string.IsNullOrWhiteSpace(onAsset)));
            return new AgentMergePreview(device, asset, candidates,
            [
                Row(AgentMergeFields.Make, "Make", asset.Make, Spelled(_data.AssetMakes, AgentFacts.Make(report)), false),
                Row(AgentMergeFields.Model, "Model", asset.Model, AgentModelSpelling(report), false),
                Row(AgentMergeFields.Type, "Type", asset.Type, Spelled(_data.AssetTypes, AgentFacts.Type(report)), false),
                Row(AgentMergeFields.Serial, "Serial number", asset.SerialNumber, device.SerialNumber, true),
                Row(AgentMergeFields.OperatingSystem, "Operating system", asset.OperatingSystem, AgentFacts.OperatingSystem(report), true)
            ]);
        }
    }

    // The PC is this asset: it reports into it from now on. usePc names the fields (AgentMergeFields) to take from the PC;
    // the rest keep what the asset says. Null takes the review's defaults. assetId null is the asset it first matched.
    public (bool Ok, string Message) LinkAgentDevice(Guid deviceId, Guid? assetId = null, IReadOnlyCollection<string>? usePc = null)
    {
        lock (_sync)
        {
            if (_agents.Values.FirstOrDefault(x => x.Device.Id == deviceId) is not { } row) return (false, "That PC isn't enrolled any more.");
            if (row.Device.AssetId is not null) return (false, "That PC already reports into an asset.");
            var target = assetId ?? row.Device.MatchAssetId;
            var preview = AgentMergePreviewFor(deviceId, target);
            if (preview is null || preview.Asset.Id != target)
                return (false, "That asset can't be linked to this PC any more: it was deleted, its serial number changed, or another PC reports into it.");
            var take = usePc?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? preview.Rows.Where(x => x.PcByDefault).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Only where the two differ: "5cd123" and "5CD123" are the same serial, and the asset's spelling stays.
            bool Take(string key) => take.Contains(key) && preview.Rows.Any(x => x.Key == key && x.PcHasValue && x.Differs);

            var index = _data.Assets.FindIndex(x => x.Id == preview.Asset.Id);
            var asset = _data.Assets[index];
            var report = row.Device.Report;
            var item = asset;
            if (Take(AgentMergeFields.Make)) item = item with { Make = AgentOption(_data.AssetMakes, AgentFacts.Make(report)) };
            if (Take(AgentMergeFields.Model)) item = item with { Model = AgentModel(report, item.Make) };
            if (Take(AgentMergeFields.Type)) item = item with { Type = AgentOption(_data.AssetTypes, AgentFacts.Type(report)) };
            if (Take(AgentMergeFields.Serial)) item = item with { SerialNumber = row.Device.SerialNumber };
            if (Take(AgentMergeFields.OperatingSystem)) item = item with { OperatingSystem = AgentFacts.OperatingSystem(report) };
            ApplyAssetUpdate(index, item);
            AddAgentHistory(index, "Linked to the device agent", $"{Named(row.Device)} reports into this asset from now on.");
            var linked = row with { Device = row.Device with { AssetId = asset.Id, MatchAssetId = null } };
            WriteAgentRow(linked);
            _agents[linked.TokenHash] = linked;
            Save();
            return (true, $"{Named(row.Device)} now reports into {asset.AssetTag}.");
        }
    }

    // Assets a waiting PC might be, its first match first, then the rest; disposed ones last. Called with the lock held.
    private List<AssetRecord> AgentCandidates(AgentDevice device)
    {
        var reported = _agents.Values.Where(x => x.Device.Id != device.Id && x.Device.AssetId is not null).Select(x => x.Device.AssetId!.Value).ToHashSet();
        return _data.Assets
            .Where(x => !reported.Contains(x.Id) && (x.Id == device.MatchAssetId
                || (device.SerialNumber.Length > 0 && string.Equals(x.SerialNumber.Trim(), device.SerialNumber, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(x => x.Id != device.MatchAssetId).ThenBy(IsDisposed).ThenBy(x => x.AssetTag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // How a value would be spelt once it is on the school's list, without adding it.
    private static string Spelled(List<string> list, string value) =>
        list.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) ?? value;

    private string AgentModelSpelling(AgentReport report)
    {
        var spellings = AgentFacts.ModelSpellings(report).ToList();
        foreach (var spelling in spellings)
            if (_data.AssetModels.FirstOrDefault(x => string.Equals(x, spelling, StringComparison.OrdinalIgnoreCase)) is { } known) return known;
        return spellings.FirstOrDefault() ?? "";
    }

    // The PC isn't the asset it matched (a mistyped serial, a second board with the same one): it gets its own.
    public (bool Ok, string Message) AddAgentDeviceAsNew(Guid deviceId)
    {
        lock (_sync)
        {
            if (_agents.Values.FirstOrDefault(x => x.Device.Id == deviceId) is not { } row) return (false, "That PC isn't enrolled any more.");
            if (row.Device.AssetId is not null) return (false, "That PC already reports into an asset.");
            var asset = AddAgentAsset(row.Device);
            var linked = row with { Device = row.Device with { AssetId = asset.Id, MatchAssetId = null } };
            WriteAgentRow(linked);
            _agents[linked.TokenHash] = linked;
            Save();
            return (true, $"{Named(row.Device)} was added as {asset.AssetTag}.");
        }
    }

    // Removes the PC. Its token stops working; if the agent is still installed it enrols again at its next report.
    public (bool Ok, string Message) ForgetAgentDevice(Guid deviceId)
    {
        lock (_sync)
        {
            if (_agents.Values.FirstOrDefault(x => x.Device.Id == deviceId) is not { } row) return (false, "That PC isn't enrolled any more.");
            RemoveAgentRow(row);
            if (row.Device.AssetId is { } assetId && _data.Assets.FindIndex(x => x.Id == assetId) is var index and >= 0)
                AddAgentHistory(index, "Removed from the device agent", $"{Named(row.Device)} was removed from the Device agent page.");
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Device agent", null, null, Named(row.Device), "PC removed",
                row.Device.AssetId is { } id ? $"It reported into {TagOf(id)}." : "It was waiting to be linked."));
            Save();
            return (true, $"{Named(row.Device)} was removed. If the agent is still on it, it enrols again at its next report.");
        }
    }

    private void AddAgentHistory(int index, string action, string details)
    {
        var asset = _data.Assets[index];
        _data.Assets[index] = asset with { History = [.. asset.History, new AssetActivity(action, details, DateTime.UtcNow) { By = CurrentActor() }] };
    }

    private string TagOf(Guid? assetId) => _data.Assets.FirstOrDefault(x => x.Id == assetId)?.AssetTag ?? "an asset that has since been deleted";

    private static string Named(AgentDevice device) => device.ComputerName.Length > 0 ? device.ComputerName : "A PC";

    // Lists capped and text trimmed, so a report can't grow the database without limit.
    private static AgentReport Tidy(AgentReport report) => report with
    {
        Disks = report.Disks?.Take(16).Select(x => x with { Name = AgentFacts.Clean(x.Name), Kind = AgentFacts.Clean(x.Kind) }).ToList(),
        Networks = report.Networks?.Take(16).Select(x => x with { Name = AgentFacts.Clean(x.Name), MacAddress = AgentFacts.Clean(x.MacAddress), Addresses = x.Addresses?.Take(8).Select(AgentFacts.Clean).OfType<string>().ToList() }).ToList(),
        Antivirus = report.Antivirus?.Take(8).Select(AgentFacts.Clean).OfType<string>().ToList(),
        ChassisTypes = report.ChassisTypes?.Take(8).ToArray()
    };

    // ---- Storage ----

    private void WriteAgentRow(AgentRow row)
    {
        var d = row.Device;
        ExecuteDirect("INSERT OR REPLACE INTO AgentDevices (Id, TokenHash, AssetId, MatchAssetId, SerialNumber, Uuid, ComputerName, EnrolledAt, LastSeenAt, LastAddress, AgentVersion, Report) VALUES ($id, $hash, $asset, $match, $serial, $uuid, $name, $enrolled, $seen, $address, $version, $report);",
            ("$id", d.Id.ToString()), ("$hash", row.TokenHash), ("$asset", d.AssetId?.ToString()), ("$match", d.MatchAssetId?.ToString()),
            ("$serial", d.SerialNumber), ("$uuid", d.Uuid), ("$name", d.ComputerName), ("$enrolled", Iso(d.EnrolledAt)), ("$seen", Iso(d.LastSeenAt)),
            ("$address", d.LastAddress), ("$version", d.AgentVersion), ("$report", JsonSerializer.Serialize(d.Report, AgentJson)));
    }

    private void RemoveAgentRow(AgentRow row)
    {
        _agents.Remove(row.TokenHash);
        ExecuteDirect("DELETE FROM AgentDevices WHERE Id = $id;", ("$id", row.Device.Id.ToString()));
    }

    // Factory reset: the enrolment key went with the old settings.
    private void ForgetAllAgentDevices()
    {
        lock (_sync)
        {
            _agents.Clear();
            ExecuteDirect("DELETE FROM AgentDevices;");
        }
    }

    private void LoadAgentDevices()
    {
        lock (_sync)
        {
            _agents.Clear();
            using var connection = new SqliteConnection($"Data Source={_path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, TokenHash, AssetId, MatchAssetId, SerialNumber, Uuid, ComputerName, EnrolledAt, LastSeenAt, LastAddress, AgentVersion, Report FROM AgentDevices;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!Guid.TryParse(reader.GetString(0), out var id)) continue;
                AgentReport report;
                try { report = JsonSerializer.Deserialize<AgentReport>(reader.GetString(11), AgentJson) ?? new AgentReport(); }
                catch (JsonException) { report = new AgentReport(); }
                Guid? Id(int i) => !reader.IsDBNull(i) && Guid.TryParse(reader.GetString(i), out var g) ? g : null;
                var device = new AgentDevice(id, Id(2), Id(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), Date(reader, 7), Date(reader, 8),
                    reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10), report);
                _agents[reader.GetString(1)] = new AgentRow(device, reader.GetString(1));
            }
        }
    }

    private static void EnsureAgentSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS AgentDevices (Id TEXT PRIMARY KEY, TokenHash TEXT NOT NULL UNIQUE, AssetId TEXT, MatchAssetId TEXT, SerialNumber TEXT NOT NULL, Uuid TEXT NOT NULL, ComputerName TEXT NOT NULL, EnrolledAt TEXT NOT NULL, LastSeenAt TEXT NOT NULL, LastAddress TEXT, AgentVersion TEXT NOT NULL, Report TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }

    private static void ReadAgentSettings(SqliteConnection connection, StoreData data)
    {
        string? Value(string key) => ExecuteScalar(connection, $"SELECT Value FROM Metadata WHERE Key = '{key}';") as string;
        data.AgentEnabled = Value("AgentEnabled") == "1";
        data.AgentEnrolmentKey = Value("AgentEnrolmentKey") ?? "";
        if (int.TryParse(Value("AgentStaleDays"), out var days)) data.AgentStaleDays = days;
        if (int.TryParse(Value("AgentLowDiskPercent"), out var percent)) data.AgentLowDiskPercent = percent;
        if (DateOnly.TryParseExact(Value("LastAgentReminder"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
            data.LastAgentReminder = day;
        data.AgentReminded = new HashSet<string>(StringComparer.Ordinal);
        if (Value("AgentReminded") is { Length: > 0 } reminded)
        {
            try { data.AgentReminded.UnionWith(JsonSerializer.Deserialize<List<string>>(reminded) ?? []); }
            catch (JsonException) { }
        }
    }

    private static void WriteAgentSettings(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "AgentEnabled", data.AgentEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "AgentEnrolmentKey", data.AgentEnrolmentKey);
        SetMetadata(connection, transaction, "AgentStaleDays", data.AgentStaleDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "AgentLowDiskPercent", data.AgentLowDiskPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "LastAgentReminder", IsoDay(data.LastAgentReminder) ?? "");
        // Sorted, so the same set always writes the same row and an unchanged save writes nothing.
        SetMetadata(connection, transaction, "AgentReminded", data.AgentReminded.Count == 0 ? "" : JsonSerializer.Serialize(data.AgentReminded.Order(StringComparer.Ordinal)));
    }
}
