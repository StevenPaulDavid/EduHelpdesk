using System.Collections.Concurrent;
using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The database exactly as stored, for looking into a problem (Settings → Database). Everything here reads through a
// read-only connection, so nothing on these pages can change data even by mistake. Table and column names come only
// from the database's own schema - anything typed in is checked against it - and every value is a parameter.
//
// Passwords, two-step secrets, recovery codes and session tokens are shown to Administrators only; anyone else with the
// permission sees that a value is there, not what it is, and can't search or filter on it either (a search is a way
// of guessing). Each look is written to the audit log.
public sealed class RawDatabase(HelpdeskStore store)
{
    public const int PageSize = 50;
    // A record's rows from one table, at most - the audit log can hold thousands about one ticket.
    public const int RecordRowLimit = 200;

    private string ConnectionString => $"Data Source={Path.Combine(store.DataFolder, "helpdesk.db")};Mode=ReadOnly";

    // ---- What each table is ----

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AssetActivities"] = "Each asset's History tab: field changes, loans, returns, ticket links.",
        ["AssetAssignments"] = "Who has held each asset and when. A row with DueBack is a loan.",
        ["AssetAttributeAssetTypes"] = "Which asset types a custom asset attribute applies to. None means all.",
        ["AssetAttributeDefinitions"] = "Custom asset attributes (Settings → Custom asset attributes).",
        ["AssetAttributeValues"] = "Each asset's answers to the custom asset attributes.",
        ["AssetComments"] = "Comments on assets.",
        ["AssetMakes"] = "The list of asset makes.",
        ["AssetModelMakes"] = "Which make each asset model belongs to.",
        ["AssetModels"] = "The list of asset models.",
        ["AssetStatuses"] = "The list of asset statuses.",
        ["AssetTypeLifespans"] = "How many years each asset type is expected to last.",
        ["AssetTypes"] = "The list of asset types.",
        ["Assets"] = "The asset register: one row per device.",
        ["AuditLog"] = "The audit log: who changed what, and when. Ticket and asset history is kept with the ticket or asset instead.",
        ["BrandingSettings"] = "School name, colours and overview wording. Always one row.",
        ["Categories"] = "The list of ticket categories.",
        ["DemoRecords"] = "Which records are the demo data, so Go live can remove exactly those.",
        ["Departments"] = "The list of departments.",
        ["KitLoans"] = "Each time a loan kit went out, and when it came back.",
        ["LoanKitAssets"] = "Which assets are in each loan kit.",
        ["LoanKits"] = "Loan kits: named bundles of devices lent out together.",
        ["LoanReasons"] = "The list of loan reasons.",
        ["Locations"] = "The list of locations.",
        ["Metadata"] = "Single settings and counters, one per row: last ticket number, backup settings, retention rules and so on.",
        ["OnboardingDocuments"] = "Documents that can go at the back of a welcome pack (the PDFs are files in the data folder).",
        ["OnboardingPackDocuments"] = "Which documents one new starter's welcome pack includes.",
        ["OnboardingTasks"] = "Each new starter's checklist: one row per task, with when it was ticked and by whom.",
        ["OnboardingTemplateDocuments"] = "Which documents each onboarding checklist template includes in the welcome pack.",
        ["OnboardingTemplateTasks"] = "The tasks in each onboarding checklist template.",
        ["OnboardingTemplates"] = "Onboarding checklist templates, one per kind of new starter.",
        ["Onboardings"] = "One row per new starter's onboarding, keyed by its ticket number.",
        ["PartActivities"] = "Each part's stock history: adjustments and why.",
        ["PartAssetTypes"] = "Which asset types each part is for.",
        ["PartCategories"] = "The list of part categories.",
        ["PartLocations"] = "The list of part locations.",
        ["PartSuppliers"] = "Which suppliers each part comes from.",
        ["Parts"] = "The parts inventory: one row per part, with the stock held.",
        ["Priorities"] = "The list of ticket priorities.",
        ["ProjectActivities"] = "Each project's History tab.",
        ["ProjectItemSuppliers"] = "The suppliers asked to quote for each project item.",
        ["ProjectItems"] = "The main things each project is buying.",
        ["ProjectNotes"] = "Notes on projects - shared with the requester, or internal.",
        ["ProjectPaymentLines"] = "The prices in each quote: amount, how often, for how long, VAT.",
        ["ProjectQuoteDocuments"] = "The files of each quote (the files themselves are in the data folder).",
        ["ProjectQuoteStatusChanges"] = "Each quote's status over time: requested, received, and so on.",
        ["ProjectQuoteVersions"] = "Earlier versions of quotes, kept when an updated quote replaced them.",
        ["ProjectRequirements"] = "The purchasing requirements ticked on each project.",
        ["ProjectSubItems"] = "The unpriced checklist under each project item.",
        ["ProjectTickets"] = "Which tickets each project is linked to.",
        ["Projects"] = "Purchasing projects: one row per project.",
        ["PurchasingRequirements"] = "The list of purchasing requirements offered on new projects.",
        ["RequireCloseMessageCategories"] = "Categories whose tickets need a closing message.",
        ["RequireCloseMessagePriorities"] = "Priorities whose tickets need a closing message.",
        ["RolePermissions"] = "What each role grants: one row per ticked box.",
        ["Roles"] = "Roles. The Allow... columns are from the old permission model and are no longer used - RolePermissions holds what each role grants.",
        ["SchoolPeriods"] = "The lessons in the school day, for SLAs counted in periods.",
        ["Sessions"] = "Who is signed in, and until when. Signing out or a password change removes the row.",
        ["SpiceworksImports"] = "Each import from Spiceworks: when, by whom, from which file, and what it brought across.",
        ["SpiceworksLinks"] = "What each Spiceworks ticket, comment, history line and requester became here, so a later import updates rather than duplicates. Created is 1 where the import made the record.",
        ["SizeHistory"] = "One reading a day of how big the database and attachments are, for the growth figures on this page.",
        ["SlaCategories"] = "Which ticket categories each SLA covers.",
        ["SlaPauseStatuses"] = "Ticket statuses that stop the SLA clock.",
        ["SlaPriorities"] = "Which priorities each SLA covers.",
        ["Slas"] = "SLAs: how long a ticket has. The Priority column is from before SLAs could cover several - SlaPriorities holds them now.",
        ["SpendingBands"] = "The finance policy's spending bands.",
        ["StatusDescriptions"] = "What each ticket status means.",
        ["Statuses"] = "The list of ticket statuses.",
        ["Suppliers"] = "The supplier directory.",
        ["TechnicianTeams"] = "The list of technician teams.",
        ["Technicians"] = "Staff accounts that sign in to the helpdesk.",
        ["TicketActivities"] = "Each ticket's History tab: field changes, links, removals.",
        ["TicketAssets"] = "Which assets each ticket is about.",
        ["TicketAttachments"] = "Files attached to tickets (the files themselves are in the attachments folder, named by Id).",
        ["TicketAttributeCategories"] = "Which ticket categories a custom ticket attribute applies to. None means all.",
        ["TicketAttributeDefinitions"] = "Custom ticket attributes (Settings → Ticket custom attributes).",
        ["TicketAttributeValues"] = "Each ticket's answers to the custom ticket attributes.",
        ["TicketComments"] = "Comments and internal notes on tickets, including replies from the staff portal.",
        ["TicketLinks"] = "Tickets linked to each other - related, or a follow-up.",
        ["TicketParts"] = "Parts used on each ticket, and how many.",
        ["TicketSlaPauses"] = "When each ticket's SLA clock was stopped, and started again.",
        ["TicketTemplateAttributes"] = "The custom attribute answers each ticket template fills in.",
        ["TicketTemplates"] = "Saved starting points for new tickets.",
        ["Tickets"] = "Tickets: one row per ticket. The AssetId column is from before a ticket could have several assets - TicketAssets holds them now.",
        ["Users"] = "Requesters: the staff directory, and their staff portal sign-in."
    };

    // ---- Secrets ----

    private static readonly Dictionary<string, HashSet<string>> SecretColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Users"] = new(StringComparer.OrdinalIgnoreCase) { "PasswordHash" },
        ["Technicians"] = new(StringComparer.OrdinalIgnoreCase) { "PasswordHash", "TotpSecret", "RecoveryCodes", "RecoveryKeyHash" },
        ["Sessions"] = new(StringComparer.OrdinalIgnoreCase) { "Id" }
    };

    public static bool IsSecret(string table, string column) => SecretColumns.TryGetValue(table, out var columns) && columns.Contains(column);

    // What a masked cell holds instead of its value, so the page can tell it from text that happens to read the same.
    public const string Masked = "•••• set";
    public sealed class Hidden
    {
        public static readonly Hidden Value = new();
        public override string ToString() => Masked;
    }

    // ---- Tables ----

    public sealed record TableSummary(string Name, string Description, long Rows);

    public IReadOnlyList<TableSummary> Tables()
    {
        using var connection = Open();
        return TableNames(connection)
            .Select(name => new TableSummary(name, Descriptions.GetValueOrDefault(name, ""), Count(connection, name, "", [])))
            .ToList();
    }

    public sealed record Column(string Name, string Type, bool Key, bool Secret, string? References);

    // Null for a name that isn't a table - the only way a table name gets into any SQL here.
    public IReadOnlyList<Column>? Columns(string? table)
    {
        using var connection = Open();
        return ResolveTable(connection, table) is { } name ? ColumnsOf(connection, name) : null;
    }

    public sealed record Query(string? Search, string? Column, string Operator, string? Value, bool NewestFirst);
    public sealed record Page(string Table, IReadOnlyList<Column> Columns, IReadOnlyList<object?[]> Rows, long Total, string? Problem);

    public static readonly string[] Operators = ["equals", "contains", "is empty"];

    // One page of a table, or every matching row (page 0) for a CSV. Null when the table doesn't exist.
    public Page? Read(string? table, Query query, bool showSecrets, int page)
    {
        using var connection = Open();
        if (ResolveTable(connection, table) is not { } name) return null;
        var columns = ColumnsOf(connection, name);
        var (where, parameters, problem) = Where(name, columns, query, showSecrets);
        var total = Count(connection, name, where, parameters);
        var rows = new List<object?[]>();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(c => Quote(c.Name)))} FROM {Quote(name)}{where} ORDER BY rowid {(query.NewestFirst ? "DESC" : "ASC")}"
            + (page > 0 ? $" LIMIT {PageSize} OFFSET {(page - 1) * PageSize}" : "") + ";";
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(ReadRow(reader, name, columns, showSecrets));
        return new Page(name, columns, rows, total, problem);
    }

    private static (string Where, List<(string, object)> Parameters, string? Problem) Where(string table, IReadOnlyList<Column> columns, Query query, bool showSecrets)
    {
        var clauses = new List<string>();
        var parameters = new List<(string, object)>();
        string? problem = null;
        var searchable = columns.Where(c => showSecrets || !c.Secret).ToList();
        if (!string.IsNullOrWhiteSpace(query.Search) && searchable.Count > 0)
        {
            clauses.Add("(" + string.Join(" OR ", searchable.Select(c => $"CAST({Quote(c.Name)} AS TEXT) LIKE $search ESCAPE '\\'")) + ")");
            parameters.Add(("$search", $"%{EscapeLike(query.Search.Trim())}%"));
        }
        if (!string.IsNullOrWhiteSpace(query.Column))
        {
            var column = columns.FirstOrDefault(c => string.Equals(c.Name, query.Column, StringComparison.OrdinalIgnoreCase));
            if (column is null) problem = $"{table} has no column called {query.Column}.";
            else if (column.Secret && !showSecrets) problem = $"{column.Name} holds sign-in secrets, so only an Administrator can filter on it.";
            else if (query.Operator == "is empty") clauses.Add($"({Quote(column.Name)} IS NULL OR CAST({Quote(column.Name)} AS TEXT) = '')");
            else if (query.Operator == "contains")
            {
                clauses.Add($"CAST({Quote(column.Name)} AS TEXT) LIKE $value ESCAPE '\\'");
                parameters.Add(("$value", $"%{EscapeLike(query.Value ?? "")}%"));
            }
            else
            {
                // Compared as text, so 1042 finds ticket 1042 whether the column holds a number or text.
                clauses.Add($"CAST({Quote(column.Name)} AS TEXT) = $value");
                parameters.Add(("$value", query.Value ?? ""));
            }
        }
        return (clauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", clauses), parameters, problem);
    }

    // ---- One record, across every table that mentions it ----

    public sealed record RecordKind(string Key, string Label, (string Table, string Where, string Why)[] Places);

    // Where each kind of record appears. $k is the record's key, compared as text.
    public static readonly RecordKind[] Kinds =
    [
        new("ticket", "Ticket",
        [
            ("Tickets", "Number = $k", "The ticket"),
            ("TicketComments", "TicketNumber = $k", "Comments and notes"),
            ("TicketActivities", "TicketNumber = $k", "History"),
            ("TicketAssets", "TicketNumber = $k", "Linked assets"),
            ("TicketParts", "TicketNumber = $k", "Parts used"),
            ("TicketAttachments", "TicketNumber = $k", "Attachments"),
            ("TicketAttributeValues", "TicketNumber = $k", "Custom attribute answers"),
            ("TicketSlaPauses", "TicketNumber = $k", "SLA clock stops"),
            ("TicketLinks", "TicketNumber = $k OR LinkedNumber = $k", "Links to other tickets"),
            ("ProjectTickets", "TicketNumber = $k", "Linked projects"),
            ("Onboardings", "TicketNumber = $k", "Onboarding (when this ticket is one)"),
            ("OnboardingTasks", "TicketNumber = $k", "Onboarding checklist"),
            ("OnboardingPackDocuments", "TicketNumber = $k", "Welcome pack documents"),
            ("DemoRecords", "EntityType = 'Ticket' AND EntityKey = $k", "Marked as demo data"),
            ("AuditLog", "EntityType = 'Ticket' AND EntityKey = $k", "Audit log")
        ]),
        new("asset", "Asset",
        [
            ("Assets", "Id = $k", "The asset"),
            ("AssetActivities", "AssetId = $k", "History"),
            ("AssetAssignments", "AssetId = $k", "Who has held it"),
            ("AssetComments", "AssetId = $k", "Comments"),
            ("AssetAttributeValues", "AssetId = $k", "Custom attribute answers"),
            ("TicketAssets", "AssetId = $k", "Tickets about it"),
            ("LoanKitAssets", "AssetId = $k", "Loan kits it is in"),
            ("OnboardingTasks", "AssetId = $k", "Onboarding tasks that issued it"),
            ("DemoRecords", "EntityType = 'Asset' AND EntityKey = $k", "Marked as demo data"),
            ("AuditLog", "EntityType = 'Asset' AND EntityKey = $k", "Audit log")
        ]),
        new("user", "Requester",
        [
            ("Users", "Id = $k", "The person"),
            ("Tickets", "RequesterId = $k", "Tickets they raised"),
            ("Assets", "AssignedUserId = $k", "Assets they hold"),
            ("AssetAssignments", "UserId = $k", "Assets they have held"),
            ("KitLoans", "BorrowerUserId = $k", "Loan kits they borrowed"),
            ("Projects", "RequesterId = $k", "Projects they asked for"),
            ("Onboardings", "StarterId = $k OR LineManagerId = $k", "Onboardings (as the new starter or line manager)"),
            ("Sessions", "AccountId = $k", "Staff portal sign-ins"),
            ("DemoRecords", "EntityType = 'User' AND EntityKey = $k", "Marked as demo data"),
            ("AuditLog", "EntityType = 'User' AND EntityKey = $k", "Audit log")
        ]),
        new("technician", "Staff account",
        [
            ("Technicians", "Id = $k", "The account"),
            ("Tickets", "TechnicianId = $k", "Tickets assigned to them"),
            ("Projects", "TechnicianId = $k", "Projects assigned to them"),
            ("OnboardingTasks", "TechnicianId = $k", "Onboarding tasks assigned to them"),
            ("Sessions", "AccountId = $k", "Sign-ins"),
            ("DemoRecords", "EntityType = 'Technician' AND EntityKey = $k", "Marked as demo data"),
            ("AuditLog", "EntityType = 'Technician' AND EntityKey = $k", "Audit log")
        ]),
        new("project", "Project",
        [
            ("Projects", "Number = $k", "The project"),
            ("ProjectNotes", "ProjectNumber = $k", "Notes"),
            ("ProjectActivities", "ProjectNumber = $k", "History"),
            ("ProjectRequirements", "ProjectNumber = $k", "Purchasing requirements"),
            ("ProjectTickets", "ProjectNumber = $k", "Linked tickets"),
            ("ProjectItems", "ProjectNumber = $k", "Items"),
            ("ProjectSubItems", "ItemId IN (SELECT Id FROM ProjectItems WHERE ProjectNumber = $k)", "Sub-items"),
            ("ProjectItemSuppliers", "ItemId IN (SELECT Id FROM ProjectItems WHERE ProjectNumber = $k)", "Suppliers quoting"),
            ("ProjectQuoteStatusChanges", "ItemId IN (SELECT Id FROM ProjectItems WHERE ProjectNumber = $k)", "Quote statuses"),
            ("ProjectPaymentLines", "ItemId IN (SELECT Id FROM ProjectItems WHERE ProjectNumber = $k)", "Quote prices"),
            ("ProjectQuoteDocuments", "ItemId IN (SELECT Id FROM ProjectItems WHERE ProjectNumber = $k)", "Quote files"),
            ("ProjectQuoteVersions", "ItemId IN (SELECT Id FROM ProjectItems WHERE ProjectNumber = $k)", "Earlier quote versions"),
            ("DemoRecords", "EntityType = 'Project' AND EntityKey = $k", "Marked as demo data"),
            ("AuditLog", "EntityType = 'Project' AND EntityKey = $k", "Audit log")
        ])
    ];

    public static RecordKind? FindKind(string? key) => Kinds.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public sealed record Found(string Table, string Why, IReadOnlyList<Column> Columns, IReadOnlyList<object?[]> Rows, long Total);

    // Only the tables with something in them, in the order above.
    public IReadOnlyList<Found> ForRecord(RecordKind kind, string key, bool showSecrets)
    {
        using var connection = Open();
        var tables = TableNames(connection).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<Found>();
        foreach (var (table, where, why) in kind.Places)
        {
            if (!tables.Contains(table)) continue;
            var columns = ColumnsOf(connection, table);
            var parameters = new List<(string, object)> { ("$k", key) };
            var total = Count(connection, table, $" WHERE {where}", parameters);
            if (total == 0) continue;
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(", ", columns.Select(c => Quote(c.Name)))} FROM {Quote(table)} WHERE {where} ORDER BY rowid LIMIT {RecordRowLimit};";
            command.Parameters.AddWithValue("$k", key);
            using var reader = command.ExecuteReader();
            var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(ReadRow(reader, table, columns, showSecrets));
            found.Add(new Found(table, why, columns, rows, total));
        }
        return found;
    }

    // ---- Showing a value ----

    // A cell as text, for the page and the CSV alike: NULL and an empty string are different things in raw data, so
    // they are shown differently.
    public static string Text(object? value) => value switch
    {
        null => "NULL",
        byte[] bytes => $"(BLOB, {bytes.Length} bytes)",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    // ---- Recording who looked ----

    // Each table or record looked at goes in the audit log - but paging through one table, or coming back to it within
    // a few minutes, is the same look and isn't recorded again. Exports are always recorded.
    private readonly ConcurrentDictionary<string, DateTime> _recentLooks = new();
    private static readonly TimeSpan SameLook = TimeSpan.FromMinutes(15);

    // "Viewed raw data" / "Exported raw data", about "Technicians table" or "Ticket 1042".
    public void RecordLook(string action, string subject, string details, bool always = false)
    {
        var actor = store.CurrentActor();
        var now = DateTime.UtcNow;
        var key = $"{actor.Id}|{actor.Name}|{action}|{subject}";
        if (!always && _recentLooks.TryGetValue(key, out var last) && now - last < SameLook) return;
        _recentLooks[key] = now;
        foreach (var stale in _recentLooks.Where(x => now - x.Value > SameLook).Select(x => x.Key).ToList()) _recentLooks.TryRemove(stale, out _);
        store.RecordEvent(new AuditEntry(now, "Database", null, null, subject, action, details) { By = actor });
    }

    public static bool IsAdministrator(System.Security.Claims.ClaimsPrincipal user) =>
        string.Equals(user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase);

    // ---- Plumbing ----

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private static List<string> TableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static string? ResolveTable(SqliteConnection connection, string? table) =>
        string.IsNullOrWhiteSpace(table) ? null : TableNames(connection).FirstOrDefault(x => string.Equals(x, table.Trim(), StringComparison.OrdinalIgnoreCase));

    private static List<Column> ColumnsOf(SqliteConnection connection, string table)
    {
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT \"from\", \"table\", \"to\" FROM pragma_foreign_key_list($t);";
            command.Parameters.AddWithValue("$t", table);
            using var reader = command.ExecuteReader();
            while (reader.Read()) references[reader.GetString(0)] = $"{reader.GetString(1)}.{(reader.IsDBNull(2) ? "" : reader.GetString(2))}".TrimEnd('.');
        }
        var columns = new List<Column>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, type, pk FROM pragma_table_info($t) ORDER BY cid;";
            command.Parameters.AddWithValue("$t", table);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                columns.Add(new Column(name, reader.GetString(1), reader.GetInt32(2) > 0, IsSecret(table, name), references.GetValueOrDefault(name)));
            }
        }
        return columns;
    }

    private static long Count(SqliteConnection connection, string table, string where, List<(string, object)> parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {Quote(table)}{where};";
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static object?[] ReadRow(SqliteDataReader reader, string table, IReadOnlyList<Column> columns, bool showSecrets)
    {
        var row = new object?[columns.Count];
        for (var i = 0; i < row.Length; i++)
        {
            var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
            row[i] = columns[i].Secret && !showSecrets && value is not null && Text(value).Length > 0 ? Hidden.Value : value;
        }
        return row;
    }

    // Names only ever come from the schema, but are quoted anyway.
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
