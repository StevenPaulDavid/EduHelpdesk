using System.Net;
using System.Security.Cryptography;
using System.Text;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Phones paired with the EduHelpdesk app (Services/PhoneEndpoints.cs for what they call, docs/Phone-API.md for the
// contract). A phone holds a token that stands in for the sign-in cookie. It is shown once, as the phone is paired, and
// only its hash is kept, so a copy of the database signs nobody in.
// A token stops working when the phone signs out or is revoked, the account's password changes (as the cookie's stamp
// does), the account is deactivated or deleted, the school turns the phone app off, the role loses "Use the phone app",
// or the phone goes unused for PhoneIdleLimit. Kept in a table of its own and written directly, like Sessions: using a
// phone shouldn't rewrite the whole database.
public sealed partial class HelpdeskStore
{
    public static readonly TimeSpan PhoneIdleLimit = TimeSpan.FromDays(90);
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(5);
    // How finely "last used" is written down; to the minute would be a database write for every screen opened.
    private static readonly TimeSpan PhoneUseGranularity = TimeSpan.FromMinutes(10);
    public const string PhoneTokenPrefix = "ehp_";
    public const int MaxPhoneNameLength = 60;

    // How a phone was paired, for the Phones list.
    public static class PhonePairedBy
    {
        public const string Code = "code";
        public const string Password = "password";
    }

    public sealed record PhoneRecord(Guid Id, Guid TechnicianId, string Name, string PairedBy, DateTime CreatedAt, DateTime LastUsedAt, string? LastAddress);
    private sealed record PhoneRow(PhoneRecord Phone, string TokenHash, string Stamp);
    // Made by Phones → Add a phone for whoever is signed in, used once, and only while their password is unchanged.
    private sealed record PairingCode(Guid TechnicianId, string Stamp, DateTime ExpiresAt);

    private readonly Dictionary<string, PhoneRow> _phones = new(StringComparer.Ordinal);
    // Kept in memory only: a restart in the middle of pairing just means pressing Add a phone again.
    private readonly Dictionary<string, PairingCode> _pairingCodes = new(StringComparer.Ordinal);
    private readonly Lock _phoneSync = new();

    public bool AllowPhoneApp { get { lock (_sync) return _data.AllowPhoneApp; } }

    public string SetAllowPhoneApp(bool allowed)
    {
        lock (_sync)
        {
            if (_data.AllowPhoneApp == allowed) return "Nothing changed.";
            _data.AllowPhoneApp = allowed;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Sign-in", null, null, "Phone app", allowed ? "Turned on" : "Turned off",
                allowed ? "Technicians whose role has Use the phone app can pair a phone." : "Paired phones stop working until the phone app is turned back on. They stay paired."));
            Save();
            return allowed ? "The phone app is on. Technicians whose role has Use the phone app can pair a phone from Phones in their account menu."
                           : "The phone app is off. Paired phones stop working until it is turned back on.";
        }
    }

    public bool RequirePhoneAppLock { get { lock (_sync) return _data.RequirePhoneAppLock; } }

    // Whether every phone must lock itself after time away (the app's own App lock). The phone reads it from /me.
    public string SetRequirePhoneAppLock(bool required)
    {
        lock (_sync)
        {
            if (_data.RequirePhoneAppLock == required) return "Nothing changed.";
            _data.RequirePhoneAppLock = required;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Sign-in", null, null, "Phone app lock", required ? "Required" : "Not required",
                required ? "Every phone asks for a fingerprint, face or PIN after 5 minutes away." : "Each technician chooses whether their phone locks."));
            Save();
            return required ? "App lock is now required. Each phone turns it on the next time it opens."
                            : "App lock is no longer required. Each technician can choose in the app's Account screen.";
        }
    }

    public enum PhoneAccess { Ok, Unknown, AppOff, NotAllowed }

    // The verdict on a token. Unknown covers revoked, run out, a changed password and a deactivated account alike: the
    // phone has to be paired again. AppOff and NotAllowed are the school's choices, and lift without re-pairing.
    public sealed record PhoneCheck(PhoneAccess Access, TechnicianRecord? Technician = null, PhoneRecord? Phone = null);

    // Whether this account may use a phone at all, and if not, why, in words for the person.
    public string? PhoneRefusal(TechnicianRecord technician)
    {
        if (!AllowPhoneApp) return "The phone app is turned off for this helpdesk. An administrator can turn it on in Settings → Sign-in security.";
        if (!RoleHasFlag(technician.Role, Modules.Flags.PhoneApp)) return $"Your role ({technician.Role}) doesn't include Use the phone app. Ask an administrator to tick it on the role.";
        return null;
    }

    public string CreatePairingCode(Guid technicianId)
    {
        TechnicianRecord technician;
        lock (_sync) technician = _data.Technicians.First(x => x.Id == technicianId);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        lock (_phoneSync)
        {
            var now = DateTime.UtcNow;
            foreach (var stale in _pairingCodes.Where(x => x.Value.ExpiresAt <= now || x.Value.TechnicianId == technicianId).Select(x => x.Key).ToList())
                _pairingCodes.Remove(stale);
            _pairingCodes[code] = new PairingCode(technicianId, TechnicianSession.Stamp(technician), now + PairingCodeLifetime);
        }
        return code;
    }

    // Swaps a pairing code for a token. The person proved who they are on the web - password and two-step sign-in - before
    // the code was shown, so the code is all the phone needs.
    public (string? Token, string? Error) PairPhone(string? code, string? deviceName, IPAddress? address)
    {
        PairingCode? pairing;
        lock (_phoneSync)
        {
            if (string.IsNullOrWhiteSpace(code) || !_pairingCodes.Remove(code.Trim().ToUpperInvariant(), out pairing) || pairing.ExpiresAt <= DateTime.UtcNow)
                return (null, "That pairing code has run out or has already been used. Press Add a phone again for a new one.");
        }
        TechnicianRecord? technician;
        lock (_sync) technician = _data.Technicians.FirstOrDefault(x => x.Id == pairing.TechnicianId && x.IsActive);
        if (technician is null || TechnicianSession.Stamp(technician) != pairing.Stamp)
            return (null, "That pairing code is no longer valid. Sign in on the web and press Add a phone again.");
        if (PhoneRefusal(technician) is { } refusal) return (null, refusal);
        return (IssuePhoneToken(technician, deviceName, PhonePairedBy.Code, address), null);
    }

    // For a phone that has proved who it is: by a pairing code, or by password and two-step sign-in (PhoneEndpoints).
    public string IssuePhoneToken(TechnicianRecord technician, string? deviceName, string pairedBy, IPAddress? address)
    {
        var token = PhoneTokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var name = string.IsNullOrWhiteSpace(deviceName) ? "Phone" : deviceName.Trim();
        if (name.Length > MaxPhoneNameLength) name = name[..MaxPhoneNameLength];
        var now = DateTime.UtcNow;
        var phone = new PhoneRecord(Guid.NewGuid(), technician.Id, name, pairedBy, now, now, address?.ToString());
        var row = new PhoneRow(phone, HashToken(token), TechnicianSession.Stamp(technician));
        lock (_phoneSync)
        {
            ExecuteDirect("INSERT INTO Phones (Id, TokenHash, TechnicianId, Name, PairedBy, Stamp, CreatedAt, LastUsedAt, LastAddress) VALUES ($id, $hash, $technician, $name, $by, $stamp, $created, $used, $address);",
                ("$id", phone.Id.ToString()), ("$hash", row.TokenHash), ("$technician", technician.Id.ToString()), ("$name", name), ("$by", pairedBy),
                ("$stamp", row.Stamp), ("$created", Iso(now)), ("$used", Iso(now)), ("$address", phone.LastAddress));
            _phones[row.TokenHash] = row;
        }
        AuditPhone(technician, "Phone paired", $"{name}, paired {(pairedBy == PhonePairedBy.Code ? "with a pairing code" : "with a password")}{(address is null ? "" : $" from {address}")}.");
        return token;
    }

    public PhoneCheck CheckPhoneToken(string? token, IPAddress? address)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith(PhoneTokenPrefix, StringComparison.Ordinal)) return new PhoneCheck(PhoneAccess.Unknown);
        var hash = HashToken(token);
        PhoneRow? row;
        lock (_phoneSync) _phones.TryGetValue(hash, out row);
        if (row is null) return new PhoneCheck(PhoneAccess.Unknown);
        TechnicianRecord? technician;
        lock (_sync) technician = _data.Technicians.FirstOrDefault(x => x.Id == row.Phone.TechnicianId);
        var now = DateTime.UtcNow;
        if (technician is null || !technician.IsActive || TechnicianSession.Stamp(technician) != row.Stamp || now - row.Phone.LastUsedAt > PhoneIdleLimit)
        {
            EndPhone(hash);
            return new PhoneCheck(PhoneAccess.Unknown);
        }
        if (!AllowPhoneApp) return new PhoneCheck(PhoneAccess.AppOff, technician, row.Phone);
        if (!RoleHasFlag(technician.Role, Modules.Flags.PhoneApp)) return new PhoneCheck(PhoneAccess.NotAllowed, technician, row.Phone);
        var phone = row.Phone;
        var where = address?.ToString();
        if (now - phone.LastUsedAt >= PhoneUseGranularity || where != phone.LastAddress)
        {
            phone = phone with { LastUsedAt = now, LastAddress = where };
            lock (_phoneSync)
            {
                if (_phones.ContainsKey(hash))
                {
                    _phones[hash] = row with { Phone = phone };
                    TryDirect("A phone's last use couldn't be recorded",
                        ("UPDATE Phones SET LastUsedAt = $used, LastAddress = $address WHERE TokenHash = $hash;", [("$used", Iso(now)), ("$address", where), ("$hash", hash)]));
                }
            }
        }
        return new PhoneCheck(PhoneAccess.Ok, technician, phone);
    }

    public IReadOnlyList<PhoneRecord> PhonesOf(Guid technicianId)
    {
        lock (_phoneSync) return _phones.Values.Select(x => x.Phone).Where(x => x.TechnicianId == technicianId).OrderByDescending(x => x.LastUsedAt).ToList();
    }

    public IReadOnlyList<PhoneRecord> AllPhones
    {
        get { lock (_phoneSync) return _phones.Values.Select(x => x.Phone).OrderByDescending(x => x.LastUsedAt).ToList(); }
    }

    // From the Phones page (Revoke) or the phone itself (Sign out). Returns false for a phone that was already gone.
    public bool RevokePhone(Guid phoneId, string how)
    {
        PhoneRow? row;
        lock (_phoneSync) row = _phones.Values.FirstOrDefault(x => x.Phone.Id == phoneId);
        if (row is null || !EndPhone(row.TokenHash)) return false;
        TechnicianRecord? technician;
        lock (_sync) technician = _data.Technicians.FirstOrDefault(x => x.Id == row.Phone.TechnicianId);
        if (technician is not null) AuditPhone(technician, how, $"{row.Phone.Name}, paired {row.Phone.CreatedAt.ToLocalTime():d MMM yyyy}.");
        return true;
    }

    // Everywhere this account is signed in includes its phones (HelpdeskStore.Sessions.EndSessionsFor).
    private int EndPhonesFor(Guid technicianId)
    {
        lock (_phoneSync)
        {
            var hashes = _phones.Where(x => x.Value.Phone.TechnicianId == technicianId).Select(x => x.Key).ToList();
            foreach (var hash in hashes) _phones.Remove(hash);
            if (hashes.Count > 0) ExecuteDirect("DELETE FROM Phones WHERE TechnicianId = $technician;", ("$technician", technicianId.ToString()));
            return hashes.Count;
        }
    }

    private void EndAllPhones()
    {
        lock (_phoneSync)
        {
            _phones.Clear();
            _pairingCodes.Clear();
            ExecuteDirect("DELETE FROM Phones;");
        }
    }

    private bool EndPhone(string hash)
    {
        lock (_phoneSync)
        {
            if (!_phones.Remove(hash)) return false;
            ExecuteDirect("DELETE FROM Phones WHERE TokenHash = $hash;", ("$hash", hash));
            return true;
        }
    }

    private void AuditPhone(TechnicianRecord technician, string action, string details)
    {
        lock (_sync)
        {
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Sign-in", "Technician", technician.Id.ToString(), technician.Name, action, details));
            Save();
        }
    }

    // At startup, with phones past the idle limit cleared away.
    private void LoadPhones()
    {
        lock (_phoneSync)
        {
            _phones.Clear();
            ExecuteDirect("DELETE FROM Phones WHERE LastUsedAt < $cutoff;", ("$cutoff", Iso(DateTime.UtcNow - PhoneIdleLimit)));
            using var connection = new SqliteConnection($"Data Source={_path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, TokenHash, TechnicianId, Name, PairedBy, Stamp, CreatedAt, LastUsedAt, LastAddress FROM Phones;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!Guid.TryParse(reader.GetString(0), out var id) || !Guid.TryParse(reader.GetString(2), out var technician)) continue;
                var phone = new PhoneRecord(id, technician, reader.GetString(3), reader.GetString(4), Date(reader, 6), Date(reader, 7), reader.IsDBNull(8) ? null : reader.GetString(8));
                _phones[reader.GetString(1)] = new PhoneRow(phone, reader.GetString(1), reader.GetString(5));
            }
        }
    }

    private static void EnsurePhoneSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Phones (Id TEXT PRIMARY KEY, TokenHash TEXT NOT NULL UNIQUE, TechnicianId TEXT NOT NULL, Name TEXT NOT NULL, PairedBy TEXT NOT NULL, Stamp TEXT NOT NULL, CreatedAt TEXT NOT NULL, LastUsedAt TEXT NOT NULL, LastAddress TEXT);";
        command.ExecuteNonQuery();
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
