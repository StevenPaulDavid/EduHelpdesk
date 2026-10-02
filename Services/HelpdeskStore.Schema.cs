using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The database schema, and the upgrades that bring an older database up to date on startup.
public sealed partial class HelpdeskStore
{
    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT NULL);
            CREATE TABLE IF NOT EXISTS Users (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Email TEXT NOT NULL, Department TEXT NULL, Location TEXT NULL);
            CREATE TABLE IF NOT EXISTS TechnicianTeams (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Technicians (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Email TEXT NOT NULL, Team TEXT NULL,
                FOREIGN KEY (Team) REFERENCES TechnicianTeams(Name) ON UPDATE CASCADE);
            CREATE TABLE IF NOT EXISTS Departments (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Locations (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetTypes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetMakes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetModels (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetModelMakes (Model TEXT PRIMARY KEY, Make TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Categories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Statuses (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS StatusDescriptions (Status TEXT PRIMARY KEY, Description TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS RequireCloseMessagePriorities (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS RequireCloseMessageCategories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Priorities (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Slas (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, Duration INTEGER NOT NULL, DurationUnit TEXT NOT NULL, Priority TEXT NULL, Description TEXT NULL);
            CREATE TABLE IF NOT EXISTS SlaPriorities (SlaId TEXT NOT NULL, Priority TEXT NOT NULL, PRIMARY KEY (SlaId, Priority), FOREIGN KEY (SlaId) REFERENCES Slas(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS SlaCategories (SlaId TEXT NOT NULL, Category TEXT NOT NULL, PRIMARY KEY (SlaId, Category), FOREIGN KEY (SlaId) REFERENCES Slas(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS Suppliers (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, ContactName TEXT NOT NULL DEFAULT '', Email TEXT NOT NULL DEFAULT '', Phone TEXT NOT NULL DEFAULT '', AddressLine1 TEXT NOT NULL DEFAULT '', AddressLine2 TEXT NOT NULL DEFAULT '', City TEXT NOT NULL DEFAULT '', StateRegion TEXT NOT NULL DEFAULT '', PostalCode TEXT NOT NULL DEFAULT '', Country TEXT NOT NULL DEFAULT '', Website TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Parts (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Sku TEXT NOT NULL DEFAULT '', Category TEXT NOT NULL DEFAULT '', QuantityOnHand INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TicketParts (TicketNumber INTEGER NOT NULL, PartId TEXT NOT NULL, Quantity INTEGER NOT NULL,
                PRIMARY KEY (TicketNumber, PartId),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS Assets (Id TEXT PRIMARY KEY, AssetTag TEXT NOT NULL, Make TEXT NOT NULL DEFAULT '', Type TEXT NOT NULL, Model TEXT NOT NULL,
                SerialNumber TEXT NOT NULL, Location TEXT NOT NULL, AssignedUserId TEXT NULL, SupplierId TEXT NULL,
                FOREIGN KEY (AssignedUserId) REFERENCES Users(Id) ON DELETE SET NULL);
            CREATE TABLE IF NOT EXISTS Tickets (Number INTEGER PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NOT NULL,
                RequesterId TEXT NOT NULL, AssetId TEXT NULL, TechnicianId TEXT NULL, Priority TEXT NOT NULL, Status TEXT NOT NULL,
                Category TEXT NOT NULL, CreatedAt TEXT NOT NULL, ClosedAt TEXT NULL, SlaId TEXT NULL, DueDate TEXT NULL, DueDateOverridden INTEGER NOT NULL DEFAULT 0, SlaOverridden INTEGER NOT NULL DEFAULT 0, TeamName TEXT NULL,
                FOREIGN KEY (RequesterId) REFERENCES Users(Id), FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE SET NULL,
                FOREIGN KEY (TechnicianId) REFERENCES Technicians(Id) ON DELETE SET NULL);
            CREATE TABLE IF NOT EXISTS TicketComments (Id INTEGER PRIMARY KEY AUTOINCREMENT, TicketNumber INTEGER NOT NULL,
                Text TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, TicketNumber INTEGER NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS AssetComments (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL,
                Text TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS AssetActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS BrandingSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), BrandName TEXT NOT NULL,
                DashboardEyebrow TEXT NOT NULL, DashboardTitle TEXT NOT NULL, DashboardDescription TEXT NOT NULL,
                PrimaryColor TEXT NOT NULL, AccentColor TEXT NOT NULL, BackgroundColor TEXT NOT NULL, DarkMode INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS AssetAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, AssetType TEXT NULL,
                FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS AssetAttributeValues (AssetId TEXT NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (AssetId, AttributeDefinitionId),
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE,
                FOREIGN KEY (AttributeDefinitionId) REFERENCES AssetAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Category TEXT NULL, FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS TicketAttributeValues (TicketNumber INTEGER NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, AttributeDefinitionId), FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (AttributeDefinitionId) REFERENCES TicketAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAssets (TicketNumber INTEGER NOT NULL, AssetId TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, AssetId),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
        using var migration = connection.CreateCommand();
        migration.CommandText = "ALTER TABLE Assets ADD COLUMN Make TEXT NOT NULL DEFAULT '';";
        try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var supplierMigration = connection.CreateCommand();
        supplierMigration.CommandText = "ALTER TABLE Assets ADD COLUMN SupplierId TEXT NULL;";
        try { supplierMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var attributeMigration = connection.CreateCommand();
        attributeMigration.CommandText = "ALTER TABLE AssetAttributeDefinitions ADD COLUMN FieldType TEXT NOT NULL DEFAULT 'single-line';";
        try { attributeMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var choicesMigration = connection.CreateCommand();
        choicesMigration.CommandText = "ALTER TABLE AssetAttributeDefinitions ADD COLUMN Choices TEXT NOT NULL DEFAULT '';";
        try { choicesMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var slaMigration = connection.CreateCommand();
        slaMigration.CommandText = "ALTER TABLE Tickets ADD COLUMN SlaId TEXT NULL; ALTER TABLE Tickets ADD COLUMN DueDate TEXT NULL; ALTER TABLE Tickets ADD COLUMN DueDateOverridden INTEGER NOT NULL DEFAULT 0; ALTER TABLE Tickets ADD COLUMN SlaOverridden INTEGER NOT NULL DEFAULT 0; ALTER TABLE Tickets ADD COLUMN TeamName TEXT NULL;";
        try { slaMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        foreach (var sql in new[] { "ALTER TABLE Slas ADD COLUMN Priority TEXT NULL;", "ALTER TABLE Slas ADD COLUMN Description TEXT NULL;", "ALTER TABLE TicketAttributeDefinitions ADD COLUMN FieldType TEXT NOT NULL DEFAULT 'single-line';", "ALTER TABLE TicketAttributeDefinitions ADD COLUMN Choices TEXT NOT NULL DEFAULT '';" })
        { using var m = connection.CreateCommand(); m.CommandText = sql; try { m.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { } }
        MigrateAssetAttributeTypeToNullable(connection);
        using var scopeTables = connection.CreateCommand();
        scopeTables.CommandText = """
            CREATE TABLE IF NOT EXISTS AssetAttributeAssetTypes (AttributeId TEXT NOT NULL, AssetType TEXT NOT NULL,
                PRIMARY KEY (AttributeId, AssetType), FOREIGN KEY (AttributeId) REFERENCES AssetAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAttributeCategories (AttributeId TEXT NOT NULL, Category TEXT NOT NULL,
                PRIMARY KEY (AttributeId, Category), FOREIGN KEY (AttributeId) REFERENCES TicketAttributeDefinitions(Id) ON DELETE CASCADE);
            """;
        scopeTables.ExecuteNonQuery();
        using var auditTable = connection.CreateCommand();
        auditTable.CommandText = "CREATE TABLE IF NOT EXISTS AuditLog (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Area TEXT NOT NULL, EntityType TEXT NULL, EntityKey TEXT NULL, Entity TEXT NOT NULL, Action TEXT NOT NULL, Details TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL);";
        auditTable.ExecuteNonQuery();
        using var rolePermissionTable = connection.CreateCommand();
        // No foreign key to Roles(Name): WriteData deletes and reinserts everything, and a role rename would otherwise
        // have to be ordered against this table. Rows whose role no longer exists are simply ignored on load.
        rolePermissionTable.CommandText = "CREATE TABLE IF NOT EXISTS RolePermissions (RoleName TEXT NOT NULL, Permission TEXT NOT NULL, PRIMARY KEY (RoleName, Permission));";
        rolePermissionTable.ExecuteNonQuery();
        using var demoTable = connection.CreateCommand();
        // No foreign keys on purpose: deleting a demo record by hand should leave a harmless stale pointer here, not
        // break the next save. RemoveDemoData and the Settings panel both ignore ids that no longer resolve.
        demoTable.CommandText = "CREATE TABLE IF NOT EXISTS DemoRecords (EntityType TEXT NOT NULL, EntityKey TEXT NOT NULL, PRIMARY KEY (EntityType, EntityKey));";
        demoTable.ExecuteNonQuery();
        foreach (var sql in new[]
        {
            "ALTER TABLE Assets ADD COLUMN Status TEXT NOT NULL DEFAULT 'In use';",
            "ALTER TABLE Assets ADD COLUMN PurchaseDate TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN PurchasePrice TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN PurchaseOrder TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Assets ADD COLUMN WarrantyEnd TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN ReplacementDate TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN LoanDueDate TEXT NULL;",
            "ALTER TABLE TicketComments ADD COLUMN IsInternal INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Tickets ADD COLUMN TicketType TEXT NOT NULL DEFAULT 'Incident';",
            "ALTER TABLE Tickets ADD COLUMN Location TEXT NULL;",
            "ALTER TABLE Tickets ADD COLUMN SubCategory TEXT NOT NULL DEFAULT '';",
            // ServiceItems and TicketTemplates are created further down, after this loop, so these only upgrade a database
            // that already has the tables - a new one gets the columns from its CREATE. Existing catalogue items stay
            // visible in the portal; existing templates stay hidden, as they were written for technicians.
            "ALTER TABLE ServiceItems ADD COLUMN HelperLine TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE ServiceItems ADD COLUMN ShowInPortal INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE TicketTemplates ADD COLUMN HelperLine TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE TicketTemplates ADD COLUMN ShowInPortal INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE ServiceItems ADD COLUMN PortalOrder INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TicketTemplates ADD COLUMN PortalOrder INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Users ADD COLUMN PasswordHash TEXT NULL;",
            "ALTER TABLE Users ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE Users ADD COLUMN RequirePasswordChange INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Technicians ADD COLUMN Role TEXT NOT NULL DEFAULT 'Technician';",
            "ALTER TABLE Technicians ADD COLUMN PasswordHash TEXT NULL;",
            "ALTER TABLE Technicians ADD COLUMN RequirePasswordChange INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Technicians ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;",
            // Finance and audit reporting: the supplier's quote number (orders placed via the Trust often have no PO),
            // and disposal, which is a status rather than a delete so the record survives for an auditor.
            "ALTER TABLE Assets ADD COLUMN QuoteReference TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Assets ADD COLUMN DisposalDate TEXT NULL;",
            "ALTER TABLE Assets ADD COLUMN DisposalMethod TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Assets ADD COLUMN DisposalProceeds TEXT NULL;",
            "ALTER TABLE Parts ADD COLUMN Location TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Parts ADD COLUMN ReorderThreshold INTEGER NULL;",
            // Actor attribution. Nullable on purpose: everything recorded before this existed keeps no actor rather
            // than being backfilled with a guess, so "no name shown" honestly means "we did not record it".
            "ALTER TABLE AuditLog ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE AuditLog ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE TicketActivities ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE TicketActivities ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE TicketComments ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE TicketComments ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE AssetActivities ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE AssetActivities ADD COLUMN ActorId TEXT NULL;",
            "ALTER TABLE AssetComments ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE AssetComments ADD COLUMN ActorId TEXT NULL;",
            // PartActivities is created further down, after this loop, so these two only ever upgrade a database that
            // already has the table - a brand-new one gets the columns from its CREATE instead.
            "ALTER TABLE PartActivities ADD COLUMN Actor TEXT NULL;",
            "ALTER TABLE PartActivities ADD COLUMN ActorId TEXT NULL;",
            // AssetAssignments is created after this loop too, so the same applies - see its CREATE below.
            "ALTER TABLE AssetAssignments ADD COLUMN Reason TEXT NULL;",
            "ALTER TABLE AssetAssignments ADD COLUMN KitLoanId TEXT NULL;"
        })
        {
            using var m = connection.CreateCommand();
            m.CommandText = sql;
            try { m.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
        using var assetTables = connection.CreateCommand();
        assetTables.CommandText = """
            CREATE TABLE IF NOT EXISTS AssetStatuses (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS PartCategories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS PartLocations (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetTypeLifespans (AssetType TEXT PRIMARY KEY, Years INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS AssetAssignments (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL, UserId TEXT NULL, UserName TEXT NOT NULL,
                StartedAt TEXT NULL, EndedAt TEXT NULL, DueBack TEXT NULL, Reason TEXT NULL, KitLoanId TEXT NULL,
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            """;
        assetTables.ExecuteNonQuery();
        using var ticketTables = connection.CreateCommand();
        ticketTables.CommandText = """
            CREATE TABLE IF NOT EXISTS TicketAttachments (Id TEXT PRIMARY KEY, TicketNumber INTEGER NOT NULL, FileName TEXT NOT NULL, ContentType TEXT NOT NULL,
                Size INTEGER NOT NULL, UploadedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketLinks (TicketNumber INTEGER NOT NULL, LinkedNumber INTEGER NOT NULL, Kind TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, LinkedNumber),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE, FOREIGN KEY (LinkedNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketTemplates (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, TicketType TEXT NOT NULL, Title TEXT NOT NULL, Description TEXT NOT NULL,
                Category TEXT NOT NULL, Priority TEXT NOT NULL, SlaId TEXT NULL, HelperLine TEXT NOT NULL DEFAULT '', ShowInPortal INTEGER NOT NULL DEFAULT 0, PortalOrder INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS ServiceItems (Id TEXT PRIMARY KEY, Category TEXT NOT NULL, Name TEXT NOT NULL, DefaultPriority TEXT NOT NULL DEFAULT '', HelperLine TEXT NOT NULL DEFAULT '', ShowInPortal INTEGER NOT NULL DEFAULT 1, PortalOrder INTEGER NOT NULL DEFAULT 0);
            -- No foreign key to Categories: a rename or delete moves or removes the row, and a stale one is ignored on load.
            CREATE TABLE IF NOT EXISTS CategoryStyles (Category TEXT PRIMARY KEY, Icon TEXT NOT NULL, Color TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TicketTemplateAttributes (TemplateId TEXT NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (TemplateId, AttributeDefinitionId), FOREIGN KEY (TemplateId) REFERENCES TicketTemplates(Id) ON DELETE CASCADE);
            """;
        ticketTables.ExecuteNonQuery();
        using var partTables = connection.CreateCommand();
        partTables.CommandText = """
            CREATE TABLE IF NOT EXISTS PartSuppliers (PartId TEXT NOT NULL, SupplierId TEXT NOT NULL,
                PRIMARY KEY (PartId, SupplierId),
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE,
                FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PartAssetTypes (PartId TEXT NOT NULL, AssetType TEXT NOT NULL,
                PRIMARY KEY (PartId, AssetType), FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PartActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, PartId TEXT NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE);
            """;
        partTables.ExecuteNonQuery();
        using var loanTables = connection.CreateCommand();
        loanTables.CommandText = """
            CREATE TABLE IF NOT EXISTS LoanReasons (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS SchoolPeriods (Position INTEGER PRIMARY KEY, Name TEXT NOT NULL, StartTime TEXT NOT NULL, EndTime TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS LoanKits (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Notes TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL, IsRetired INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS LoanKitAssets (KitId TEXT NOT NULL, AssetId TEXT NOT NULL,
                PRIMARY KEY (KitId, AssetId),
                FOREIGN KEY (KitId) REFERENCES LoanKits(Id) ON DELETE CASCADE,
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS KitLoans (Id TEXT PRIMARY KEY, KitId TEXT NOT NULL, BorrowerUserId TEXT NULL,
                BorrowerName TEXT NOT NULL, Reason TEXT NOT NULL, IssuedAt TEXT NOT NULL, DueBack TEXT NOT NULL,
                ReturnedAt TEXT NULL, IssuedBy TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (KitId) REFERENCES LoanKits(Id) ON DELETE CASCADE);
            """;
        loanTables.ExecuteNonQuery();
        using var roleTable = connection.CreateCommand();
        roleTable.CommandText = """
            CREATE TABLE IF NOT EXISTS Roles (Name TEXT PRIMARY KEY, AllowSettings INTEGER NOT NULL DEFAULT 0, AllowManageRoles INTEGER NOT NULL DEFAULT 0,
                AllowManageStaff INTEGER NOT NULL DEFAULT 0, AllowManageRequesters INTEGER NOT NULL DEFAULT 0, AllowManageAssets INTEGER NOT NULL DEFAULT 0,
                AllowManageSuppliers INTEGER NOT NULL DEFAULT 0, AllowManageParts INTEGER NOT NULL DEFAULT 0, AllowTicketDestructive INTEGER NOT NULL DEFAULT 0,
                AllowChangeWorkingAs INTEGER NOT NULL DEFAULT 0, IsProtected INTEGER NOT NULL DEFAULT 0);
            """;
        roleTable.ExecuteNonQuery();
        EnsureProjectSchema(connection);
        EnsureProjectTicketSchema(connection);
        EnsureTicketProcessSchema(connection);
        EnsureLifecycleSchema(connection);
        EnsureSessionSchema(connection);
        EnsureNotificationSchema(connection);
        EnsureTwoFactorSchema(connection);
        EnsureRecoveryKeySchema(connection);
        EnsureOnboardingSchema(connection);
    }

    private static void MigrateAssetAttributeTypeToNullable(SqliteConnection connection)
    {
        var assetTypeIsNotNull = false;
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(AssetAttributeDefinitions);";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "AssetType", StringComparison.OrdinalIgnoreCase))
                    assetTypeIsNotNull = reader.GetInt32(3) == 1;
            }
        }
        if (!assetTypeIsNotNull) return;
        using var migrate = connection.CreateCommand();
        migrate.CommandText = """
            ALTER TABLE AssetAttributeDefinitions RENAME TO AssetAttributeDefinitions_old;
            CREATE TABLE AssetAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, AssetType TEXT NULL, FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            INSERT INTO AssetAttributeDefinitions (Id, Name, AssetType, FieldType, Choices) SELECT Id, Name, AssetType, FieldType, Choices FROM AssetAttributeDefinitions_old;
            DROP TABLE AssetAttributeDefinitions_old;
            """;
        migrate.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static void DropLegacyStore(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DROP TABLE IF EXISTS Store;";
        command.ExecuteNonQuery();
    }

    private static void SetMetadata(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
