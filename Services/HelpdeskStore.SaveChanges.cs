using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Saving only what changed. WriteData describes the whole dataset as INSERT statements, one per row. A save used to run
// them all after emptying every table, so its cost grew with everything held. Now a save captures those rows instead,
// compares them with the rows the previous save wrote (kept in _written), and sends the database only the differences:
// new rows are inserted, changed rows are updated in place, and rows that are gone are deleted.
//
// Two things make that harder than it sounds:
// - Foreign keys cascade. Deleting and re-inserting a ticket's row to change its title would take its comments and
//   history with it, so a changed row is always an UPDATE, never a delete and insert.
// - Some lists are ordered only by the order their rows were inserted (comments, history, assignments: loaded ORDER BY
//   Id). Such rows are compared per owner - one ticket's comments, one asset's history - and when the owner's list is
//   anything other than the old one with rows added at the end, that owner's rows are rewritten in order.
//
// Whenever the comparison can't be sure (a table written by two different statements, a repeated key, a reorder of rows
// that other tables point at), the save falls back to the full rewrite, which is always right. So does the first save
// after startup or after a failed save, when there is no record of what the database holds.
public sealed partial class HelpdeskStore
{
    // What the previous successful save wrote, table by table. Null means unknown: the next save rewrites everything.
    private RowCapture? _written;
    private Dictionary<string, TableShape>? _shapes;
    private readonly Queue<SaveTiming> _saveTimings = new();
    private const int SaveTimingsKept = 500;

    // How long each recent save took, for the database health check.
    public sealed record SaveTiming(DateTime At, double Milliseconds, bool FullRewrite, int RowsWritten);

    public IReadOnlyList<SaveTiming> RecentSaveTimings()
    {
        lock (_sync) return _saveTimings.ToList();
    }

    private void RecordSaveTiming(long started, bool full, int rows)
    {
        _saveTimings.Enqueue(new SaveTiming(DateTime.UtcNow, Stopwatch.GetElapsedTime(started).TotalMilliseconds, full, rows));
        while (_saveTimings.Count > SaveTimingsKept) _saveTimings.Dequeue();
    }

