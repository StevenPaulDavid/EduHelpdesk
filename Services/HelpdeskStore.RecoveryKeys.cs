using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Recovery keys for technicians (Services/RecoveryKeys.cs): making one, taking one away, and resetting a forgotten
// password with one. Settings → Sign-in security can turn the reset off for everyone.
public sealed partial class HelpdeskStore
{
    public bool AllowRecoveryKeys { get { lock (_sync) return _data.AllowRecoveryKeys; } }

    public string SetAllowRecoveryKeys(bool allowed)
    {
        lock (_sync)
        {
            if (_data.AllowRecoveryKeys == allowed) return "Nothing changed.";
            _data.AllowRecoveryKeys = allowed;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Sign-in", null, null, "Recovery keys", allowed ? "Allowed" : "Turned off",
                allowed ? "Technicians can reset a forgotten password with their recovery key." : "Recovery keys no longer reset passwords. Existing keys are kept, but don't work until this is turned back on."));
            Save();
            return allowed ? "Technicians can now reset a forgotten password with their recovery key."
                           : "Recovery keys are turned off. A forgotten password has to be reset by an administrator.";
        }
    }

    // A new key for the technician, replacing any they had. Returned once, to be shown and saved; only its hash is kept.
    // `firstPassword` is the one made for them as they choose their first password (Pages/ChangePassword).
    public string? CreateRecoveryKey(Guid technicianId, bool firstPassword = false)
    {
        lock (_sync)
        {
            var index = _data.Technicians.FindIndex(x => x.Id == technicianId);
            if (index < 0) return null;
            var key = RecoveryKeys.NewKey();
            var technician = _data.Technicians[index];
            _data.Technicians[index] = technician with { RecoveryKey = new RecoveryKeySetup(RecoveryKeys.Hash(key), DateTime.UtcNow) };
            _pendingAudit.Add(RecoveryKeyEntry(technician, technician.RecoveryKey is null ? "Recovery key made" : "Recovery key replaced",
                (firstPassword ? "Made for the account holder as they chose their own password." : "Made by the account holder.")
                + (technician.RecoveryKey is null ? "" : " The previous key no longer works.")));
            Save();
            return key;
        }
    }

    public (bool Ok, string Message) RemoveRecoveryKey(Guid technicianId)
    {
        lock (_sync)
        {
            var index = _data.Technicians.FindIndex(x => x.Id == technicianId);
            if (index < 0) return (false, "That account no longer exists.");
            var technician = _data.Technicians[index];
            if (technician.RecoveryKey is null) return (false, $"{technician.Name} doesn't have a recovery key.");
            _data.Technicians[index] = technician with { RecoveryKey = null };
            _pendingAudit.Add(RecoveryKeyEntry(technician, "Recovery key removed", "It no longer resets the password."));
            Save();
            return (true, $"{technician.Name}'s recovery key was removed. It no longer resets their password.");
        }
    }

    public bool RecoveryKeyMatches(Guid technicianId, string? key)
    {
        lock (_sync) return RecoveryKeys.Matches(key, _data.Technicians.FirstOrDefault(x => x.Id == technicianId)?.RecoveryKey?.Hash);
    }

    // The forgotten-password reset. The key is used up; the new password is theirs, so no change is forced. Technician
    // is null when the email and key don't match an active account with a key - one answer for every reason, so the form
    // doesn't tell anyone which accounts exist. `check` vets the new password against the account (its holder's name),
    // and only runs once the key has proved who is asking; a Problem leaves the key unused.
    public (TechnicianRecord? Technician, string? Problem) ResetPasswordWithRecoveryKey(string? email, string? key, string newPasswordHash, Func<TechnicianRecord, string?> check)
    {
        lock (_sync)
        {
            if (!_data.AllowRecoveryKeys) return (null, null);
            var index = _data.Technicians.FindIndex(x => string.Equals(x.Email, (email ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            var technician = index < 0 ? null : _data.Technicians[index];
            if (technician is not { IsActive: true, RecoveryKey: { } setup } || !RecoveryKeys.Matches(key, setup.Hash)) return (null, null);
            if (check(technician) is { } problem) return (null, problem);
            var updated = technician with { PasswordHash = newPasswordHash, RequirePasswordChange = false, RecoveryKey = null };
            _data.Technicians[index] = updated;
            // Nobody is signed in to name, so the entry names the account holder.
            _pendingAudit.Add(RecoveryKeyEntry(technician, "Password reset with recovery key",
                $"The recovery key made on {setup.CreatedAt.ToLocalTime():d MMM yyyy} was used and no longer works. Every sign-in of the account was ended.") with { By = new Actor(technician.Id, technician.Name) });
            Save();
            return (updated, null);
        }
    }

    private static AuditEntry RecoveryKeyEntry(TechnicianRecord technician, string action, string details) =>
        new(DateTime.UtcNow, "Sign-in", "Technician", technician.Id.ToString(), technician.Name, action, details);

    // ---- Storage ----

    private static void EnsureRecoveryKeySchema(SqliteConnection connection)
    {
        foreach (var sql in new[]
        {
            "ALTER TABLE Technicians ADD COLUMN RecoveryKeyHash TEXT NULL;",
            "ALTER TABLE Technicians ADD COLUMN RecoveryKeySince TEXT NULL;",
        })
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            catch (SqliteException) { }
        }
    }

    // Columns 12-13 of the Technicians SELECT in ReadData.
    private static RecoveryKeySetup? ReadRecoveryKey(SqliteDataReader reader) =>
        reader.IsDBNull(12) || reader.IsDBNull(13) ? null : new RecoveryKeySetup(reader.GetString(12), Date(reader, 13));
}
