using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The school's EduDashboard: the one site allowed to show the helpdesk inside it, as one of its tiles (SecurityHeaders).
// Set in Settings → Sign-in security; empty, as it starts, means no site may.
public sealed partial class HelpdeskStore
{
    public string DashboardAddress { get { lock (_sync) return _data.DashboardAddress; } }

    public (bool Ok, string Message) SetDashboardAddress(string? address)
    {
        var value = string.IsNullOrWhiteSpace(address) ? "" : SecurityHeaders.NormaliseOrigin(address);
        if (value is null) return (false, "Type the dashboard's whole address, starting http:// or https://, such as https://dashboard.school.org.uk.");
        lock (_sync)
        {
            if (_data.DashboardAddress == value) return (true, "Nothing changed.");
            var before = _data.DashboardAddress;
            _data.DashboardAddress = value;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Sign-in", null, null, "Shown inside EduDashboard", value.Length == 0 ? "Turned off" : "Allowed",
                $"{(before.Length == 0 ? "No site" : before)} → {(value.Length == 0 ? "no site" : value)}"));
            Save();
            return (true, value.Length == 0 ? "No site can show the helpdesk inside it now." : $"{value} can now show the helpdesk inside the dashboard.");
        }
    }
}
