using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Settings → Notifications: whether pings and pop-ups (HelpdeskStore.Notifications) work at all, for staff, for requesters
// in the portal, and whether people who haven't switched them on are reminded to. Kept with the other settings and saved
// by Save, but read without taking the store's lock: every page asks, and so does every browser's held request, and
// neither should queue behind a long save.
public sealed partial class HelpdeskStore
{
    public sealed record NotificationSettingsView(bool Enabled, bool Staff, bool Requesters, bool Prompt);

    public NotificationSettingsView NotificationSettings
    {
        get
        {
            var data = _data;
            return new(data.NotificationsEnabled, data.StaffNotificationsEnabled, data.RequesterNotificationsEnabled, data.NotificationPrompt);
        }
    }

    // Whether this side is switched on: the master switch and its own. Off means nothing is recorded, browsers stop
    // asking, and the Notifications pages say it is switched off.
    public bool NotificationsOpenFor(string audience)
    {
        var settings = NotificationSettings;
        return settings.Enabled && (audience == StaffAudience ? settings.Staff : audience == PortalAudience && settings.Requesters);
    }

    public string SetNotificationSettings(bool enabled, bool staff, bool requesters, bool prompt)
    {
        lock (_sync)
        {
            var before = NotificationSettings;
            if (before == new NotificationSettingsView(enabled, staff, requesters, prompt)) return "Nothing changed.";
            _data.NotificationsEnabled = enabled;
            _data.StaffNotificationsEnabled = staff;
            _data.RequesterNotificationsEnabled = requesters;
            _data.NotificationPrompt = prompt;
            var summary = !enabled ? "Off for everyone"
                : staff && requesters ? "On for staff and requesters"
                : staff ? "On for staff only"
                : requesters ? "On for requesters only"
                : "Off for everyone";
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "Notifications", summary,
                $"Pings and pop-ups: {(enabled ? "available" : "switched off")}; staff {(staff ? "on" : "off")}, requesters {(requesters ? "on" : "off")}. Reminder to switch them on: {(prompt ? "shown" : "not shown")}."));
            Save();
            return $"Saved. {summary}.{(prompt ? "" : " People are no longer reminded to switch them on.")}";
        }
    }
}
