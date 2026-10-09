using System.Globalization;
using System.Text.Json;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Reading and writing the database: load, save (one transaction that writes what changed - see SaveChanges - and records
// the audit diff), and the audit log.
public sealed partial class HelpdeskStore
{
    private StoreData Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        EnsureSchema(connection);
        EnsureOwnerIndexes(connection);
        EnsureHealthSchema(connection);
        EnsureSpiceworksSchema(connection);
        // Write-ahead logging: a save appends its changes to helpdesk.db-wal, which SQLite folds back into the main file
        // as it goes, so writing is quicker and a backup or a reader never waits for a save. It is a property of the file,
        // so setting it again at each start costs nothing.
        ExecuteScalar(connection, "PRAGMA journal_mode = WAL;");

        var version = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SchemaVersion';");
        if (version is null)
        {
            StoreData? migrated = null;
            var payload = TableExists(connection, "Store") ? ExecuteScalar(connection, "SELECT Payload FROM Store WHERE Id = 1;") : null;
            if (payload is string json && !string.IsNullOrWhiteSpace(json))
            {
                try { migrated = JsonSerializer.Deserialize<StoreData>(json); } catch (JsonException) { }
            }
            if (migrated is null && File.Exists(_legacyPath))
            {
                try { migrated = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(_legacyPath)); } catch (JsonException) { }
            }
            if (migrated is not null)
            {
                foreach (var team in migrated.Technicians.Select(x => x.Team).Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (!migrated.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase)) migrated.TechnicianTeams.Add(team);
                foreach (var department in migrated.Users.Select(x => x.Department).Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (!migrated.Departments.Contains(department, StringComparer.OrdinalIgnoreCase)) migrated.Departments.Add(department);
                using var transaction = connection.BeginTransaction();
                using var statements = new StatementScope(transaction);
                WriteData(connection, transaction, migrated);
                SetMetadata(connection, transaction, "SchemaVersion", "8");
                DropLegacyStore(connection, transaction);
                transaction.Commit();
                return migrated;
            }
            using var marker = connection.CreateCommand();
            marker.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES ('SchemaVersion', '5');";
            marker.ExecuteNonQuery();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS Store;";
            drop.ExecuteNonQuery();
        }
        else if (TableExists(connection, "Store"))
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS Store;";
            drop.ExecuteNonQuery();
        }
        return ReadData(connection);
    }

    private void Save() => Persist(auditChanges: true);

    // Saves without auditing the differences: used at startup and after seeding, when the change is not a user action.
    private void SaveBaseline() => Persist(auditChanges: false);

    private void Persist(bool auditChanges)
    {
        // Timed from here, so the audit comparison counts too: the health check shows how long a save really takes.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var current = AuditTracker.Take(_data);
        var entries = new List<AuditEntry>(_pendingAudit);
        if (auditChanges && _snapshot is not null) entries.AddRange(AuditTracker.Diff(_snapshot, current, DateTime.UtcNow));
        // Every audit entry funnels through here, whether it came from diffing or was queued by a mutator, so this is
        // the one place attribution has to happen. It applies to baseline saves too: a factory reset does not diff, but
        // it is still very much something a person did. An entry that already named its actor keeps it.
        var actor = CurrentActor();
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].By is null) entries[i] = entries[i] with { By = actor };

        RowCapture wanted;
        bool full;
        int rowsWritten;
        try
        {
            using var connection = new SqliteConnection($"Data Source={_path}");
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var statements = new StatementScope(transaction);
            // Foreign keys are checked when the transaction commits rather than after each statement, so the changes
            // can go in table by table without a moment's inconsistency between them counting as an error.
            using (var defer = connection.CreateCommand())
            {
                defer.Transaction = transaction;
                defer.CommandText = "PRAGMA defer_foreign_keys = ON;";
                defer.ExecuteNonQuery();
            }
            // Only what changed since the last save, when that is known and can be worked out safely (SaveChanges);
            // otherwise every table is emptied and written again.
            wanted = CaptureRows(connection, transaction, _data, _written);
            _shapes ??= ReadShapes(connection, transaction);
            var plan = _written is null ? null : PlanChanges(_written, wanted, _shapes);
            full = plan is null;
            if (plan is null)
            {
                ClearTables(connection, transaction);
                wanted.Replay(connection, transaction);
                rowsWritten = wanted.RowCount;
            }
            else
            {
                foreach (var (sql, values) in plan) Execute(connection, transaction, sql, values);
                rowsWritten = plan.Count;
            }
            SetMetadata(connection, transaction, "SchemaVersion", "5");
            foreach (var entry in entries)
                Execute(connection, transaction, "INSERT INTO AuditLog (At, Area, EntityType, EntityKey, Entity, Action, Details, Actor, ActorId) VALUES ($at,$area,$type,$key,$entity,$action,$details,$actor,$actorid);",
                    ("$at", Iso(entry.At)), ("$area", entry.Area), ("$type", entry.EntityType), ("$key", entry.EntityKey), ("$entity", entry.Entity), ("$action", entry.Action), ("$details", entry.Details),
                    ("$actor", entry.By?.Name), ("$actorid", entry.By?.Id?.ToString()));
            transaction.Commit();
        }
        catch (Exception ex)
        {
            // Every mutator changes the in-memory data first and saves last. If the save fails, the transaction rolls
            // back but memory would keep the change - showing people data that vanishes at the next restart, and
            // making every later save carry it again. So memory goes back to what the database holds, and the caller
            // is told nothing was changed (SaveFailureFilter turns that into a message on the page).
            throw new SaveFailedException(ex, restored: RestoreFromDatabase());
        }

        _audit.AddRange(entries);
        _pendingAudit.Clear();
        _snapshot = current;
        // A capture whose rows couldn't be compared this time can't be compared next time either.
        _written = wanted.Comparable ? wanted : null;
        RecordSaveTiming(started, full, rowsWritten);
    }

    // Called under the lock after a failed save. If even reading fails, the unsaved change stays in memory - there is
    // nothing better to go back to - and the next successful save writes it after all.
    private bool RestoreFromDatabase()
    {
        _pendingAudit.Clear();
        // What the database holds is no longer known row by row, so the next save rewrites everything.
        _written = null;
        try
        {
            _data = Load();
            Prepare();
            _snapshot = AuditTracker.Take(_data);
            return true;
        }
        catch (Exception ex)
        {
            LogProblem(ex, "A save failed and the data couldn't be reloaded either");
            return false;
        }
    }

    // An event that changes no data - a sign-in lockout - written straight to the audit log. Save would compare all the
    // data to record one line, and someone hammering the sign-in page shouldn't be able to make the helpdesk do that.
    // Never throws: failing to record a lockout mustn't turn into an error page on the sign-in form.
    public void RecordEvent(AuditEntry entry)
    {
        lock (_sync)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_path}");
                connection.Open();
                using var transaction = connection.BeginTransaction();
                using var statements = new StatementScope(transaction);
                Execute(connection, transaction, "INSERT INTO AuditLog (At, Area, EntityType, EntityKey, Entity, Action, Details, Actor, ActorId) VALUES ($at,$area,$type,$key,$entity,$action,$details,$actor,$actorid);",
                    ("$at", Iso(entry.At)), ("$area", entry.Area), ("$type", entry.EntityType), ("$key", entry.EntityKey), ("$entity", entry.Entity), ("$action", entry.Action), ("$details", entry.Details),
                    ("$actor", entry.By?.Name), ("$actorid", entry.By?.Id?.ToString()));
                transaction.Commit();
                _audit.Add(entry);
            }
            catch (Exception ex)
            {
                LogProblem(ex, $"Couldn't record '{entry.Action}' in the audit log");
            }
        }
    }

    // How many audit-log lines one area has had since a moment - the Settings overview's count of recent lockouts.
    public int CountAuditEntries(string area, DateTime sinceUtc)
    {
        lock (_sync) return _audit.Count(x => x.At >= sinceUtc && string.Equals(x.Area, area, StringComparison.OrdinalIgnoreCase));
    }

    // Lockouts only - the Sign-in area also records two-step sign-in being set up, reset and so on (SignInThrottle).
    public int CountLockouts(DateTime sinceUtc)
    {
        lock (_sync) return _audit.Count(x => x.At >= sinceUtc && x.Area == "Sign-in" && x.Action is "Locked out" or "Address blocked");
    }

    private List<AuditEntry> LoadAudit()
    {
        var entries = new List<AuditEntry>();
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT At, Area, EntityType, EntityKey, Entity, Action, Details, Actor, ActorId FROM AuditLog ORDER BY Id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            entries.Add(new AuditEntry(Date(reader, 0), reader.GetString(1), NullableString(reader, 2), NullableString(reader, 3), reader.GetString(4), reader.GetString(5), reader.GetString(6))
            {
                By = ReadActor(reader, 7, 8)
            });
        return entries;
    }

    // Everything that has happened, newest first: the audit log plus the history and comments already kept on tickets and assets.
    public IReadOnlyList<AuditEntry> GetAuditEntries()
    {
        lock (_sync)
        {
            var entries = new List<AuditEntry>(_audit);
            foreach (var ticket in _data.Tickets)
            {
                var label = $"#{ticket.Number} {ticket.Title}";
                var key = ticket.Number.ToString();
                entries.AddRange(ticket.History.Select(x => new AuditEntry(x.CreatedAt, "Tickets", "Ticket", key, label, x.Action, x.Details) { By = x.By }));
                entries.AddRange(ticket.Comments.Where(x => !x.Text.StartsWith("(Merged from #", StringComparison.Ordinal))
                    .Select(x => new AuditEntry(x.CreatedAt, "Tickets", "Ticket", key, label, x.IsInternal ? "Internal note added" : "Comment added", x.Text) { By = x.By }));
            }
            foreach (var asset in _data.Assets)
            {
                var key = asset.Id.ToString();
                entries.AddRange(asset.History.Select(x => new AuditEntry(x.CreatedAt, "Assets", "Asset", key, asset.AssetTag, x.Action, x.Details) { By = x.By }));
                entries.AddRange(asset.Comments.Select(x => new AuditEntry(x.CreatedAt, "Assets", "Asset", key, asset.AssetTag, "Comment added", x.Text) { By = x.By }));
            }
            foreach (var part in _data.Parts)
            {
                var key = part.Id.ToString();
                entries.AddRange(part.History.Select(x => new AuditEntry(x.CreatedAt, "Parts", "Part", key, part.Name, x.Action, x.Details) { By = x.By }));
            }
            foreach (var project in _data.Projects)
            {
                var key = project.Number.ToString();
                var label = $"{project.Reference} {project.Title}";
                entries.AddRange(project.History.Select(x => new AuditEntry(x.CreatedAt, "Projects", "Project", key, label, x.Action, x.Details) { By = x.By }));
                entries.AddRange(project.Notes.Select(x => new AuditEntry(x.CreatedAt, "Projects", "Project", key, label, x.IsInternal ? "Internal note added" : "Note added", x.Text) { By = x.By }));
            }
            return entries.OrderByDescending(x => x.At).ToList();
        }
    }

    private static string Iso(DateTime value) => value.ToUniversalTime().ToString("O");
    private static string? IsoDay(DateOnly? value) => value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static DateOnly? NullableDateOnly(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) || !DateOnly.TryParseExact(reader.GetString(index), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value) ? null : value;
    private static DateTime Date(SqliteDataReader reader, int index) => DateTime.Parse(reader.GetString(index), null, System.Globalization.DateTimeStyles.RoundtripKind);
    private static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static Guid? NullableGuid(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : Guid.Parse(reader.GetString(index));
    // The name column is what decides whether there is an actor at all: rows written before attribution was added have
    // neither, and a portal action has a name but no account id.
    private static Actor? ReadActor(SqliteDataReader reader, int nameIndex, int idIndex) =>
        NullableString(reader, nameIndex) is { Length: > 0 } name ? new Actor(NullableGuid(reader, idIndex), name) : null;

    private static StoreData ReadData(SqliteConnection connection)
    {
        var data = new StoreData();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Email, Department, Location, PasswordHash, IsActive, CanRaiseProjects, IsProjectLead, RequirePasswordChange, LeftAt, AnonymisedAt FROM Users;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Users.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), NullableString(reader, 3) ?? "", NullableString(reader, 4) ?? "", NullableString(reader, 5), reader.GetInt32(6) != 0)
            {
                CanRaiseProjects = reader.GetInt32(7) != 0,
                IsProjectLead = reader.GetInt32(8) != 0,
                RequirePasswordChange = reader.GetInt32(9) != 0,
                LeftAt = reader.IsDBNull(10) ? null : Date(reader, 10),
                AnonymisedAt = reader.IsDBNull(11) ? null : Date(reader, 11)
            });
        }
        ReadStrings(connection, "TechnicianTeams", data.TechnicianTeams);
        ReadStrings(connection, "Departments", data.Departments);
        ReadStrings(connection, "Locations", data.Locations);
        ReadStrings(connection, "AssetTypes", data.AssetTypes);
        ReadStrings(connection, "AssetMakes", data.AssetMakes);
        ReadStrings(connection, "AssetModels", data.AssetModels);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Model, Make FROM AssetModelMakes;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetModelMakes[reader.GetString(0)] = reader.GetString(1);
        }
        ReadStrings(connection, "AssetStatuses", data.AssetStatuses);
        ReadStrings(connection, "PartCategories", data.PartCategories);
        ReadStrings(connection, "PartLocations", data.PartLocations);
        ReadStrings(connection, "LoanReasons", data.LoanReasons);
        ReadStrings(connection, "Buildings", data.Buildings);
        ReadStrings(connection, "AssetConditions", data.AssetConditions);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Notes, CreatedAt, IsRetired FROM LoanKits;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.LoanKits.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), Date(reader, 3))
                {
                    IsRetired = reader.GetInt32(4) != 0
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT KitId, AssetId FROM LoanKitAssets;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.LoanKits.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.LoanKits[index] = data.LoanKits[index] with { AssetIds = [.. data.LoanKits[index].AssetIds, Guid.Parse(reader.GetString(1))] };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, KitId, BorrowerUserId, BorrowerName, Reason, IssuedAt, DueBack, ReturnedAt, IssuedBy, Notes FROM KitLoans;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.KitLoans.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), NullableGuid(reader, 2),
                    reader.GetString(3), reader.GetString(4), Date(reader, 5), NullableDateOnly(reader, 6) ?? AssetInsights.Today,
                    reader.IsDBNull(7) ? null : Date(reader, 7), reader.GetString(8), reader.GetString(9)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AssetType, Years FROM AssetTypeLifespans;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetTypeLifespans[reader.GetString(0)] = reader.GetInt32(1);
        }
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'AssetReviewDays';") as string, out var reviewDays)) data.AssetReviewDays = reviewDays;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'CheckDueSoonDays';") as string, out var checkDueSoon)) data.CheckDueSoonDays = checkDueSoon;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SupportWarningDays';") as string, out var supportWarning)) data.SupportWarningDays = supportWarning;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'CheckIntervalMonths';") as string, out var checkInterval)) data.CheckIntervalMonths = checkInterval;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'AcademicYearStartMonth';") as string, out var academicStart)) data.AcademicYearStartMonth = academicStart;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'PermissionModelVersion';") as string, out var permissionVersion)) data.PermissionModelVersion = permissionVersion;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'TicketDueSoonHours';") as string, out var dueSoonHours)) data.TicketDueSoonHours = dueSoonHours;
        data.RequireTwoFactor = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'RequireTwoFactor';") as string == "1";
        data.SiteAddress = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SiteAddress';") as string ?? "";
        data.AllowRecoveryKeys = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'AllowRecoveryKeys';") as string != "0";
        data.ProjectPageIncludesVat = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'ProjectPageIncludesVat';") as string == "1";
        data.NotificationsEnabled = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'NotificationsEnabled';") as string != "0";
        data.StaffNotificationsEnabled = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'StaffNotificationsEnabled';") as string != "0";
        data.RequesterNotificationsEnabled = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'RequesterNotificationsEnabled';") as string != "0";
        data.NotificationPrompt = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'NotificationPrompt';") as string != "0";
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'PartsDefaultReorderThreshold';") as string, out var reorderThreshold)) data.PartsDefaultReorderThreshold = reorderThreshold;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LoanRepeatCount';") as string, out var loanCount)) data.LoanRepeatCount = loanCount;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LoanRepeatDays';") as string, out var loanDays) ) data.LoanRepeatDays = loanDays;
        // Stored as day numbers (0 = Sunday). Missing means a database from before the setting existed: Monday to Friday.
        if (ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SchoolDays';") is string schoolDays)
            data.SchoolDays = schoolDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var day) && day is >= 0 and <= 6 ? (DayOfWeek?)day : null).OfType<DayOfWeek>().Distinct().ToList();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Name, StartTime, EndTime FROM SchoolPeriods ORDER BY Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (TimeOnly.TryParse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture, out var start)
                    && TimeOnly.TryParse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture, out var end))
                    data.Periods.Add(new SchoolPeriod(reader.GetString(0), start, end));
        }
        ReadStrings(connection, "Categories", data.Categories);
        ReadStrings(connection, "Statuses", data.Statuses);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Status, Description FROM StatusDescriptions;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.StatusDescriptions[reader.GetString(0)] = reader.GetString(1);
        }
        ReadStrings(connection, "Priorities", data.Priorities);
        ReadStrings(connection, "RequireCloseMessagePriorities", data.RequireCloseMessagePriorities);
        ReadStrings(connection, "RequireCloseMessageCategories", data.RequireCloseMessageCategories);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Duration, DurationUnit, Priority, Description FROM Slas ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slaId = Guid.Parse(reader.GetString(0));
                var legacyPriority = NullableString(reader, 4);
                data.Slas.Add(new(slaId, reader.GetString(1), reader.GetInt32(2), NormalizeDurationUnit(reader.GetString(3)), NullableString(reader, 5))
                {
                    Priorities = legacyPriority is not null ? [legacyPriority] : []
                });
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT SlaId, Priority FROM SlaPriorities;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slaId = Guid.Parse(reader.GetString(0));
                var priority = reader.GetString(1);
                var index = data.Slas.FindIndex(x => x.Id == slaId);
                if (index >= 0 && !data.Slas[index].Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase))
                    data.Slas[index] = data.Slas[index] with { Priorities = data.Slas[index].Priorities.Append(priority).ToList() };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT SlaId, Category FROM SlaCategories;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slaId = Guid.Parse(reader.GetString(0));
                var category = reader.GetString(1);
                var index = data.Slas.FindIndex(x => x.Id == slaId);
                if (index >= 0) data.Slas[index] = data.Slas[index] with { Categories = data.Slas[index].Categories.Append(category).ToList() };
            }
        }
        using (var command = connection.CreateCommand())
        {
            // The legacy single AssetType column seeds the scope; rows in AssetAttributeAssetTypes are merged in.
            command.CommandText = "SELECT Id, Name, AssetType, FieldType, Choices FROM AssetAttributeDefinitions ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var legacyAssetType = NullableString(reader, 2);
                data.AssetAttributeDefinitions.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(3), reader.GetString(4))
                {
                    AssetTypes = string.IsNullOrWhiteSpace(legacyAssetType) ? [] : [legacyAssetType]
                });
            }
        }
        ReadAttributeScope(connection, "AssetAttributeAssetTypes", "AssetType", (id, assetType) =>
        {
            var definition = data.AssetAttributeDefinitions.FirstOrDefault(x => x.Id == id);
            if (definition is not null && !definition.AssetTypes.Contains(assetType, StringComparer.OrdinalIgnoreCase)) definition.AssetTypes.Add(assetType);
        });
        using (var command = connection.CreateCommand())
        {
            // The legacy single Category column seeds the scope; rows in TicketAttributeCategories are merged in.
            command.CommandText = "SELECT Id, Name, Category, FieldType, Choices FROM TicketAttributeDefinitions ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var legacyCategory = NullableString(reader, 2);
                data.TicketAttributeDefinitions.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(3), reader.GetString(4))
                {
                    Categories = string.IsNullOrWhiteSpace(legacyCategory) ? [] : [legacyCategory]
                });
            }
        }
        ReadAttributeScope(connection, "TicketAttributeCategories", "Category", (id, category) =>
        {
            var definition = data.TicketAttributeDefinitions.FirstOrDefault(x => x.Id == id);
            if (definition is not null && !definition.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)) definition.Categories.Add(category);
        });
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, AttributeDefinitionId, Value FROM TicketAttributeValues;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketAttributeValues.Add(new(reader.GetInt32(0), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            // The nine Allow* columns from the original permission model are still in the table but are neither read
            // nor written: roles were emptied when permissions moved to independent ticks, so there is nothing left to
            // convert from. They stay only because dropping a column rewrites the table.
            command.CommandText = "SELECT Name, IsProtected FROM Roles ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Roles.Add(new RoleRecord(reader.GetString(0), reader.GetInt32(1) != 0));
        }
        using (var command = connection.CreateCommand())
        {
            // One row per ticked box, stored as "Assets:Edit". Nothing is implied by anything else, so every action a
            // role holds has its own row. Anything without a colon is a flag.
            command.CommandText = "SELECT RoleName, Permission FROM RolePermissions;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var role = data.Roles.FirstOrDefault(x => string.Equals(x.Name, reader.GetString(0), StringComparison.OrdinalIgnoreCase));
                if (role is null) continue;
                var permission = reader.GetString(1);
                var split = permission.IndexOf(':');
                if (split < 0) { role.Flags.Add(permission); continue; }
                var action = ModulePermissions.Parse(permission[(split + 1)..]);
                if (action != ModulePermission.None) role.Grants[permission[..split]] = role.GrantsFor(permission[..split]) | action;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Email, Team, Role, PasswordHash, RequirePasswordChange, IsActive, TotpSecret, TwoFactorSince, TotpLastStep, RecoveryCodes, RecoveryKeyHash, RecoveryKeySince FROM Technicians;";
            using var reader = command.ExecuteReader();
            // Trusts the stored value as-is rather than validating against Roles here: on first run after an upgrade the
            // Roles table is still being seeded (see EnsureSeedRoles, called after Load()), so it can't be checked yet.
            // Permission lookups (RoleGrants) are case-insensitive, so this doesn't need to be exact.
            while (reader.Read()) data.Technicians.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), NullableString(reader, 3) ?? "",
                NullableString(reader, 4) is { Length: > 0 } role ? role : StaffRoles.DefaultRole, NullableString(reader, 5), reader.GetInt32(6) != 0, reader.GetInt32(7) != 0)
            {
                TwoFactor = ReadTwoFactor(reader),
                RecoveryKey = ReadRecoveryKey(reader)
            });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AssetId, AttributeDefinitionId, Value FROM AssetAttributeValues;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetAttributeValues.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, ContactName, Email, Phone, AddressLine1, AddressLine2, City, StateRegion, PostalCode, Country, Website, Notes, CreatedAt FROM Suppliers;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Suppliers.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), Date(reader, 13)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Sku, Category, QuantityOnHand, CreatedAt, Location, ReorderThreshold FROM Parts;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.Parts.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), Date(reader, 5))
                {
                    Location = NullableString(reader, 6) ?? "",
                    ReorderThreshold = reader.IsDBNull(7) ? null : reader.GetInt32(7)
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PartId, SupplierId FROM PartSuppliers;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.Parts.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.Parts[index] = data.Parts[index] with { SupplierIds = [.. data.Parts[index].SupplierIds, Guid.Parse(reader.GetString(1))] };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PartId, AssetType FROM PartAssetTypes;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.Parts.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.Parts[index] = data.Parts[index] with { AssetTypes = [.. data.Parts[index].AssetTypes, reader.GetString(1)] };
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT EntityType, EntityKey FROM DemoRecords;";
            using var demoReader = command.ExecuteReader();
            while (demoReader.Read()) data.DemoRecords.Add(new DemoRecord(demoReader.GetString(0), demoReader.GetString(1)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PartId, Action, Details, CreatedAt, Actor, ActorId FROM PartActivities ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var index = data.Parts.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index >= 0) data.Parts[index].History.Add(new(reader.GetString(1), reader.GetString(2), Date(reader, 3)) { By = ReadActor(reader, 4, 5) });
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, PartId, Quantity FROM TicketParts;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketParts.Add(new(reader.GetInt32(0), Guid.Parse(reader.GetString(1)), reader.GetInt32(2)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, TicketType, Title, Description, Category, Priority, SlaId, HelperLine, ShowInPortal, PortalOrder FROM TicketTemplates ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketTemplates.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), TicketTypes.Normalize(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), NullableGuid(reader, 7))
            {
                HelperLine = reader.GetString(8),
                ShowInPortal = reader.GetInt32(9) != 0,
                PortalOrder = reader.GetInt32(10)
            });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Category, Name, DefaultPriority, HelperLine, ShowInPortal, PortalOrder FROM ServiceItems ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.ServiceItems.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5) != 0, reader.GetInt32(6)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Category, Icon, Color FROM CategoryStyles ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.CategoryStyles[reader.GetString(0)] = new CategoryStyle(reader.GetString(1), reader.GetString(2));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TemplateId, AttributeDefinitionId, Value FROM TicketTemplateAttributes;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (data.TicketTemplates.FirstOrDefault(x => x.Id == Guid.Parse(reader.GetString(0))) is { } template)
                    template.AttributeValues[Guid.Parse(reader.GetString(1))] = reader.GetString(2);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, TicketNumber, FileName, ContentType, Size, UploadedAt, VisibleToRequester, FromRequester FROM TicketAttachments ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketAttachments.Add(new(Guid.Parse(reader.GetString(0)), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), Date(reader, 5))
            {
                VisibleToRequester = reader.GetInt32(6) != 0,
                FromRequester = reader.GetInt32(7) != 0
            });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, LinkedNumber, Kind FROM TicketLinks ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketLinks.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            // This reader is ordinal-indexed, so new columns are appended to the END of the SELECT. Inserting one in the
            // middle shifts every ordinal after it and silently scrambles the whole register - which is exactly how the
            // Type/Model swap bug happened.
            command.CommandText = "SELECT Id, AssetTag, Make, Type, Model, SerialNumber, Location, AssignedUserId, SupplierId, Status, PurchaseDate, PurchasePrice, PurchaseOrder, WarrantyEnd, ReplacementDate, LoanDueDate, QuoteReference, DisposalDate, DisposalMethod, DisposalProceeds, Building, OperatingSystem, Condition, Ownership, LastCheckDate, LastCheckBy, NextCheckDate, EndOfSupport, DisposedBy, DisposalCertificate, ContractId FROM Assets;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Assets.Add(new AssetRecord(Guid.Parse(reader.GetString(0)), NullableString(reader, 1) ?? "", NullableString(reader, 2) ?? "", NullableString(reader, 4) ?? "", NullableString(reader, 3) ?? "", NullableString(reader, 5) ?? "", NullableString(reader, 6) ?? "", NullableGuid(reader, 7), NullableGuid(reader, 8))
            {
                Status = NullableString(reader, 9) ?? "In use",
                PurchaseDate = NullableDateOnly(reader, 10),
                PurchasePrice = NullableString(reader, 11) is { } price && decimal.TryParse(price, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice) ? parsedPrice : null,
                PurchaseOrder = NullableString(reader, 12) ?? "",
                WarrantyEnd = NullableDateOnly(reader, 13),
                ReplacementDate = NullableDateOnly(reader, 14),
                LoanDueDate = NullableDateOnly(reader, 15),
                QuoteReference = NullableString(reader, 16) ?? "",
                DisposalDate = NullableDateOnly(reader, 17),
                DisposalMethod = NullableString(reader, 18) ?? "",
                DisposalProceeds = NullableString(reader, 19) is { } proceeds && decimal.TryParse(proceeds, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsedProceeds) ? parsedProceeds : null,
                Building = NullableString(reader, 20) ?? "",
                OperatingSystem = NullableString(reader, 21) ?? "",
                Condition = NullableString(reader, 22) ?? "",
                Ownership = AssetOwnership.Normalize(NullableString(reader, 23)),
                LastCheckDate = NullableDateOnly(reader, 24),
                LastCheckBy = NullableString(reader, 25) ?? "",
                NextCheckDate = NullableDateOnly(reader, 26),
                EndOfSupport = NullableDateOnly(reader, 27),
                DisposedBy = NullableString(reader, 28) ?? "",
                DisposalCertificate = NullableString(reader, 29) ?? "",
                ContractId = NullableGuid(reader, 30)
            });
        }
        ReadAssetChildren(connection, data.Assets);
        var ticketAssetMap = new Dictionary<int, List<Guid>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, AssetId FROM TicketAssets ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var number = reader.GetInt32(0);
                var assetId = Guid.Parse(reader.GetString(1));
                if (!ticketAssetMap.TryGetValue(number, out var list)) ticketAssetMap[number] = list = [];
                list.Add(assetId);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Number, Title, Description, RequesterId, AssetId, TechnicianId, Priority, Status, Category, CreatedAt, ClosedAt, SlaId, DueDate, DueDateOverridden, SlaOverridden, TeamName, TicketType, Location, RequesterSeenAt, SubCategory FROM Tickets;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var number = reader.GetInt32(0);
                var legacyAssetId = NullableGuid(reader, 4);
                List<Guid> assetIds;
                if (ticketAssetMap.TryGetValue(number, out var mapped)) assetIds = mapped;
                else if (legacyAssetId.HasValue) assetIds = new List<Guid> { legacyAssetId.Value };
                else assetIds = new List<Guid>();
                var ticket = new TicketRecord(number, reader.GetString(1), reader.GetString(2), Guid.Parse(reader.GetString(3)),
                    assetIds, NullableGuid(reader, 5), reader.GetString(6), reader.GetString(7), reader.GetString(8), Date(reader, 9),
                    reader.IsDBNull(10) ? null : Date(reader, 10), NullableGuid(reader, 11), reader.IsDBNull(12) ? null : Date(reader, 12), !reader.IsDBNull(13) && reader.GetInt32(13) != 0, !reader.IsDBNull(14) && reader.GetInt32(14) != 0, NullableString(reader, 15), NullableString(reader, 17))
                {
                    Type = TicketTypes.Normalize(NullableString(reader, 16)),
                    RequesterSeenAt = reader.IsDBNull(18) ? null : Date(reader, 18),
                    SubCategory = NullableString(reader, 19) ?? ""
                };
                data.Tickets.Add(ticket);
            }
        }
        ReadTicketChildren(connection, data.Tickets);
        ReadTicketProcess(connection, data);
        ReadLifecycle(connection, data);
        // The highest number ever handed out, not just the highest still present: deleting the newest ticket must not
        // give its number to the next one, when job sheets and conversations already carry it.
        data.LastTicketNumber = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(Number), 1000) FROM Tickets;"));
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LastTicketNumber';") as string, out var lastTicket))
            data.LastTicketNumber = Math.Max(data.LastTicketNumber, lastTicket);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT BrandName, DashboardEyebrow, DashboardTitle, DashboardDescription, PrimaryColor, AccentColor, BackgroundColor, DarkMode FROM BrandingSettings WHERE Id = 1;";
            using var reader = command.ExecuteReader();
            if (reader.Read()) data.Branding = new() { BrandName = reader.GetString(0), DashboardEyebrow = reader.GetString(1), DashboardTitle = reader.GetString(2), DashboardDescription = reader.GetString(3), PrimaryColor = reader.GetString(4), AccentColor = reader.GetString(5), BackgroundColor = reader.GetString(6), DefaultAppearance = Enum.IsDefined((Appearance)reader.GetInt32(7)) ? (Appearance)reader.GetInt32(7) : Appearance.Light };
        }
        ReadProjects(connection, data);
        ReadContracts(connection, data);
        ReadProjectTickets(connection, data);
        ReadBackupSettings(connection, data);
        ReadHealth(connection, data);
        ReadSpiceworks(connection, data);
        ReadOnboarding(connection, data);
        ReadTeams(connection, data);
        return data;
    }

    private static void ReadAttributeScope(SqliteConnection connection, string table, string column, Action<Guid, string> add)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT AttributeId, {column} FROM {table};";
        using var reader = command.ExecuteReader();
        while (reader.Read()) add(Guid.Parse(reader.GetString(0)), reader.GetString(1));
    }

    private static void ReadStrings(SqliteConnection connection, string table, List<string> target)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Name FROM {table} ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) target.Add(reader.GetString(0));
    }

    // Each child table is read once, in order, and handed to its ticket or asset - not queried once per record. The
    // tables have no index on the parent's key, so a query per record read the whole child table every time: with
    // 40,000 tickets that was billions of rows and a start-up measured in hours.
    private static void ReadTicketChildren(SqliteConnection connection, List<TicketRecord> tickets)
    {
        var byNumber = new Dictionary<int, TicketRecord>(tickets.Count);
        foreach (var ticket in tickets) byNumber[ticket.Number] = ticket;
        using (var comments = connection.CreateCommand())
        {
            comments.CommandText = "SELECT TicketNumber, Text, CreatedAt, IsInternal, Actor, ActorId, FromRequester FROM TicketComments ORDER BY Id;";
            using var reader = comments.ExecuteReader();
            while (reader.Read())
                if (byNumber.TryGetValue(reader.GetInt32(0), out var ticket))
                    ticket.Comments.Add(new(reader.GetString(1), Date(reader, 2), reader.GetInt32(3) != 0) { By = ReadActor(reader, 4, 5), FromRequester = reader.GetInt32(6) != 0 });
        }
        using (var activities = connection.CreateCommand())
        {
            activities.CommandText = "SELECT TicketNumber, Action, Details, CreatedAt, Actor, ActorId FROM TicketActivities ORDER BY Id;";
            using var reader = activities.ExecuteReader();
            while (reader.Read())
                if (byNumber.TryGetValue(reader.GetInt32(0), out var ticket))
                    ticket.History.Add(new(reader.GetString(1), reader.GetString(2), Date(reader, 3)) { By = ReadActor(reader, 4, 5) });
        }
    }

    private static void ReadAssetChildren(SqliteConnection connection, List<AssetRecord> assets)
    {
        var byId = new Dictionary<Guid, AssetRecord>(assets.Count);
        foreach (var asset in assets) byId[asset.Id] = asset;
        AssetRecord? Owner(SqliteDataReader reader) => Guid.TryParse(reader.GetString(0), out var id) && byId.TryGetValue(id, out var asset) ? asset : null;
        using (var comments = connection.CreateCommand())
        {
            comments.CommandText = "SELECT AssetId, Text, CreatedAt, Actor, ActorId FROM AssetComments ORDER BY Id;";
            using var reader = comments.ExecuteReader();
            while (reader.Read())
                Owner(reader)?.Comments.Add(new(reader.GetString(1), Date(reader, 2)) { By = ReadActor(reader, 3, 4) });
        }
        using (var activities = connection.CreateCommand())
        {
            activities.CommandText = "SELECT AssetId, Action, Details, CreatedAt, Actor, ActorId FROM AssetActivities ORDER BY Id;";
            using var reader = activities.ExecuteReader();
            while (reader.Read())
                Owner(reader)?.History.Add(new(reader.GetString(1), reader.GetString(2), Date(reader, 3)) { By = ReadActor(reader, 4, 5) });
        }
        using (var assignments = connection.CreateCommand())
        {
            assignments.CommandText = "SELECT AssetId, UserId, UserName, StartedAt, EndedAt, DueBack, Reason, KitLoanId FROM AssetAssignments ORDER BY Id;";
            using var reader = assignments.ExecuteReader();
            while (reader.Read())
                Owner(reader)?.Assignments.Add(new AssetAssignment(NullableGuid(reader, 1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : Date(reader, 3), reader.IsDBNull(4) ? null : Date(reader, 4), NullableDateOnly(reader, 5))
                {
                    Reason = NullableString(reader, 6),
                    KitLoanId = NullableGuid(reader, 7)
                });
        }
    }
    private static void WriteData(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        // Looked up once rather than searched for every row: with thousands of tickets and assets, "does this asset
        // still exist?" asked of the whole list for each ticket was hundreds of millions of comparisons per save.
        var assetIds = data.Assets.Select(x => x.Id).ToHashSet();
        var partIds = data.Parts.Select(x => x.Id).ToHashSet();
        var supplierIds = data.Suppliers.Select(x => x.Id).ToHashSet();
        var kitIds = data.LoanKits.Select(x => x.Id).ToHashSet();
        var assetDefinitionIds = data.AssetAttributeDefinitions.Select(x => x.Id).ToHashSet();
        var ticketDefinitionIds = data.TicketAttributeDefinitions.Select(x => x.Id).ToHashSet();
        var partsByTicket = data.TicketParts.ToLookup(x => x.TicketNumber);
        var valuesByTicket = data.TicketAttributeValues.ToLookup(x => x.TicketNumber);
        var attachmentsByTicket = data.TicketAttachments.ToLookup(x => x.TicketNumber);
        if (!IsCapturing(transaction)) ClearTables(connection, transaction);
        foreach (var demo in data.DemoRecords.DistinctBy(x => (x.EntityType, x.EntityKey)))
            Execute(connection, transaction, "INSERT INTO DemoRecords (EntityType, EntityKey) VALUES ($type,$key);", ("$type", demo.EntityType), ("$key", demo.EntityKey));
        InsertStrings(connection, transaction, "TechnicianTeams", data.TechnicianTeams);
        InsertStrings(connection, transaction, "Departments", data.Departments);
        InsertStrings(connection, transaction, "Locations", data.Locations);
        InsertStrings(connection, transaction, "AssetTypes", data.AssetTypes);
        InsertStrings(connection, transaction, "AssetMakes", data.AssetMakes);
        InsertStrings(connection, transaction, "AssetModels", data.AssetModels);
        InsertStrings(connection, transaction, "AssetStatuses", data.AssetStatuses);
        InsertStrings(connection, transaction, "PartCategories", data.PartCategories);
        InsertStrings(connection, transaction, "PartLocations", data.PartLocations);
        InsertStrings(connection, transaction, "LoanReasons", data.LoanReasons);
        InsertStrings(connection, transaction, "Buildings", data.Buildings);
        InsertStrings(connection, transaction, "AssetConditions", data.AssetConditions);
        SetMetadata(connection, transaction, "LoanRepeatCount", data.LoanRepeatCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "LoanRepeatDays", data.LoanRepeatDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "SchoolDays", string.Join(",", data.SchoolDays.Select(x => (int)x)));
        for (var i = 0; i < data.Periods.Count; i++)
            Execute(connection, transaction, "INSERT INTO SchoolPeriods (Position, Name, StartTime, EndTime) VALUES ($position,$name,$start,$end);",
                ("$position", i), ("$name", data.Periods[i].Name),
                ("$start", data.Periods[i].Start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
                ("$end", data.Periods[i].End.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
        // Loan kits are written after the Assets loop below, because LoanKitAssets has a foreign key to Assets.
        foreach (var pair in data.AssetTypeLifespans.Where(x => x.Value > 0 && data.AssetTypes.Contains(x.Key, StringComparer.OrdinalIgnoreCase)))
            Execute(connection, transaction, "INSERT INTO AssetTypeLifespans (AssetType, Years) VALUES ($type,$years);", ("$type", pair.Key), ("$years", pair.Value));
        SetMetadata(connection, transaction, "AssetReviewDays", data.AssetReviewDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "CheckDueSoonDays", data.CheckDueSoonDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "SupportWarningDays", data.SupportWarningDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "CheckIntervalMonths", data.CheckIntervalMonths.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "AcademicYearStartMonth", data.AcademicYearStartMonth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "PermissionModelVersion", data.PermissionModelVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "TicketDueSoonHours", data.TicketDueSoonHours.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "RequireTwoFactor", data.RequireTwoFactor ? "1" : "0");
        SetMetadata(connection, transaction, "SiteAddress", data.SiteAddress);
        SetMetadata(connection, transaction, "AllowRecoveryKeys", data.AllowRecoveryKeys ? "1" : "0");
        SetMetadata(connection, transaction, "ProjectPageIncludesVat", data.ProjectPageIncludesVat ? "1" : "0");
        SetMetadata(connection, transaction, "NotificationsEnabled", data.NotificationsEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "StaffNotificationsEnabled", data.StaffNotificationsEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "RequesterNotificationsEnabled", data.RequesterNotificationsEnabled ? "1" : "0");
        SetMetadata(connection, transaction, "NotificationPrompt", data.NotificationPrompt ? "1" : "0");
        SetMetadata(connection, transaction, "PartsDefaultReorderThreshold", data.PartsDefaultReorderThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var pair in data.AssetModelMakes.Where(x => data.AssetModels.Contains(x.Key, StringComparer.OrdinalIgnoreCase) && data.AssetMakes.Contains(x.Value, StringComparer.OrdinalIgnoreCase)))
            Execute(connection, transaction, "INSERT INTO AssetModelMakes (Model, Make) VALUES ($model,$make);", ("$model", pair.Key), ("$make", pair.Value));
        InsertStrings(connection, transaction, "Categories", data.Categories);
        InsertStrings(connection, transaction, "Statuses", data.Statuses);
        foreach (var pair in data.StatusDescriptions.Where(x => data.Statuses.Contains(x.Key, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Value)))
            Execute(connection, transaction, "INSERT INTO StatusDescriptions (Status, Description) VALUES ($status,$description);", ("$status", pair.Key), ("$description", pair.Value));
        InsertStrings(connection, transaction, "Priorities", data.Priorities);
        InsertStrings(connection, transaction, "RequireCloseMessagePriorities", data.RequireCloseMessagePriorities);
        InsertStrings(connection, transaction, "RequireCloseMessageCategories", data.RequireCloseMessageCategories);
        foreach (var sla in data.Slas)
        {
            Execute(connection, transaction, "INSERT INTO Slas (Id, Name, Duration, DurationUnit, Description) VALUES ($id,$name,$duration,$unit,$description);", ("$id", sla.Id.ToString()), ("$name", sla.Name), ("$duration", sla.Duration), ("$unit", NormalizeDurationUnit(sla.DurationUnit)), ("$description", sla.Description));
            foreach (var priority in sla.Priorities.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO SlaPriorities (SlaId, Priority) VALUES ($id,$priority);", ("$id", sla.Id.ToString()), ("$priority", priority));
            foreach (var category in sla.Categories.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO SlaCategories (SlaId, Category) VALUES ($id,$category);", ("$id", sla.Id.ToString()), ("$category", category));
        }
        foreach (var item in data.Users)
            ExecuteFor(item, 0, item, connection, transaction, "INSERT INTO Users (Id, Name, Email, Department, Location, PasswordHash, IsActive, CanRaiseProjects, IsProjectLead, RequirePasswordChange, LeftAt, AnonymisedAt) VALUES ($id,$name,$email,$department,$location,$hash,$active,$projects,$lead,$requireChange,$left,$anonymised);", static s => [("$id", s.Item.Id.ToString()), ("$name", s.Item.Name), ("$email", s.Item.Email), ("$department", s.Item.Department), ("$location", s.Item.Location), ("$hash", s.Item.PasswordHash), ("$active", s.Item.IsActive ? 1 : 0), ("$projects", s.Item.CanRaiseProjects ? 1 : 0), ("$lead", s.Item.IsProjectLead ? 1 : 0), ("$requireChange", s.Item.RequirePasswordChange ? 1 : 0), ("$left", s.Item.LeftAt is { } left ? Iso(left) : null), ("$anonymised", s.Item.AnonymisedAt is { } anonymised ? Iso(anonymised) : null)]);
        foreach (var item in data.Technicians)
            // A blank team must be written as NULL, not '' - the column has a foreign key to TechnicianTeams(Name), which only exempts NULL.
            Execute(connection, transaction, "INSERT INTO Technicians (Id, Name, Email, Team, Role, PasswordHash, RequirePasswordChange, IsActive, TotpSecret, TwoFactorSince, TotpLastStep, RecoveryCodes, RecoveryKeyHash, RecoveryKeySince) VALUES ($id,$name,$email,$team,$role,$hash,$requireChange,$active,$totp,$totpSince,$totpStep,$recovery,$keyHash,$keySince);",
                ("$id", item.Id.ToString()), ("$name", item.Name), ("$email", item.Email), ("$team", string.IsNullOrWhiteSpace(item.Team) ? null : item.Team), ("$role", string.IsNullOrWhiteSpace(item.Role) ? StaffRoles.DefaultRole : item.Role), ("$hash", item.PasswordHash), ("$requireChange", item.RequirePasswordChange ? 1 : 0), ("$active", item.IsActive ? 1 : 0),
                ("$totp", item.TwoFactor?.Secret), ("$totpSince", item.TwoFactor is { } twoFactor ? Iso(twoFactor.EnabledAt) : null), ("$totpStep", item.TwoFactor?.LastUsedStep), ("$recovery", item.TwoFactor is { } codes ? string.Join(",", codes.RecoveryCodeHashes) : null),
                ("$keyHash", item.RecoveryKey?.Hash), ("$keySince", item.RecoveryKey is { } key ? Iso(key.CreatedAt) : null));
        foreach (var item in data.Roles)
        {
            // The nine Allow* columns are NOT NULL DEFAULT 0, so leaving them out retires them cleanly - the same way
            // the SLA single-value columns were retired.
            Execute(connection, transaction, "INSERT INTO Roles (Name, IsProtected) VALUES ($name,$protected);",
                ("$name", item.Name), ("$protected", item.IsProtected ? 1 : 0));
            // One row per ticked box: "Assets:Access", "Assets:Edit", and so on.
            foreach (var module in item.Grants.Where(x => x.Value != ModulePermission.None))
                foreach (var action in ModulePermissions.Split(module.Value))
                    Execute(connection, transaction, "INSERT INTO RolePermissions (RoleName, Permission) VALUES ($role,$permission);",
                        ("$role", item.Name), ("$permission", $"{module.Key}:{action}"));
            foreach (var flag in item.Flags)
                Execute(connection, transaction, "INSERT INTO RolePermissions (RoleName, Permission) VALUES ($role,$permission);",
                    ("$role", item.Name), ("$permission", flag));
        }
        foreach (var item in data.Suppliers)
            Execute(connection, transaction, "INSERT INTO Suppliers (Id, Name, ContactName, Email, Phone, AddressLine1, AddressLine2, City, StateRegion, PostalCode, Country, Website, Notes, CreatedAt) VALUES ($id,$name,$contact,$email,$phone,$a1,$a2,$city,$state,$postal,$country,$website,$notes,$created);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$contact", item.ContactName), ("$email", item.Email), ("$phone", item.Phone), ("$a1", item.AddressLine1), ("$a2", item.AddressLine2), ("$city", item.City), ("$state", item.StateRegion), ("$postal", item.PostalCode), ("$country", item.Country), ("$website", item.Website), ("$notes", item.Notes), ("$created", Iso(item.CreatedAt)));
        // After Users, Technicians and Suppliers, which Projects and their item suppliers have foreign keys to.
        WriteProjects(connection, transaction, data);
        WriteContracts(connection, transaction, data);
        WriteProjectTickets(connection, transaction, data);
        WriteOnboarding(connection, transaction, data);
        WriteBackupSettings(connection, transaction, data);
        WriteHealth(connection, transaction, data);
        WriteSpiceworks(connection, transaction, data);
        WriteTicketProcessSettings(connection, transaction, data);
        WriteLifecycleSettings(connection, transaction, data);
        WriteTeams(connection, transaction, data);
        foreach (var item in data.Parts)
        {
            Execute(connection, transaction, "INSERT INTO Parts (Id, Name, Sku, Category, QuantityOnHand, CreatedAt, Location, ReorderThreshold) VALUES ($id,$name,$sku,$category,$quantity,$created,$location,$reorder);",
                ("$id", item.Id.ToString()), ("$name", item.Name), ("$sku", item.Sku), ("$category", item.Category), ("$quantity", item.QuantityOnHand), ("$created", Iso(item.CreatedAt)), ("$location", item.Location ?? string.Empty), ("$reorder", item.ReorderThreshold));
            foreach (var supplierId in item.SupplierIds.Distinct())
                if (supplierIds.Contains(supplierId))
                    Execute(connection, transaction, "INSERT INTO PartSuppliers (PartId, SupplierId) VALUES ($part,$supplier);", ("$part", item.Id.ToString()), ("$supplier", supplierId.ToString()));
            foreach (var assetType in item.AssetTypes.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO PartAssetTypes (PartId, AssetType) VALUES ($part,$type);", ("$part", item.Id.ToString()), ("$type", assetType));
            foreach (var activity in item.History)
                ExecuteFor(activity, item.Id, item, connection, transaction, "INSERT INTO PartActivities (PartId, Action, Details, CreatedAt, Actor, ActorId) VALUES ($id,$action,$details,$created,$actor,$actorid);", static s => [("$id", s.Item.Id.ToString()), ("$action", s.Record.Action), ("$details", s.Record.Details), ("$created", Iso(s.Record.CreatedAt)), ("$actor", s.Record.By?.Name), ("$actorid", s.Record.By?.Id?.ToString())]);
        }
        foreach (var item in data.Assets)
        {
            ExecuteFor(item, 0, item, connection, transaction, "INSERT INTO Assets (Id, AssetTag, Make, Type, Model, SerialNumber, Location, AssignedUserId, SupplierId, Status, PurchaseDate, PurchasePrice, PurchaseOrder, WarrantyEnd, ReplacementDate, LoanDueDate, QuoteReference, DisposalDate, DisposalMethod, DisposalProceeds, Building, OperatingSystem, Condition, Ownership, LastCheckDate, LastCheckBy, NextCheckDate, EndOfSupport, DisposedBy, DisposalCertificate, ContractId) VALUES ($id,$tag,$make,$type,$model,$serial,$location,$user,$supplier,$status,$purchased,$price,$po,$warranty,$replacement,$loan,$quote,$disposed,$method,$proceeds,$building,$os,$condition,$ownership,$lastcheck,$lastcheckby,$nextcheck,$eos,$disposedby,$certificate,$contract);", static s => [("$id", s.Item.Id.ToString()), ("$tag", s.Item.AssetTag), ("$make", s.Item.Make), ("$type", s.Item.Type), ("$model", s.Item.Model), ("$serial", s.Item.SerialNumber), ("$location", s.Item.Location), ("$user", s.Item.AssignedUserId?.ToString()), ("$supplier", s.Item.SupplierId?.ToString()),
                ("$status", string.IsNullOrWhiteSpace(s.Item.Status) ? "In use" : s.Item.Status), ("$purchased", IsoDay(s.Item.PurchaseDate)), ("$price", s.Item.PurchasePrice?.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("$po", s.Item.PurchaseOrder ?? string.Empty), ("$warranty", IsoDay(s.Item.WarrantyEnd)), ("$replacement", IsoDay(s.Item.ReplacementDate)), ("$loan", IsoDay(s.Item.LoanDueDate)),
                ("$quote", s.Item.QuoteReference ?? string.Empty), ("$disposed", IsoDay(s.Item.DisposalDate)), ("$method", s.Item.DisposalMethod ?? string.Empty), ("$proceeds", s.Item.DisposalProceeds?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("$building", s.Item.Building ?? string.Empty), ("$os", s.Item.OperatingSystem ?? string.Empty), ("$condition", s.Item.Condition ?? string.Empty), ("$ownership", s.Item.Ownership ?? string.Empty),
                ("$lastcheck", IsoDay(s.Item.LastCheckDate)), ("$lastcheckby", s.Item.LastCheckBy ?? string.Empty), ("$nextcheck", IsoDay(s.Item.NextCheckDate)), ("$eos", IsoDay(s.Item.EndOfSupport)),
                ("$disposedby", s.Item.DisposedBy ?? string.Empty), ("$certificate", s.Item.DisposalCertificate ?? string.Empty),
                ("$contract", s.Item.ContractId?.ToString())]);
            foreach (var assignment in item.Assignments)
                ExecuteFor(assignment, item.Id, item, connection, transaction, "INSERT INTO AssetAssignments (AssetId, UserId, UserName, StartedAt, EndedAt, DueBack, Reason, KitLoanId) VALUES ($id,$user,$name,$started,$ended,$due,$reason,$kitloan);", static s => [("$id", s.Item.Id.ToString()), ("$user", s.Record.UserId?.ToString()), ("$name", s.Record.UserName), ("$started", s.Record.StartedAt.HasValue ? Iso(s.Record.StartedAt.Value) : null), ("$ended", s.Record.EndedAt.HasValue ? Iso(s.Record.EndedAt.Value) : null), ("$due", IsoDay(s.Record.DueBack)), ("$reason", s.Record.Reason), ("$kitloan", s.Record.KitLoanId?.ToString())]);
            foreach (var comment in item.Comments)
                ExecuteFor(comment, item.Id, item, connection, transaction, "INSERT INTO AssetComments (AssetId, Text, CreatedAt, Actor, ActorId) VALUES ($id,$text,$created,$actor,$actorid);", static s => [("$id", s.Item.Id.ToString()), ("$text", s.Record.Text), ("$created", Iso(s.Record.CreatedAt)), ("$actor", s.Record.By?.Name), ("$actorid", s.Record.By?.Id?.ToString())]);
            foreach (var activity in item.History)
                ExecuteFor(activity, item.Id, item, connection, transaction, "INSERT INTO AssetActivities (AssetId, Action, Details, CreatedAt, Actor, ActorId) VALUES ($id,$action,$details,$created,$actor,$actorid);", static s => [("$id", s.Item.Id.ToString()), ("$action", s.Record.Action), ("$details", s.Record.Details), ("$created", Iso(s.Record.CreatedAt)), ("$actor", s.Record.By?.Name), ("$actorid", s.Record.By?.Id?.ToString())]);
        }
        // After Assets: LoanKitAssets references Assets(Id), and KitLoans references LoanKits(Id).
        foreach (var kit in data.LoanKits)
        {
            Execute(connection, transaction, "INSERT INTO LoanKits (Id, Name, Notes, CreatedAt, IsRetired) VALUES ($id,$name,$notes,$created,$retired);",
                ("$id", kit.Id.ToString()), ("$name", kit.Name), ("$notes", kit.Notes ?? string.Empty), ("$created", Iso(kit.CreatedAt)), ("$retired", kit.IsRetired ? 1 : 0));
            foreach (var assetId in kit.AssetIds.Distinct())
                if (assetIds.Contains(assetId))
                    Execute(connection, transaction, "INSERT INTO LoanKitAssets (KitId, AssetId) VALUES ($kit,$asset);", ("$kit", kit.Id.ToString()), ("$asset", assetId.ToString()));
        }
        foreach (var loan in data.KitLoans.Where(x => kitIds.Contains(x.KitId)))
            Execute(connection, transaction, "INSERT INTO KitLoans (Id, KitId, BorrowerUserId, BorrowerName, Reason, IssuedAt, DueBack, ReturnedAt, IssuedBy, Notes) VALUES ($id,$kit,$user,$name,$reason,$issued,$due,$returned,$by,$notes);",
                ("$id", loan.Id.ToString()), ("$kit", loan.KitId.ToString()), ("$user", loan.BorrowerUserId?.ToString()), ("$name", loan.BorrowerName),
                ("$reason", loan.Reason), ("$issued", Iso(loan.IssuedAt)), ("$due", IsoDay(loan.DueBack)),
                ("$returned", loan.ReturnedAt.HasValue ? Iso(loan.ReturnedAt.Value) : null), ("$by", loan.IssuedBy ?? string.Empty), ("$notes", loan.Notes ?? string.Empty));
        foreach (var item in data.AssetAttributeDefinitions)
        {
            Execute(connection, transaction, "INSERT INTO AssetAttributeDefinitions (Id, Name, FieldType, Choices) VALUES ($id,$name,$fieldType,$choices);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$fieldType", NormalizeAttributeType(item.FieldType)), ("$choices", NormalizeChoices(item.Choices)));
            foreach (var assetType in item.AssetTypes.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO AssetAttributeAssetTypes (AttributeId, AssetType) VALUES ($id,$type);", ("$id", item.Id.ToString()), ("$type", assetType));
        }
        foreach (var item in data.AssetAttributeValues)
            if (assetIds.Contains(item.AssetId) && assetDefinitionIds.Contains(item.AttributeDefinitionId))
                Execute(connection, transaction, "INSERT INTO AssetAttributeValues (AssetId, AttributeDefinitionId, Value) VALUES ($asset,$definition,$value);", ("$asset", item.AssetId.ToString()), ("$definition", item.AttributeDefinitionId.ToString()), ("$value", item.Value));
        foreach (var item in data.TicketAttributeDefinitions)
        {
            Execute(connection, transaction, "INSERT INTO TicketAttributeDefinitions (Id, Name, FieldType, Choices) VALUES ($id,$name,$fieldType,$choices);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$fieldType", NormalizeAttributeType(item.FieldType)), ("$choices", NormalizeChoices(item.Choices)));
            foreach (var category in item.Categories.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO TicketAttributeCategories (AttributeId, Category) VALUES ($id,$category);", ("$id", item.Id.ToString()), ("$category", category));
        }
        foreach (var item in data.Tickets)
        {
            ExecuteFor(item, 0, item, connection, transaction, "INSERT INTO Tickets (Number, Title, Description, RequesterId, TechnicianId, Priority, Status, Category, CreatedAt, ClosedAt, SlaId, DueDate, DueDateOverridden, SlaOverridden, TeamName, TicketType, Location, RequesterSeenAt, SubCategory) VALUES ($number,$title,$description,$requester,$technician,$priority,$status,$category,$created,$closed,$sla,$due,$overridden,$slaoverridden,$team,$type,$location,$seen,$subcategory);",
                static s => [("$number", s.Item.Number), ("$title", s.Item.Title), ("$description", s.Item.Description), ("$requester", s.Item.RequesterId.ToString()), ("$technician", s.Item.TechnicianId?.ToString()), ("$priority", s.Item.Priority), ("$status", s.Item.Status), ("$category", s.Item.Category), ("$created", Iso(s.Item.CreatedAt)), ("$closed", s.Item.ClosedAt.HasValue ? Iso(s.Item.ClosedAt.Value) : null), ("$sla", s.Item.SlaId?.ToString()), ("$due", s.Item.DueDate.HasValue ? Iso(s.Item.DueDate.Value) : null), ("$overridden", s.Item.DueDateOverridden ? 1 : 0), ("$slaoverridden", s.Item.SlaOverridden ? 1 : 0), ("$team", s.Item.TeamName), ("$type", TicketTypes.Normalize(s.Item.Type)), ("$location", s.Item.Location), ("$seen", s.Item.RequesterSeenAt is { } seen ? Iso(seen) : null), ("$subcategory", s.Item.SubCategory ?? "")]);
            // Most tickets have one asset or none, and those need no de-duplicating.
            foreach (var assetId in item.AssetIds.Count < 2 ? item.AssetIds : item.AssetIds.Distinct())
                if (assetIds.Contains(assetId))
                    Execute(connection, transaction, "INSERT INTO TicketAssets (TicketNumber, AssetId) VALUES ($number,$asset);", ("$number", item.Number), ("$asset", assetId.ToString()));
            foreach (var part in partsByTicket[item.Number])
                if (partIds.Contains(part.PartId))
                    Execute(connection, transaction, "INSERT INTO TicketParts (TicketNumber, PartId, Quantity) VALUES ($number,$part,$quantity);", ("$number", item.Number), ("$part", part.PartId.ToString()), ("$quantity", part.Quantity));
            foreach (var value in valuesByTicket[item.Number])
                if (ticketDefinitionIds.Contains(value.AttributeDefinitionId))
                    Execute(connection, transaction, "INSERT INTO TicketAttributeValues (TicketNumber, AttributeDefinitionId, Value) VALUES ($number,$definition,$value);", ("$number", value.TicketNumber), ("$definition", value.AttributeDefinitionId.ToString()), ("$value", value.Value));
            foreach (var comment in item.Comments)
                ExecuteFor(comment, item.Number, item, connection, transaction, "INSERT INTO TicketComments (TicketNumber, Text, CreatedAt, IsInternal, Actor, ActorId, FromRequester) VALUES ($number,$text,$created,$internal,$actor,$actorid,$fromRequester);", static s => [("$number", s.Item.Number), ("$text", s.Record.Text), ("$created", Iso(s.Record.CreatedAt)), ("$internal", s.Record.IsInternal ? 1 : 0), ("$actor", s.Record.By?.Name), ("$actorid", s.Record.By?.Id?.ToString()), ("$fromRequester", s.Record.FromRequester ? 1 : 0)]);
            foreach (var attachment in attachmentsByTicket[item.Number])
                Execute(connection, transaction, "INSERT INTO TicketAttachments (Id, TicketNumber, FileName, ContentType, Size, UploadedAt, VisibleToRequester, FromRequester) VALUES ($id,$number,$name,$type,$size,$uploaded,$visible,$fromRequester);", ("$id", attachment.Id.ToString()), ("$number", item.Number), ("$name", attachment.FileName), ("$type", attachment.ContentType), ("$size", attachment.Size), ("$uploaded", Iso(attachment.UploadedAt)), ("$visible", attachment.VisibleToRequester ? 1 : 0), ("$fromRequester", attachment.FromRequester ? 1 : 0));
            foreach (var pause in item.SlaPauses)
                ExecuteFor(pause, item.Number, item, connection, transaction, "INSERT INTO TicketSlaPauses (TicketNumber, StartedAt, EndedAt) VALUES ($number,$started,$ended);", static s => [("$number", s.Item.Number), ("$started", Iso(s.Record.StartedAt)), ("$ended", s.Record.EndedAt is { } ended ? Iso(ended) : null)]);
            foreach (var activity in item.History)
                ExecuteFor(activity, item.Number, item, connection, transaction, "INSERT INTO TicketActivities (TicketNumber, Action, Details, CreatedAt, Actor, ActorId) VALUES ($number,$action,$details,$created,$actor,$actorid);", static s => [("$number", s.Item.Number), ("$action", s.Record.Action), ("$details", s.Record.Details), ("$created", Iso(s.Record.CreatedAt)), ("$actor", s.Record.By?.Name), ("$actorid", s.Record.By?.Id?.ToString())]);
        }
        foreach (var item in data.ServiceItems)
            Execute(connection, transaction, "INSERT INTO ServiceItems (Id, Category, Name, DefaultPriority, HelperLine, ShowInPortal, PortalOrder) VALUES ($id,$category,$name,$priority,$helper,$show,$order);",
                ("$id", item.Id.ToString()), ("$category", item.Category), ("$name", item.Name), ("$priority", item.DefaultPriority ?? ""), ("$helper", item.HelperLine ?? ""), ("$show", item.ShowInPortal ? 1 : 0), ("$order", item.PortalOrder));
        // Only categories that still exist: a style left behind by a deleted category is dropped here, not kept for ever.
        // Sorted, so the rows come out in the same order whatever order the dictionary happens to hold them in.
        foreach (var (category, style) in data.CategoryStyles.Where(x => data.Categories.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, "INSERT INTO CategoryStyles (Category, Icon, Color) VALUES ($category,$icon,$color);", ("$category", category), ("$icon", style.Icon), ("$color", style.Color));
        foreach (var template in data.TicketTemplates)
        {
            Execute(connection, transaction, "INSERT INTO TicketTemplates (Id, Name, TicketType, Title, Description, Category, Priority, SlaId, HelperLine, ShowInPortal, PortalOrder) VALUES ($id,$name,$type,$title,$description,$category,$priority,$sla,$helper,$show,$order);",
                ("$id", template.Id.ToString()), ("$name", template.Name), ("$type", TicketTypes.Normalize(template.Type)), ("$title", template.Title), ("$description", template.Description), ("$category", template.Category), ("$priority", template.Priority), ("$sla", template.SlaId?.ToString()), ("$helper", template.HelperLine ?? ""), ("$show", template.ShowInPortal ? 1 : 0), ("$order", template.PortalOrder));
            foreach (var pair in template.AttributeValues.Where(x => !string.IsNullOrEmpty(x.Value) && ticketDefinitionIds.Contains(x.Key)))
                Execute(connection, transaction, "INSERT INTO TicketTemplateAttributes (TemplateId, AttributeDefinitionId, Value) VALUES ($template,$definition,$value);", ("$template", template.Id.ToString()), ("$definition", pair.Key.ToString()), ("$value", pair.Value));
        }
        SetMetadata(connection, transaction, "LastTicketNumber", Math.Max(data.LastTicketNumber, data.Tickets.Select(x => x.Number).DefaultIfEmpty(1000).Max()).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var ticketNumbers = data.Tickets.Select(x => x.Number).ToHashSet();
        foreach (var link in data.TicketLinks.Where(x => ticketNumbers.Contains(x.TicketNumber) && ticketNumbers.Contains(x.LinkedNumber)))
            Execute(connection, transaction, "INSERT INTO TicketLinks (TicketNumber, LinkedNumber, Kind) VALUES ($a,$b,$kind);", ("$a", link.TicketNumber), ("$b", link.LinkedNumber), ("$kind", link.Kind));
        var branding = data.Branding ?? new BrandingSettings();
        Execute(connection, transaction, "INSERT INTO BrandingSettings (Id, BrandName, DashboardEyebrow, DashboardTitle, DashboardDescription, PrimaryColor, AccentColor, BackgroundColor, DarkMode) VALUES (1,$name,$eyebrow,$title,$description,$primary,$accent,$background,$dark);",
            ("$name", branding.BrandName), ("$eyebrow", branding.DashboardEyebrow), ("$title", branding.DashboardTitle), ("$description", branding.DashboardDescription), ("$primary", branding.PrimaryColor), ("$accent", branding.AccentColor), ("$background", branding.BackgroundColor), ("$dark", (int)branding.DefaultAppearance));
    }

    // The full rewrite empties every table first. Children before parents: projects point at Users and Technicians.
    private static void ClearTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM OnboardingPackDocuments; DELETE FROM OnboardingTemplateDocuments; DELETE FROM OnboardingDocuments; DELETE FROM OnboardingTasks; DELETE FROM Onboardings; DELETE FROM OnboardingTemplateTasks; DELETE FROM OnboardingTemplates; DELETE FROM ProjectTickets; DELETE FROM SpendingBands; DELETE FROM ProjectPaymentLines; DELETE FROM ProjectQuoteDocuments; DELETE FROM ProjectQuoteVersions; DELETE FROM ProjectQuoteStatusChanges; DELETE FROM ProjectItemSuppliers; DELETE FROM ProjectSubItems; DELETE FROM ProjectItems; DELETE FROM ProjectRequirements; DELETE FROM ProjectNotes; DELETE FROM ProjectActivities; DELETE FROM Projects; DELETE FROM PurchasingRequirements; DELETE FROM TicketTemplateAttributes; DELETE FROM TicketTemplates; DELETE FROM ServiceItems; DELETE FROM CategoryStyles; DELETE FROM TicketLinks; DELETE FROM TicketAttachments; DELETE FROM TicketSlaPauses; DELETE FROM TicketActivities; DELETE FROM TicketComments; DELETE FROM TicketAttributeValues; DELETE FROM TicketAssets; DELETE FROM TicketParts; DELETE FROM PartSuppliers; DELETE FROM PartAssetTypes; DELETE FROM PartActivities; DELETE FROM Parts; DELETE FROM Tickets; DELETE FROM TicketAttributeCategories; DELETE FROM TicketAttributeDefinitions; DELETE FROM AssetAssignments; DELETE FROM AssetComments; DELETE FROM AssetActivities; DELETE FROM AssetAttributeValues; DELETE FROM Assets; DELETE FROM Suppliers; DELETE FROM Technicians; DELETE FROM Roles; DELETE FROM Users; DELETE FROM AssetAttributeAssetTypes; DELETE FROM AssetAttributeDefinitions; DELETE FROM SlaPriorities; DELETE FROM SlaCategories; DELETE FROM Slas; DELETE FROM TechnicianTeams; DELETE FROM Departments; DELETE FROM Locations; DELETE FROM AssetTypes; DELETE FROM AssetMakes; DELETE FROM AssetModelMakes; DELETE FROM AssetStatuses; DELETE FROM AssetTypeLifespans; DELETE FROM PartCategories; DELETE FROM PartLocations; DELETE FROM KitLoans; DELETE FROM LoanKitAssets; DELETE FROM LoanKits; DELETE FROM LoanReasons; DELETE FROM Buildings; DELETE FROM AssetConditions; DELETE FROM Contracts; DELETE FROM ContractTypes; DELETE FROM SpendCategories; DELETE FROM ContractDurations; DELETE FROM ContractStatuses; DELETE FROM SchoolPeriods;DELETE FROM AssetModels; DELETE FROM Categories; DELETE FROM Statuses; DELETE FROM StatusDescriptions; DELETE FROM SlaPauseStatuses; DELETE FROM Priorities; DELETE FROM RequireCloseMessagePriorities; DELETE FROM RequireCloseMessageCategories; DELETE FROM DemoRecords; DELETE FROM RolePermissions; DELETE FROM BrandingSettings; DELETE FROM SizeHistory; DELETE FROM SpiceworksImports; DELETE FROM SpiceworksLinks; DELETE FROM SpiceworksTicketStates; DELETE FROM SpiceworksUndo;";
        command.ExecuteNonQuery();
    }

    private static void InsertStrings(SqliteConnection connection, SqliteTransaction transaction, string table, IEnumerable<string> values)
    {
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, $"INSERT INTO {table} (Name) VALUES ($value);", ("$value", value));
    }

    // Each statement is prepared once per transaction and reused for every row it writes, rather than built and
    // prepared again for each of the tens of thousands of rows a save can hold. StatementScope releases them when the
    // transaction is done, before the connection closes - a statement left open would keep the database file open.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SqliteTransaction, Dictionary<string, SqliteCommand>> Statements = new();

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] values)
    {
        // While a save is working out what changed, WriteData's rows are recorded rather than run (SaveChanges).
        if (TryGetCapture(transaction, out var capture))
        {
            capture.Add(sql, values);
            return;
        }
        var cache = Statements.GetValue(transaction, _ => new Dictionary<string, SqliteCommand>(StringComparer.Ordinal));
        if (!cache.TryGetValue(sql, out var command))
        {
            command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var value in values) command.Parameters.Add(new SqliteParameter(value.Name, value.Value ?? DBNull.Value));
            cache[sql] = command;
        }
        else
        {
            foreach (var value in values) command.Parameters[value.Name].Value = value.Value ?? DBNull.Value;
        }
        command.ExecuteNonQuery();
    }

    private sealed class StatementScope(SqliteTransaction transaction) : IDisposable
    {
        public void Dispose()
        {
            if (!Statements.TryGetValue(transaction, out var cache)) return;
            foreach (var command in cache.Values) command.Dispose();
            Statements.Remove(transaction);
        }
    }
}