    // Which column a child table's rows belong to - the owner whose list they make up. Rows are compared per owner, so
    // adding one comment touches only that ticket's comments. It must be the owner or something coarser (a quote's
    // status changes are grouped by item, which holds several quotes): grouping any finer could reorder an owner's list.
    // A table not named here is compared as one list, which is always correct, just slower for a big table.
    private static readonly Dictionary<string, string> OwnerColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AssetActivities"] = "AssetId", ["AssetAssignments"] = "AssetId", ["AssetComments"] = "AssetId", ["AssetAttributeValues"] = "AssetId",
        ["AssetAttributeAssetTypes"] = "AttributeId", ["TicketAttributeCategories"] = "AttributeId",
        ["KitLoans"] = "KitId", ["LoanKitAssets"] = "KitId",
        ["OnboardingTasks"] = "TicketNumber", ["OnboardingPackDocuments"] = "TicketNumber",
        ["OnboardingTemplateTasks"] = "TemplateId", ["OnboardingTemplateDocuments"] = "TemplateId",
        ["PartActivities"] = "PartId", ["PartAssetTypes"] = "PartId", ["PartSuppliers"] = "PartId",
        ["ProjectActivities"] = "ProjectNumber", ["ProjectItems"] = "ProjectNumber", ["ProjectNotes"] = "ProjectNumber",
        ["ProjectRequirements"] = "ProjectNumber", ["ProjectTickets"] = "ProjectNumber",
        ["ProjectItemSuppliers"] = "ItemId", ["ProjectSubItems"] = "ItemId", ["ProjectQuoteStatusChanges"] = "ItemId",
        ["ProjectQuoteVersions"] = "ItemId", ["ProjectQuoteDocuments"] = "ItemId", ["ProjectPaymentLines"] = "ItemId",
        ["RolePermissions"] = "RoleName", ["SlaCategories"] = "SlaId", ["SlaPriorities"] = "SlaId",
        ["TicketActivities"] = "TicketNumber", ["TicketAssets"] = "TicketNumber", ["TicketAttachments"] = "TicketNumber",
        ["TicketAttributeValues"] = "TicketNumber", ["TicketComments"] = "TicketNumber", ["TicketLinks"] = "TicketNumber",
        ["TicketParts"] = "TicketNumber", ["TicketSlaPauses"] = "TicketNumber",
        ["TicketTemplateAttributes"] = "TemplateId"
    };

    // ---- Capturing ----

    // The capture in progress on this thread, and the transaction it belongs to. Asked for once per row, so a plain
    // field rather than a lookup.
    [ThreadStatic] private static RowCapture? _activeCapture;
    [ThreadStatic] private static SqliteTransaction? _activeTransaction;

    private static bool IsCapturing(SqliteTransaction transaction) => ReferenceEquals(transaction, _activeTransaction);

    private static bool TryGetCapture(SqliteTransaction transaction, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RowCapture? capture)
    {
        capture = ReferenceEquals(transaction, _activeTransaction) ? _activeCapture : null;
        return capture is not null;
    }

    // Runs WriteData with Execute recording each row instead of running it.
    private static RowCapture CaptureRows(SqliteConnection connection, SqliteTransaction transaction, StoreData data, RowCapture? previous = null)
    {
        var capture = new RowCapture(previous);
        _activeCapture = capture;
        _activeTransaction = transaction;
        try { WriteData(connection, transaction, data); }
        finally { _activeCapture = null; _activeTransaction = null; }
        return capture;
    }

    // The previous capture, when there is one, says how big each table's list will be, so a list of 160,000 history
    // lines is made the right size once instead of grown by doubling.
    private sealed partial class RowCapture(RowCapture? previous)
    {
        public readonly List<TableRows> Tables = [];
        private readonly Dictionary<string, TableRows> _byName = new(StringComparer.OrdinalIgnoreCase);
        // False when a statement couldn't be understood, or one table was written by two different statements. The
        // rows are still all here and can be replayed, but they can't be compared.
        public bool Comparable { get; private set; } = true;
        public int RowCount => Tables.Sum(x => x.Rows.Count);

        [GeneratedRegex(@"^\s*INSERT INTO (\w+) \(([^)]*)\) VALUES \(([^)]*)\);?\s*$", RegexOptions.Singleline)]
        private static partial Regex InsertPattern();

        // A save captures hundreds of thousands of rows, so this is kept cheap: rows come in runs from the same
        // statement, and a statement passes its parameters in the same order every time.
        private string? _lastSql;
        private TableRows? _lastTable;

        public object?[]? LastRow { get; private set; }

        // Statements are string literals, so the same instance comes every time: found by reference first, which saves
        // hashing a few hundred characters for every row. A statement built on the fly is found by its text.
        private readonly Dictionary<string, TableRows> _byReference = new(ReferenceEqualityComparer.Instance);

        private TableRows TableFor(string sql)
        {
            if (ReferenceEquals(sql, _lastSql)) return _lastTable!;
            if (_byReference.TryGetValue(sql, out var known))
            {
                _lastSql = sql;
                return _lastTable = known;
            }
            if (!_byName.TryGetValue(sql, out var table))
            {
                table = Parse(sql);
                if (previous?.Find(table.Name) is { } before) table.Rows.Capacity = before.Rows.Count + 16;
                if (!table.Understood || Tables.Any(x => string.Equals(x.Name, table.Name, StringComparison.OrdinalIgnoreCase))) Comparable = false;
                _byName[sql] = table;
                Tables.Add(table);
            }
            if (ReferenceEquals(string.IsInterned(sql), sql)) _byReference[sql] = table;
            _lastSql = sql;
            _lastTable = table;
            return table;
        }

        // A row made by an earlier capture from the same statement.
        public void AddRow(string sql, object?[] row) => TableFor(sql).Rows.Add(LastRow = row);

        public void Add(string sql, (string Name, object? Value)[] values)
        {
            var table = TableFor(sql);
            var row = new object?[table.Columns.Length];
            var map = table.ValueIndexes ??= table.Tokens.Select(t => t.Parameter is null ? -1 : Array.FindIndex(values, v => v.Name == t.Parameter)).ToArray();
            for (var i = 0; i < row.Length; i++)
            {
                var token = table.Tokens[i];
                if (token.Parameter is null) { row[i] = token.Literal; continue; }
                var at = map[i];
                if (at >= 0 && at < values.Length && (ReferenceEquals(values[at].Name, token.Parameter) || values[at].Name == token.Parameter)) { row[i] = values[at].Value; continue; }
                // Not where it was last time: look for it.
                foreach (var value in values)
                    if (value.Name == token.Parameter) { row[i] = value.Value; break; }
            }
            table.Rows.Add(LastRow = row);
        }

        private static TableRows Parse(string sql)
        {
            var match = InsertPattern().Match(sql);
            if (!match.Success) return new TableRows("?", sql, [], [], Understood: false);
            var columns = match.Groups[2].Value.Split(',').Select(x => x.Trim()).ToArray();
            var raw = match.Groups[3].Value.Split(',').Select(x => x.Trim()).ToArray();
            if (raw.Length != columns.Length) return new TableRows(match.Groups[1].Value, sql, columns, [], Understood: false);
            var understood = true;
            var tokens = raw.Select(x =>
            {
                if (x.StartsWith('$')) return new Token(x, null);
                if (long.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)) return new Token(null, number);
                if (x.Length >= 2 && x[0] == '\'' && x[^1] == '\'') return new Token(null, x[1..^1].Replace("''", "'"));
                understood = false;
                return new Token(null, null);
            }).ToArray();
            return new TableRows(match.Groups[1].Value, sql, columns, tokens, understood);
        }

        // Every row, as the statements that made it: the full rewrite.
        public void Replay(SqliteConnection connection, SqliteTransaction transaction)
        {
            foreach (var table in Tables)
                foreach (var row in table.Rows)
                    Execute(connection, transaction, table.Sql, table.Parameters(row));
        }

        public TableRows? Find(string name) => Tables.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct Token(string? Parameter, object? Literal);

    private sealed record TableRows(string Name, string Sql, string[] Columns, Token[] Tokens, bool Understood)
    {
        public List<object?[]> Rows { get; } = [];
        // Where each column's parameter sat in the statement's values last time.
        public int[]? ValueIndexes { get; set; }

        public (string Name, object? Value)[] Parameters(object?[] row)
        {
            var parameters = new List<(string, object?)>(row.Length);
            for (var i = 0; i < Tokens.Length; i++)
                if (Tokens[i].Parameter is { } name && !parameters.Any(x => x.Item1 == name)) parameters.Add((name, row[i]));
            return parameters.ToArray();
        }
    }

    // ---- Rows made from records ----

    // The records behind the big tables (tickets, comments, history, assets, people) never change: an edit makes a new
    // record. So in a capture, the row made from the same record last time is used again rather than formatted afresh,
    // and the comparison sees the very same row and moves straight past it. The owner is anything else the row is made
    // from - the ticket number a comment is written under. Only for rows made from nothing but the record and its owner.
    private sealed record CachedRow(object? Owner, string Sql, object?[] Row);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, CachedRow> RowCache = new();

    // A save passes through here for every comment and history line held, so reusing a row allocates nothing: the values
    // are made by a static function of (owner record, record) only when the row isn't reused.
    private static void ExecuteFor<TItem, TRecord, TOwner>(TRecord record, TOwner owner, TItem item, SqliteConnection connection, SqliteTransaction transaction,
        string sql, Func<(TItem Item, TRecord Record), (string Name, object? Value)[]> values)
        where TRecord : class where TOwner : notnull
    {
        if (!TryGetCapture(transaction, out var capture))
        {
            Execute(connection, transaction, sql, values((item, record)));
            return;
        }
        if (RowCache.TryGetValue(record, out var cached) && cached.Owner is TOwner was && EqualityComparer<TOwner>.Default.Equals(was, owner)
            && (ReferenceEquals(cached.Sql, sql) || cached.Sql == sql))
        {
            capture.AddRow(sql, cached.Row);
            return;
        }
        capture.Add(sql, values((item, record)));
        RowCache.AddOrUpdate(record, new CachedRow(owner, sql, capture.LastRow!));
    }

    // ---- The schema, as the comparison needs it ----

    // A table's primary key, and whether any other table's foreign key points at it (then its rows can't be deleted and
    // re-inserted, because the delete would cascade).
    private sealed record TableShape(string[] PrimaryKey, bool Referenced);

    private static Dictionary<string, TableShape> ReadShapes(SqliteConnection connection, SqliteTransaction transaction)
    {
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = command.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT name FROM pragma_table_info($t) WHERE pk > 0 ORDER BY pk;";
                command.Parameters.AddWithValue("$t", table);
                using var reader = command.ExecuteReader();
                var pk = new List<string>();
                while (reader.Read()) pk.Add(reader.GetString(0));
                keys[table] = pk.ToArray();
            }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT \"table\" FROM pragma_foreign_key_list($t);";
                command.Parameters.AddWithValue("$t", table);
                using var reader = command.ExecuteReader();
                while (reader.Read()) referenced.Add(reader.GetString(0));
            }
        }
        return tables.ToDictionary(x => x, x => new TableShape(keys[x], referenced.Contains(x)), StringComparer.OrdinalIgnoreCase);
    }

    // Deleting a ticket cascades into eight tables, and a save looks up one owner's rows; without an index on the owner
    // column either means reading the whole child table. Created once, at startup.
    private static void EnsureOwnerIndexes(SqliteConnection connection)
    {
        foreach (var (table, column) in OwnerColumns)
        {
            if (!TableExists(connection, table)) continue;
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE INDEX IF NOT EXISTS IX_{table}_{column} ON {table} ({column});";
            command.ExecuteNonQuery();
        }
    }

    // ---- Checking ----

    // For the tests: whether the database holds exactly what a full rewrite of the data in memory would write - the same
    // rows, with each owner's rows in the same order. Null when it does, otherwise the first difference found.
    internal string? CompareDatabaseWithMemory()
    {
        lock (_sync)
        {
            using var connection = new SqliteConnection($"Data Source={_path}");
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var wanted = CaptureRows(connection, transaction, _data);
            try
            {
                var tables = new List<string>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('AuditLog', 'Metadata', 'Sessions');";
                    using var reader = command.ExecuteReader();
                    while (reader.Read()) tables.Add(reader.GetString(0));
                }
                foreach (var table in tables)
                {
                    var capture = wanted.Find(table);
                    var columns = capture?.Columns ?? ["COUNT(*)"];
                    var stored = new List<object?[]>();
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = capture is null ? $"SELECT COUNT(*) FROM {table};" : $"SELECT {string.Join(", ", columns)} FROM {table} ORDER BY rowid;";
                        using var reader = command.ExecuteReader();
                        while (reader.Read())
                        {
                            var row = new object?[reader.FieldCount];
                            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            stored.Add(row);
                        }
                    }
                    if (capture is null)
                    {
                        if (Convert.ToInt64(stored[0][0], System.Globalization.CultureInfo.InvariantCulture) != 0) return $"{table} should be empty but holds {stored[0][0]} rows.";
                        continue;
                    }
                    var ownerIndex = OwnerColumns.TryGetValue(table, out var owner) ? Array.FindIndex(columns, c => string.Equals(c, owner, StringComparison.OrdinalIgnoreCase)) : -1;
                    string Owner(object?[] row) => ownerIndex < 0 ? "" : Text(row[ownerIndex]);
                    string Line(object?[] row) => string.Join(" | ", row.Select(Text));
                    var inDatabase = stored.GroupBy(Owner).ToDictionary(g => g.Key, g => g.Select(Line).ToList());
                    var inMemory = capture.Rows.GroupBy(Owner).ToDictionary(g => g.Key, g => g.Select(Line).ToList());
                    foreach (var key in inMemory.Keys.Union(inDatabase.Keys))
                    {
                        var a = inMemory.GetValueOrDefault(key) ?? [];
                        var b = inDatabase.GetValueOrDefault(key) ?? [];
                        if (!a.SequenceEqual(b))
                            return $"{table}{(key.Length > 0 ? $" ({owner} {key})" : "")}: memory has [{string.Join(" ; ", a)}] but the database has [{string.Join(" ; ", b)}]";
                    }
                }
                return null;
            }
            finally
            {
                transaction.Rollback();
            }
        }

        // Values as SQLite gives them back: an int written into a text column reads back as text, and so on.
        static string Text(object? value) => value switch
        {
            null => "∅",
            bool b => b ? "1" : "0",
            byte[] bytes => Convert.ToBase64String(bytes),
            Guid g => Convert.ToBase64String(g.ToByteArray()),
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    // ---- Comparing and writing the difference ----

    // The statements that turn what was written into what is wanted, or null when the difference can't be worked out
    // safely and everything should be rewritten instead. Nothing is sent to the database while planning.
    private static List<(string Sql, (string Name, object? Value)[] Values)>? PlanChanges(RowCapture before, RowCapture after, Dictionary<string, TableShape> shapes)
    {
        if (!before.Comparable || !after.Comparable) return null;
        var plan = new List<(string, (string, object?)[])>();
        // A table that has disappeared from the capture entirely (every row gone) still needs its rows deleted.
        foreach (var gone in before.Tables.Where(x => after.Find(x.Name) is null))
            plan.Add(($"DELETE FROM {gone.Name};", []));
        foreach (var table in after.Tables)
        {
            var old = before.Find(table.Name);
            if (old is null)
            {
                // Empty last time (so never captured) - every row is new.
                foreach (var row in table.Rows) plan.Add((table.Sql, table.Parameters(row)));
                continue;
            }
            if (old.Sql != table.Sql || !shapes.TryGetValue(table.Name, out var shape)) return null;
            if (!PlanTable(old, table, shape, plan)) return null;
        }
        return plan;
    }

    private static bool PlanTable(TableRows old, TableRows now, TableShape shape, List<(string, (string, object?)[])> plan)
    {
        var columns = now.Columns;
        var keyIndexes = shape.PrimaryKey.Select(k => Array.FindIndex(columns, c => string.Equals(c, k, StringComparison.OrdinalIgnoreCase))).ToArray();
        var keyed = keyIndexes.Length > 0 && keyIndexes.All(x => x >= 0);
        // Rows with no key of their own (an autoincrement Id the statement doesn't supply) can't be updated in place,
        // only deleted and re-inserted - fine, as long as nothing points at them.
        if (!keyed && shape.Referenced) return false;
        var ownerIndex = OwnerColumns.TryGetValue(now.Name, out var owner) ? Array.FindIndex(columns, c => string.Equals(c, owner, StringComparison.OrdinalIgnoreCase)) : -1;
        var ownerColumns = ownerIndex >= 0 ? new[] { ownerIndex } : [];

        // Almost all of a table is as it was: a change touches a few rows somewhere in it. Rows that match at the start
        // and at the end are set aside, and only what lies between them is looked at.
        var start = 0;
        int oldEnd = old.Rows.Count, newEnd = now.Rows.Count;
        while (start < oldEnd && start < newEnd && RowKey.SameValues(old.Rows[start], now.Rows[start])) start++;
        while (oldEnd > start && newEnd > start && RowKey.SameValues(old.Rows[oldEnd - 1], now.Rows[newEnd - 1])) { oldEnd--; newEnd--; }
        if (start == oldEnd && start == newEnd) return true;
        List<object?[]> oldSubset, newSubset;
        var rowsFollow = false;
        if (ownerColumns.Length > 0)
        {
            // Whole lists of the owners the change touched. Every other owner's rows lie in the matching start and end,
            // so they are unchanged and in the same order.
            var touched = old.Rows.Skip(start).Take(oldEnd - start).Concat(now.Rows.Skip(start).Take(newEnd - start)).Select(r => OwnerOf(r, ownerColumns)).ToHashSet();
            oldSubset = old.Rows.Where(r => touched.Contains(OwnerOf(r, ownerColumns))).ToList();
            newSubset = now.Rows.Where(r => touched.Contains(OwnerOf(r, ownerColumns))).ToList();
        }
        else if (keyed)
        {
            // One list for the whole table: just the changed stretch, remembering whether unchanged rows come after it -
            // then anything new in it would sit before them, out of order.
            oldSubset = old.Rows.GetRange(start, oldEnd - start);
            newSubset = now.Rows.GetRange(start, newEnd - start);
            rowsFollow = oldEnd < old.Rows.Count;
        }
        else
        {
            // Small tables with no key and no owner are compared whole.
            oldSubset = old.Rows;
            newSubset = now.Rows;
        }

        var oldGroups = Group(oldSubset, ownerColumns);
        var newGroups = Group(newSubset, ownerColumns);

        if (!keyed)
        {
            foreach (var (ownerKey, oldRows) in oldGroups)
                if (!newGroups.ContainsKey(ownerKey)) plan.Add(DeleteGroup(now.Name, columns, ownerColumns, ownerKey));
            foreach (var (ownerKey, newRows) in newGroups)
            {
                var oldRows = oldGroups.GetValueOrDefault(ownerKey) ?? [];
                if (IsPrefix(oldRows, newRows))
                {
                    // The usual case: a comment or history line added at the end.
                    for (var i = oldRows.Count; i < newRows.Count; i++) plan.Add((now.Sql, now.Parameters(newRows[i])));
                    continue;
                }
                if (oldRows.Count > 0) plan.Add(DeleteGroup(now.Name, columns, ownerColumns, ownerKey));
                foreach (var row in newRows) plan.Add((now.Sql, now.Parameters(row)));
            }
            return true;
        }

        var oldByKey = new Dictionary<RowKey, object?[]>();
        foreach (var row in oldSubset) if (!oldByKey.TryAdd(RowKey.Of(row, keyIndexes), row)) return false;
        var newByKey = new Dictionary<RowKey, object?[]>();
        foreach (var row in newSubset) if (!newByKey.TryAdd(RowKey.Of(row, keyIndexes), row)) return false;

        // Rows are stored in insertion order, and for some tables that order is the list's order. An update keeps a
        // row's place and an insert goes to the end, so each owner's rows must be the old ones, in the old order, with
        // any new ones after them. Anything else - a reorder, a row moved to another owner - rewrites that owner's rows.
        var rewrite = new HashSet<RowKey>();
        foreach (var (ownerKey, newRows) in newGroups)
        {
            var oldKeys = (oldGroups.GetValueOrDefault(ownerKey) ?? []).Select(r => RowKey.Of(r, keyIndexes)).ToList();
            var oldSet = oldKeys.ToHashSet();
            var newSet = newRows.Select(r => RowKey.Of(r, keyIndexes)).ToHashSet();
            var survivors = oldKeys.Where(newSet.Contains).ToList();
            var next = 0;
            var addedYet = false;
            var inOrder = true;
            foreach (var row in newRows)
            {
                var key = RowKey.Of(row, keyIndexes);
                if (oldSet.Contains(key))
                {
                    // A row that was already here: it must come next in the old order, and before anything new.
                    if (addedYet || !survivors[next].Equals(key)) { inOrder = false; break; }
                    next++;
                }
                else
                {
                    // New to this owner. A row that existed under another owner keeps its old place when updated, so
                    // it can't simply be moved.
                    if (oldByKey.ContainsKey(key)) { inOrder = false; break; }
                    addedYet = true;
                }
            }
            // Unchanged rows after the changed stretch went in before anything new could, so it would land ahead of them.
            if (inOrder && addedYet && rowsFollow) inOrder = false;
            if (inOrder) continue;
            if (shape.Referenced) return false;
            rewrite.Add(ownerKey);
        }

        foreach (var (key, row) in oldByKey)
        {
            if (rewrite.Contains(OwnerOf(row, ownerColumns))) continue;
            if (!newByKey.ContainsKey(key)) plan.Add(DeleteRow(now.Name, columns, keyIndexes, row));
        }
        foreach (var ownerKey in rewrite)
        {
            plan.Add(DeleteGroup(now.Name, columns, ownerColumns, ownerKey));
            // With no owner the group is the whole table, of which only the changed stretch was looked at: write it all.
            foreach (var row in ownerColumns.Length == 0 ? now.Rows : newGroups[ownerKey])
            {
                // A row moved here from an owner that isn't being rewritten is still in the database under its old owner.
                if (oldByKey.TryGetValue(RowKey.Of(row, keyIndexes), out var was) && !rewrite.Contains(OwnerOf(was, ownerColumns)))
                    plan.Add(DeleteRow(now.Name, columns, keyIndexes, was));
                plan.Add((now.Sql, now.Parameters(row)));
            }
        }
        foreach (var row in newSubset)
        {
            if (rewrite.Contains(OwnerOf(row, ownerColumns))) continue;
            if (!oldByKey.TryGetValue(RowKey.Of(row, keyIndexes), out var before)) plan.Add((now.Sql, now.Parameters(row)));
            else if (!RowKey.SameValues(before, row)) plan.Add(UpdateRow(now.Name, columns, keyIndexes, row));
        }
        return true;
    }

    private static Dictionary<RowKey, List<object?[]>> Group(List<object?[]> rows, int[] ownerColumns)
    {
        var groups = new Dictionary<RowKey, List<object?[]>>();
        foreach (var row in rows)
        {
            var key = OwnerOf(row, ownerColumns);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(row);
        }
        return groups;
    }

    private static RowKey OwnerOf(object?[] row, int[] ownerColumns) => RowKey.Of(row, ownerColumns);

    private static bool IsPrefix(List<object?[]> shorter, List<object?[]> longer)
    {
        if (shorter.Count > longer.Count) return false;
        for (var i = 0; i < shorter.Count; i++)
            if (!RowKey.SameValues(shorter[i], longer[i])) return false;
        return true;
    }

    private static (string, (string, object?)[]) DeleteGroup(string table, string[] columns, int[] ownerColumns, RowKey ownerKey) =>
        ownerColumns.Length == 0
            ? ($"DELETE FROM {table};", [])
            : ($"DELETE FROM {table} WHERE {string.Join(" AND ", ownerColumns.Select((c, i) => $"{columns[c]} IS $o{i}"))};",
                ownerColumns.Select((_, i) => ($"$o{i}", ownerKey.Values[i])).ToArray());

    private static (string, (string, object?)[]) DeleteRow(string table, string[] columns, int[] keyIndexes, object?[] row) =>
        ($"DELETE FROM {table} WHERE {string.Join(" AND ", keyIndexes.Select((c, i) => $"{columns[c]} = $k{i}"))};",
            keyIndexes.Select((c, i) => ($"$k{i}", row[c])).ToArray());

    private static (string, (string, object?)[]) UpdateRow(string table, string[] columns, int[] keyIndexes, object?[] row)
    {
        var others = Enumerable.Range(0, columns.Length).Where(i => !keyIndexes.Contains(i)).ToArray();
        return ($"UPDATE {table} SET {string.Join(", ", others.Select(i => $"{columns[i]} = $v{i}"))} WHERE {string.Join(" AND ", keyIndexes.Select((c, i) => $"{columns[c]} = $k{i}"))};",
            others.Select(i => ($"$v{i}", row[i])).Concat(keyIndexes.Select((c, i) => ($"$k{i}", row[c]))).ToArray());
    }

    // A row's key (or owner) values, compared by value: strings by content, byte arrays by their bytes.
    private readonly struct RowKey : IEquatable<RowKey>
    {
        public readonly object?[] Values;
        private readonly int _hash;

        private RowKey(object?[] values)
        {
            Values = values;
            var hash = new HashCode();
            foreach (var value in values) hash.Add(value is byte[] bytes ? bytes.Length : value?.GetHashCode() ?? 0);
            _hash = hash.ToHashCode();
        }

        public static RowKey Of(object?[] row, int[] indexes)
        {
            var values = new object?[indexes.Length];
            for (var i = 0; i < indexes.Length; i++) values[i] = row[indexes[i]];
            return new RowKey(values);
        }

        public static bool SameValues(object?[] a, object?[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++) if (!Same(a[i], b[i])) return false;
            return true;
        }

        private static bool Same(object? a, object? b) =>
            a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);

        public bool Equals(RowKey other) => SameValues(Values ?? [], other.Values ?? []);
        public override bool Equals(object? obj) => obj is RowKey other && Equals(other);
        public override int GetHashCode() => _hash;
    }
}
