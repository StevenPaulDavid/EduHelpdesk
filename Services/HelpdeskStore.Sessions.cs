using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Signed-in sessions, recorded on the server so that signing out ends one for good. The cookies used to be the whole
// story: one copied before the person signed out - off a shared staffroom PC, say - kept working until it ran out. Now
// every helpdesk and portal cookie names its session, and a session that has been ended, or has run out, is refused
// whatever the cookie says. Kept in a table of its own and written directly, not by Save: signing in or out shouldn't
// rewrite the whole database, and Save's rewrite leaves this table alone.
public sealed partial class HelpdeskStore
{
    public const string HelpdeskSession = "helpdesk";
    public const string PortalSession = "portal";

    private sealed record SessionRow(string Kind, Guid AccountId, DateTime ExpiresAt);

    // Checked on every request, so it has its own lock: a sign-in check must not queue behind a long save.
    private readonly Dictionary<string, SessionRow> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _sessionSync = new();

    public string StartSession(string kind, Guid accountId, DateTime expiresAtUtc)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        lock (_sessionSync)
        {
            ExecuteDirect("INSERT INTO Sessions (Id, Kind, AccountId, CreatedAt, ExpiresAt) VALUES ($id, $kind, $account, $created, $expires);",
                ("$id", id), ("$kind", kind), ("$account", accountId.ToString()), ("$created", Iso(DateTime.UtcNow)), ("$expires", Iso(expiresAtUtc)));
            _sessions[id] = new SessionRow(kind, accountId, expiresAtUtc);
        }
        return id;
    }

    // Live, of this kind, and for this account.
    public bool SessionActive(string? id, string kind, Guid accountId)
    {
        if (string.IsNullOrEmpty(id)) return false;
        lock (_sessionSync)
            return _sessions.TryGetValue(id, out var row) && row.Kind == kind && row.AccountId == accountId && row.ExpiresAt > DateTime.UtcNow;
    }

    public void EndSession(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (_sessionSync)
        {
            if (!_sessions.Remove(id)) return;
            ExecuteDirect("DELETE FROM Sessions WHERE Id = $id;", ("$id", id));
        }
    }

    // Everywhere this account is signed in - used by a factory reset, and available for "sign out everywhere".
    public int EndSessionsFor(Guid accountId)
    {
        lock (_sessionSync)
        {
            var ids = _sessions.Where(x => x.Value.AccountId == accountId).Select(x => x.Key).ToList();
            foreach (var id in ids) _sessions.Remove(id);
            if (ids.Count > 0) ExecuteDirect("DELETE FROM Sessions WHERE AccountId = $account;", ("$account", accountId.ToString()));
            return ids.Count;
        }
    }

    public void EndAllSessions()
    {
        lock (_sessionSync)
        {
            _sessions.Clear();
            ExecuteDirect("DELETE FROM Sessions;");
        }
    }

    // At startup: what is still live, with anything that has run out cleared away.
    private void LoadSessions()
    {
        lock (_sessionSync)
        {
            _sessions.Clear();
            ExecuteDirect("DELETE FROM Sessions WHERE ExpiresAt < $now;", ("$now", Iso(DateTime.UtcNow)));
            using var connection = new SqliteConnection($"Data Source={_path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Kind, AccountId, ExpiresAt FROM Sessions;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (Guid.TryParse(reader.GetString(2), out var account))
                    _sessions[reader.GetString(0)] = new SessionRow(reader.GetString(1), account, Date(reader, 3));
        }
    }

    // Also used by the notifications table, which is written the same way (HelpdeskStore.Notifications).
    private void ExecuteDirect(string sql, params (string Name, object? Value)[] values)
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void EnsureSessionSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Sessions (Id TEXT PRIMARY KEY, Kind TEXT NOT NULL, AccountId TEXT NOT NULL, CreatedAt TEXT NOT NULL, ExpiresAt TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }
}
