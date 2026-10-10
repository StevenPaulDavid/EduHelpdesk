using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Reminders under the bell when something on the DfE registers falls due: a contract's notice date or renewal, an asset
// check or end of support, a leaver still with access, the termly access review. Once a day (BackupScheduler), each
// finding is remembered by its key, so the bell only rings for what is new - not every morning for the same overdue
// check. The message itself is generic, like every notification, and opens the compliance summary.
public sealed partial class HelpdeskStore
{
    public const int ComplianceReminderHour = 7;

    // Returns how many accounts were told. force: run now whatever the time and whether it ran today (the tests, and a
    // first look straight after an upgrade).
    public int SendComplianceReminders(DateTime localNow, bool force = false)
    {
        lock (_sync)
        {
            var today = DateOnly.FromDateTime(localNow);
            if (!force && (_data.LastComplianceReminder == today || localNow.Hour < ComplianceReminderHour)) return 0;
            var findings = ComplianceFindings.For(this, today).Where(x => x.Level >= ComplianceFinding.Levels.Warning).ToList();
            var fresh = findings.Where(x => !_data.ComplianceReminded.Contains(x.Key)).ToList();
            // Only what is still outstanding is remembered, so something fixed and then due again rings again.
            _data.ComplianceReminded = findings.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            _data.LastComplianceReminder = today;
            var sent = 0;
            if (fresh.Count > 0)
            {
                var registers = fresh.Select(x => x.Register).ToHashSet();
                foreach (var technician in _data.Technicians.Where(x => x.IsActive))
                {
                    var sees = (registers.Contains(ComplianceFindings.Assets) && RoleAllows(technician.Role, Modules.Assets, ModulePermission.Access))
                        || (registers.Contains(ComplianceFindings.Contracts) && RoleAllows(technician.Role, Modules.Contracts, ModulePermission.Access))
                        || (registers.Contains(ComplianceFindings.Access) && RoleAllows(technician.Role, Modules.Access, ModulePermission.Access));
                    if (sees && AddNotification(StaffAudience, technician.Id, NotificationKinds.Compliance) > 0) sent++;
                }
            }
            Save();
            return sent;
        }
    }

    // ---- Storage ----

    private static void EnsureComplianceReminderSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ComplianceReminders (Key TEXT PRIMARY KEY);";
        command.ExecuteNonQuery();
    }

    private static void ReadComplianceReminders(SqliteConnection connection, StoreData data)
    {
        if (ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LastComplianceReminder';") is string last
            && DateOnly.TryParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            data.LastComplianceReminder = day;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key FROM ComplianceReminders;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) data.ComplianceReminded.Add(reader.GetString(0));
    }

    private static void WriteComplianceReminders(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "LastComplianceReminder", IsoDay(data.LastComplianceReminder) ?? "");
        foreach (var key in data.ComplianceReminded)
            Execute(connection, transaction, "INSERT INTO ComplianceReminders (Key) VALUES ($key);", ("$key", key));
    }
}
