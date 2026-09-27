using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Two-step sign-in for staff accounts: after the password, a code from an authenticator app (Totp), or one of ten
// one-time recovery codes. Each technician turns it on for themselves; Settings → Sign-in security can require it of
// everyone, and someone with Staff accounts: Edit can reset it for a person who has lost their phone. The DfE's cyber
// security standard for schools expects this on accounts that can change a system.
//
// The secret is stored as it is, like the rest of the data in the database, rather than encrypted with the sign-in
// keys: those are tied to this machine, and a backup restored elsewhere would otherwise lock every technician out.
public sealed partial class HelpdeskStore
{
    public bool RequireTwoFactor { get { lock (_sync) return _data.RequireTwoFactor; } }

    public string SetRequireTwoFactor(bool required)
    {
        lock (_sync)
        {
            if (_data.RequireTwoFactor == required) return "Nothing changed.";
            _data.RequireTwoFactor = required;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Sign-in", null, null, "Two-step sign-in", required ? "Required" : "No longer required",
                required ? "Every staff account now needs two-step sign-in. Anyone without it sets it up the next time they sign in."
                         : "Two-step sign-in is now each person's choice. Anyone who has it keeps it."));
            Save();
            return required ? "Two-step sign-in is now required. Anyone without it will set it up the next time they use the helpdesk."
                            : "Two-step sign-in is no longer required. Those who have it keep it.";
        }
    }

    // Turns it on once the first code from the app proves the secret reached it. The recovery codes are returned once,
    // to be shown once; only their hashes are kept.
    public (bool Ok, string Message, IReadOnlyList<string> RecoveryCodes) EnableTwoFactor(Guid technicianId, string secret, string? code)
    {
        lock (_sync)
        {
            var index = _data.Technicians.FindIndex(x => x.Id == technicianId);
            if (index < 0) return (false, "That account no longer exists.", []);
            if (Totp.Verify(secret, code, DateTimeOffset.UtcNow, long.MinValue) is not { } step)
                return (false, "That code didn't match. Check the phone's clock is right, and type the code the app shows now.", []);
            var codes = Totp.NewRecoveryCodes();
            var technician = _data.Technicians[index];
            _data.Technicians[index] = technician with { TwoFactor = new TwoFactorSetup(secret, DateTime.UtcNow, step, codes.Select(Totp.HashRecoveryCode).ToList()) };
            _pendingAudit.Add(TwoFactorEntry(technician, "Two-step sign-in turned on", "Set up with an authenticator app."));
            Save();
            return (true, "Two-step sign-in is on.", codes);
        }
    }

    // The person themselves, proving they still have the app or a recovery code.
    public (bool Ok, string Message) DisableTwoFactor(Guid technicianId, string? code)
    {
        lock (_sync)
        {
            if (_data.RequireTwoFactor) return (false, "Two-step sign-in is required for every staff account, so it can't be turned off.");
            if (CheckTwoFactorCore(technicianId, code) is not { Ok: true }) return (false, "That code didn't match, so two-step sign-in is still on.");
            var index = _data.Technicians.FindIndex(x => x.Id == technicianId);
            var technician = _data.Technicians[index];
            _data.Technicians[index] = technician with { TwoFactor = null };
            _pendingAudit.Add(TwoFactorEntry(technician, "Two-step sign-in turned off", "Turned off by the account holder."));
            Save();
            return (true, "Two-step sign-in is off.");
        }
    }

    // Someone else, for a person who has lost their phone and their recovery codes. They set it up again next time.
    public (bool Ok, string Message) ResetTwoFactor(Guid technicianId)
    {
        lock (_sync)
        {
            var index = _data.Technicians.FindIndex(x => x.Id == technicianId);
            if (index < 0) return (false, "That account no longer exists.");
            var technician = _data.Technicians[index];
            if (technician.TwoFactor is null) return (false, $"{technician.Name} doesn't have two-step sign-in set up.");
            _data.Technicians[index] = technician with { TwoFactor = null };
            _pendingAudit.Add(TwoFactorEntry(technician, "Two-step sign-in reset", "Reset so it can be set up again on a new phone."));
            Save();
            return (true, $"Two-step sign-in was reset for {technician.Name}." + (_data.RequireTwoFactor ? " They'll set it up again the next time they sign in." : ""));
        }
    }

    public IReadOnlyList<string>? RenewRecoveryCodes(Guid technicianId)
    {
        lock (_sync)
        {
            var index = _data.Technicians.FindIndex(x => x.Id == technicianId);
            if (index < 0 || _data.Technicians[index].TwoFactor is not { } setup) return null;
            var codes = Totp.NewRecoveryCodes();
            _data.Technicians[index] = _data.Technicians[index] with { TwoFactor = setup with { RecoveryCodeHashes = codes.Select(Totp.HashRecoveryCode).ToList() } };
            _pendingAudit.Add(TwoFactorEntry(_data.Technicians[index], "New recovery codes", "The old recovery codes no longer work."));
            Save();
            return codes;
        }
    }

    public sealed record TwoFactorCheck(bool Ok, bool UsedRecoveryCode, int RecoveryCodesLeft);

    // The second step of signing in. A right code is used up: an authenticator code can't be replayed, and a recovery
    // code is struck off.
    public TwoFactorCheck CheckTwoFactor(Guid technicianId, string? code)
    {
        lock (_sync)
        {
            var result = CheckTwoFactorCore(technicianId, code);
            if (result.Ok) Save();
            return result;
        }
    }

    private TwoFactorCheck CheckTwoFactorCore(Guid technicianId, string? code)
    {
        var index = _data.Technicians.FindIndex(x => x.Id == technicianId);
        if (index < 0 || _data.Technicians[index].TwoFactor is not { } setup) return new(false, false, 0);
        if (Totp.LooksLikeRecoveryCode(code))
        {
            var hash = Totp.HashRecoveryCode(code!);
            if (!setup.RecoveryCodeHashes.Contains(hash, StringComparer.Ordinal)) return new(false, true, setup.RecoveryCodeHashes.Count);
            var left = setup.RecoveryCodeHashes.Where(x => x != hash).ToList();
            _data.Technicians[index] = _data.Technicians[index] with { TwoFactor = setup with { RecoveryCodeHashes = left } };
            _pendingAudit.Add(TwoFactorEntry(_data.Technicians[index], "Recovery code used", $"{left.Count} recovery code{(left.Count == 1 ? "" : "s")} left."));
            return new(true, true, left.Count);
        }
        if (Totp.Verify(setup.Secret, code, DateTimeOffset.UtcNow, setup.LastUsedStep) is not { } step) return new(false, false, setup.RecoveryCodeHashes.Count);
        _data.Technicians[index] = _data.Technicians[index] with { TwoFactor = setup with { LastUsedStep = step } };
        return new(true, false, setup.RecoveryCodeHashes.Count);
    }

    private static AuditEntry TwoFactorEntry(TechnicianRecord technician, string action, string details) =>
        new(DateTime.UtcNow, "Sign-in", "Technician", technician.Id.ToString(), technician.Name, action, details);

    // ---- Storage ----

    private static void EnsureTwoFactorSchema(SqliteConnection connection)
    {
        foreach (var sql in new[]
        {
            "ALTER TABLE Technicians ADD COLUMN TotpSecret TEXT NULL;",
            "ALTER TABLE Technicians ADD COLUMN TwoFactorSince TEXT NULL;",
            "ALTER TABLE Technicians ADD COLUMN TotpLastStep INTEGER NULL;",
            "ALTER TABLE Technicians ADD COLUMN RecoveryCodes TEXT NULL;",
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

    // Columns 8-11 of the Technicians SELECT in ReadData.
    private static TwoFactorSetup? ReadTwoFactor(SqliteDataReader reader) =>
        reader.IsDBNull(8) || reader.IsDBNull(9) ? null
            : new TwoFactorSetup(reader.GetString(8), Date(reader, 9), reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
                (NullableString(reader, 11) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
}
