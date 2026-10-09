using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Small choices a person makes about how pages look for them - which columns a list shows - kept against their account,
// so they follow them from the office PC to their laptop. Like the bell's read marks, they live in their own table,
// written directly rather than through Save(): they aren't helpdesk data, aren't audited, and losing one only puts a
// list back to its default columns.
public partial class HelpdeskStore
{
    private readonly object _preferenceSync = new();
    private readonly Dictionary<(Guid Account, string Key), string> _preferences = [];

    public string? Preference(Guid accountId, string key)
    {
        lock (_preferenceSync) return _preferences.GetValueOrDefault((accountId, key));
    }

    // A null or empty value removes the choice, which puts that page back to its default.
    public void SetPreference(Guid accountId, string key, string? value)
    {
        lock (_preferenceSync)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (!_preferences.Remove((accountId, key))) return;
                TryDirect("A display choice couldn't be cleared",
                    ("DELETE FROM AccountPreferences WHERE AccountId = $account AND Key = $key;", [("$account", accountId.ToString()), ("$key", key)]));
                return;
            }
            if (_preferences.GetValueOrDefault((accountId, key)) == value) return;
            _preferences[(accountId, key)] = value;
            TryDirect("A display choice couldn't be saved",
                ("INSERT OR REPLACE INTO AccountPreferences (AccountId, Key, Value) VALUES ($account, $key, $value);", [("$account", accountId.ToString()), ("$key", key), ("$value", value)]));
        }
    }

    // For a factory reset: the accounts they belonged to are gone.
    public void ClearPreferences()
    {
        lock (_preferenceSync)
        {
            _preferences.Clear();
            ExecuteDirect("DELETE FROM AccountPreferences;");
        }
    }

    private void LoadPreferences()
    {
        lock (_preferenceSync)
        {
            _preferences.Clear();
            using var connection = new SqliteConnection($"Data Source={_path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT AccountId, Key, Value FROM AccountPreferences;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (Guid.TryParse(reader.GetString(0), out var account)) _preferences[(account, reader.GetString(1))] = reader.GetString(2);
        }
    }

    private static void EnsurePreferenceSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS AccountPreferences (AccountId TEXT NOT NULL, Key TEXT NOT NULL, Value TEXT NOT NULL, PRIMARY KEY (AccountId, Key));";
        command.ExecuteNonQuery();
    }
}
