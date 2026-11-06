using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Device agent warnings: a PC that has stopped reporting (lost, broken, in a cupboard, or the agent removed) and a PC
// running low on disk space. Listed under Needs attention on the Device agent page, and once a day (BackupScheduler, from
// 7am as the compliance reminders) the bell rings for any PC newly on the list - once per PC per problem, so the end of
// a holiday's worth of silent PCs is one ring, not one a day. A disposed asset's PC is expected to go quiet and isn't
// listed.
public sealed partial class HelpdeskStore
{
    public const int DefaultAgentStaleDays = 14;
    public const int DefaultAgentLowDiskPercent = 10;
    public const int MaxAgentStaleDays = 365;
    public const int MaxAgentLowDiskPercent = 50;

    public static class AgentWarningKinds
    {
        public const string NotSeen = "not-seen";
        public const string LowDisk = "low-disk";
    }

    // Key: what the bell remembers. Detail: in words for the list.
    public sealed record AgentWarning(AgentDevice Device, AssetRecord? Asset, string Kind, string Detail)
    {
        public string Key => $"{Kind}:{Device.Id}";
    }

    public (int StaleDays, int LowDiskPercent) AgentWarningSettings { get { lock (_sync) return (_data.AgentStaleDays, _data.AgentLowDiskPercent); } }

    public string SetAgentWarningSettings(int staleDays, int lowDiskPercent)
    {
        if (staleDays is < 0 or > MaxAgentStaleDays) return $"Choose between 1 and {MaxAgentStaleDays} days, or 0 for no warning.";
        if (lowDiskPercent is < 0 or > MaxAgentLowDiskPercent) return $"Choose between 1 and {MaxAgentLowDiskPercent}%, or 0 for no warning.";
        lock (_sync)
        {
            if (_data.AgentStaleDays == staleDays && _data.AgentLowDiskPercent == lowDiskPercent) return "Nothing changed.";
            _data.AgentStaleDays = staleDays;
            _data.AgentLowDiskPercent = lowDiskPercent;
            Save();
            return "Warnings saved.";
        }
    }

    public IReadOnlyList<AgentWarning> AgentWarnings(DateTime utcNow)
    {
        lock (_sync) return AgentWarningsCore(utcNow);
    }

    private List<AgentWarning> AgentWarningsCore(DateTime utcNow)
    {
        var warnings = new List<AgentWarning>();
        var assets = _data.Assets.ToDictionary(x => x.Id);
        foreach (var device in _agents.Values.Select(x => x.Device).OrderBy(x => x.ComputerName, StringComparer.OrdinalIgnoreCase))
        {
            var asset = device.AssetId is { } id && assets.TryGetValue(id, out var found) ? found : null;
            if (asset is not null && IsDisposed(asset)) continue;
            var quiet = utcNow - device.LastSeenAt;
            if (_data.AgentStaleDays > 0 && quiet >= TimeSpan.FromDays(_data.AgentStaleDays))
                warnings.Add(new AgentWarning(device, asset, AgentWarningKinds.NotSeen, $"Not seen for {(int)quiet.TotalDays} days"));
            if (_data.AgentLowDiskPercent > 0 && AgentFacts.FreePercent(device.Report) is { } free && free < _data.AgentLowDiskPercent)
                warnings.Add(new AgentWarning(device, asset, AgentWarningKinds.LowDisk, $"Low disk space: {AgentFacts.FreeSpace(device.Report)}"));
        }
        return warnings;
    }

    // Returns how many accounts were told. force: whatever the time and whether it ran today (the tests).
    public int SendAgentReminders(DateTime localNow, bool force = false)
    {
        lock (_sync)
        {
            var today = DateOnly.FromDateTime(localNow);
            if (!force && (_data.LastAgentReminder == today || localNow.Hour < ComplianceReminderHour)) return 0;
            // Nothing to say while the agent is off or assets are switched off; nothing is remembered either, so turning
            // it back on rings for what is wrong then.
            if (AgentRefusal() is not null) return 0;
            var warnings = AgentWarningsCore(DateTime.UtcNow);
            var fresh = warnings.Count(x => !_data.AgentReminded.Contains(x.Key));
            // Only what is still wrong is remembered: a PC that comes back and goes quiet again rings again.
            _data.AgentReminded = warnings.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            _data.LastAgentReminder = today;
            var sent = 0;
            if (fresh > 0)
                foreach (var technician in _data.Technicians.Where(x => x.IsActive && RoleAllows(x.Role, Modules.DeviceAgent, ModulePermission.Access)))
                    if (AddNotification(StaffAudience, technician.Id, NotificationKinds.DeviceAgent) > 0) sent++;
            Save();
            return sent;
        }
    }
}
