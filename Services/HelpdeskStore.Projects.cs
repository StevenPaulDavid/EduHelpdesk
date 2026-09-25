using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Purchasing projects: raised by line managers and SLT from the staff portal, assigned by the project lead, and worked
// by a technician who gathers supplier quotes into a proposal. Like tickets, a project carries its own history and
// notes, so the audit log only records deletions (see AuditTracker) and folds the rest in from the project itself.
public sealed partial class HelpdeskStore
{
    public const int MaxProjectTitleLength = 200;
    public const int MaxProjectTextLength = 5000;
    public const int MaxProjectOtherLength = 500;

    public IReadOnlyList<ProjectRecord> Projects { get { lock (_sync) return _data.Projects.OrderByDescending(x => x.Number).ToList(); } }
    public IReadOnlyList<string> PurchasingRequirements { get { lock (_sync) return _data.PurchasingRequirements.ToList(); } }
    public ProjectRecord? FindProject(int number) { lock (_sync) return _data.Projects.FirstOrDefault(x => x.Number == number); }

    // The starting purchasing requirements, put in place once. Gated on a version so a school that deletes the option
    // doesn't find it back after the next restart, and run from both the constructor (an upgrading database) and
    // SeedStarterData (a new install or a factory reset, whose fresh StoreData starts at version 0).
    private void EnsureProjectDefaults()
    {
        if (_data.ProjectsVersion < 1)
        {
            EnsureOptions(_data.PurchasingRequirements, ["Lease / finance option"]);
            _data.ProjectsVersion = 1;
        }
        if (_data.ProjectsVersion < 2)
        {
            // A typical school finance policy, as something to edit rather than type from nothing. Every school's
            // thresholds differ, and these say so. Seeded once, so a school that deletes them doesn't get them back.
            if (_data.SpendingBands.Count == 0)
                _data.SpendingBands =
                [
                    new(Guid.NewGuid(), "Low value", 0m, 999.99m, 1, "One written quote or a catalogue price. Example band - edit to match your finance policy."),
                    new(Guid.NewGuid(), "Medium value", 1_000m, 4_999.99m, 2, "Two written quotes. Example band - edit to match your finance policy."),
                    new(Guid.NewGuid(), "High value", 5_000m, 24_999.99m, 3, "Three written quotes; business manager approval. Example band - edit to match your finance policy."),
                    new(Guid.NewGuid(), "Tender", 25_000m, null, 3, "Formal tender or an approved framework; governors' approval. Example band - edit to match your finance policy.")
                ];
            _data.ProjectsVersion = 2;
        }
    }

    // Who can be given a project: an active account whose role can actually work one. Offering anyone else would hand
    // the project to someone who is refused the moment they open it.
    public bool CanWorkProjects(TechnicianRecord technician) =>
        technician.IsActive && RoleAllows(technician.Role, Modules.Projects, ModulePermission.Edit);

    // The project lead works entirely in the staff portal, so every lead-only action there asks this rather than trusting
    // what the page showed: the tick can be taken away between loading a form and sending it.
    public bool IsActiveProjectLead(Guid userId)
    {
        lock (_sync) return _data.Users.Any(x => x.Id == userId && x.IsActive && x.IsProjectLead);
    }

    // A portal visitor may open a project they raised, and the lead may open any project - they need to see it to assign it.
    public ProjectRecord? PortalProject(Guid userId, int number)
    {
        lock (_sync)
        {
            var project = _data.Projects.FirstOrDefault(x => x.Number == number);
            return project is not null && (project.RequesterId == userId || IsActiveProjectLead(userId)) ? project : null;
        }
    }

    // A line manager's priority is a suggestion for the lead to confirm. When the lead raises a project themselves, their
    // priority is the decision, and they can hand it to a technician in the same step.
    public (bool Ok, string Message, int Number) RaiseProject(Guid requesterId, string? title, DateOnly? dueDate, string? itemsWanted,
        IEnumerable<string>? requirements, string? other, int suggestedPriority, Guid? technicianId = null)
    {
        lock (_sync)
        {
            var requester = _data.Users.FirstOrDefault(x => x.Id == requesterId);
            if (requester is null || !requester.IsActive) return (false, "Your account couldn't be found.", 0);
            if (!requester.MayRaiseProjects) return (false, "Your account isn't set up to raise projects. Ask the IT team if you need to.", 0);
            if (technicianId is not null && !requester.IsProjectLead) return (false, "Only the project lead can choose the technician.", 0);
            TechnicianRecord? technician = null;
            if (technicianId is { } techId)
            {
                technician = _data.Technicians.FirstOrDefault(x => x.Id == techId);
                if (technician is null || !CanWorkProjects(technician)) return (false, "That technician can't be given projects. Choose someone else, or leave it for now.", 0);
            }
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (dueDate is null) return (false, "Choose the date you need the proposal by.", 0);
            if (dueDate < today) return (false, "The date you need the proposal by can't be in the past.", 0);
            if (!ProjectPriorities.IsValid(suggestedPriority)) return (false, "Choose a priority from 1 to 5.", 0);
            if (CheckProjectText(title, itemsWanted, other) is { } error) return (false, error, 0);
            if (ResolveRequirements(requirements, []) is not { } resolved) return (false, "One of the purchasing requirements isn't on the list any more. Reload the page and try again.", 0);

            var number = NextProjectNumber();
            var now = DateTime.UtcNow;
            var actor = CurrentActor();
            var history = new List<ProjectActivity>
            {
                new("Project raised", requester.IsProjectLead
                    ? $"Raised by {requester.Name}, the project lead, at priority {ProjectPriorities.Label(suggestedPriority)}. Proposal needed by {Day(dueDate.Value)}."
                    : $"Raised by {requester.Name} with a suggested priority of {ProjectPriorities.Label(suggestedPriority)}. Proposal needed by {Day(dueDate.Value)}.", now) { By = actor }
            };
            if (technician is not null)
                history.Add(new ProjectActivity("Assignment", $"Assigned to {technician.Name}; Status: {ProjectStatuses.New} → {ProjectStatuses.GatheringQuotes}.", now) { By = actor });
            var project = new ProjectRecord(number, title!.Trim(), requester.Id, dueDate.Value, itemsWanted!.Trim(), now)
            {
                PurchasingRequirements = resolved,
                PurchasingOther = (other ?? string.Empty).Trim(),
                SuggestedPriority = suggestedPriority,
                Priority = requester.IsProjectLead ? suggestedPriority : null,
                TechnicianId = technician?.Id,
                Status = technician is null ? ProjectStatuses.New : ProjectStatuses.GatheringQuotes,
                History = history
            };
            _data.Projects.Add(project);
            Save();
            return (true, technician is null ? $"Project {project.Reference} sent to the IT team." : $"Project {project.Reference} raised and assigned to {technician.Name}.", number);
        }
    }

    // The technician's tidy-up of what the requester typed. Requirements that have since been removed from Settings stay
    // valid on a project that already had them, so saving an old project never fails over a list someone else edited.
    public (bool Ok, string Message) UpdateProjectDetails(int number, string? title, DateOnly? dueDate, string? itemsWanted, IEnumerable<string>? requirements, string? other)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var project = _data.Projects[index];
            if (dueDate is null) return (false, "Enter the date the proposal is needed by.");
            if (CheckProjectText(title, itemsWanted, other) is { } error) return (false, error);
            if (ResolveRequirements(requirements, project.PurchasingRequirements) is not { } resolved) return (false, "One of the purchasing requirements isn't on the list any more. Reload the page and try again.");

            var updated = project with
            {
                Title = title!.Trim(),
                DueDate = dueDate.Value,
                ItemsWanted = itemsWanted!.Trim(),
                PurchasingRequirements = resolved,
                PurchasingOther = (other ?? string.Empty).Trim()
            };
            var changes = new List<string>();
            if (updated.Title != project.Title) changes.Add($"Title: {project.Title} → {updated.Title}");
            if (updated.DueDate != project.DueDate) changes.Add($"Needed by: {Day(project.DueDate)} → {Day(updated.DueDate)}");
            if (updated.ItemsWanted != project.ItemsWanted) changes.Add("Items wanted were edited");
            if (!updated.PurchasingRequirements.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(project.PurchasingRequirements.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
                || updated.PurchasingOther != project.PurchasingOther)
                changes.Add($"Purchasing requirements: {DescribeRequirements(updated)}");
            if (changes.Count == 0) return (true, "Nothing had changed.");
            _data.Projects[index] = WithHistory(updated, "Details updated", string.Join("; ", changes) + ".");
            Save();
            return (true, "Project details saved.");
        }
    }

    // The project lead's decision: the priority that counts, and who does the work. Assigning a new project also starts
    // it moving, since the next thing that happens to it is the technician asking suppliers for quotes.
    public (bool Ok, string Message) AssignProject(int number, Guid? technicianId, int priority)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var project = _data.Projects[index];
            if (!project.IsActive) return (false, "This project is closed. Reopen it before reassigning it.");
            if (!ProjectPriorities.IsValid(priority)) return (false, "Choose a priority from 1 to 5.");
            TechnicianRecord? technician = null;
            if (technicianId is { } id)
            {
                technician = _data.Technicians.FirstOrDefault(x => x.Id == id);
                if (technician is null) return (false, "That technician couldn't be found.");
                if (!CanWorkProjects(technician)) return (false, $"{technician.Name} can't be given projects: their account is inactive or their role can't edit projects.");
            }

            var changes = new List<string>();
            if (project.Priority != priority)
                changes.Add(project.Priority is { } old
                    ? $"Priority: {ProjectPriorities.Label(old)} → {ProjectPriorities.Label(priority)}"
                    : $"Priority confirmed as {ProjectPriorities.Label(priority)}" + (priority == project.SuggestedPriority ? " (as suggested)" : $" (suggested {ProjectPriorities.Label(project.SuggestedPriority)})"));
            if (project.TechnicianId != technicianId)
                changes.Add(technician is null ? "Technician removed" : $"Assigned to {technician.Name}");
            var status = project.Status == ProjectStatuses.New && technician is not null ? ProjectStatuses.GatheringQuotes : project.Status;
            if (status != project.Status) changes.Add($"Status: {project.Status} → {status}");
            if (changes.Count == 0) return (true, "Nothing had changed.");

            _data.Projects[index] = WithHistory(project with { Priority = priority, TechnicianId = technicianId, Status = status }, "Assignment", string.Join("; ", changes) + ".");
            Save();
            return (true, technician is null ? "Assignment saved." : $"Assigned to {technician.Name}.");
        }
    }

    // Moves an open project between its working stages, or reopens a closed one. Closing goes through CloseProject so
    // that it always records an outcome.
    public (bool Ok, string Message) SetProjectStatus(int number, string? status)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var project = _data.Projects[index];
            var target = ProjectStatuses.Find(status);
            if (target is null) return (false, "Choose a status.");
            if (target == ProjectStatuses.Closed) return (false, "Use Close project, so the outcome is recorded.");
            if (target == project.Status) return (true, "Nothing had changed.");
            var reopening = project.Status == ProjectStatuses.Closed;
            var updated = project with { Status = target, ClosedAt = reopening ? null : project.ClosedAt, Outcome = reopening ? null : project.Outcome, OutcomeNote = reopening ? null : project.OutcomeNote };
            _data.Projects[index] = WithHistory(updated, reopening ? "Project reopened" : "Status changed",
                reopening ? $"Reopened as {target}. It had been closed as {project.Outcome ?? "closed"}." : $"{project.Status} → {target}.");
            Save();
            return (true, reopening ? "Project reopened." : $"Status set to {target}.");
        }
    }

    public (bool Ok, string Message) CloseProject(int number, string? outcome, string? note)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var project = _data.Projects[index];
            if (!project.IsActive) return (false, "This project is already closed.");
            var resolved = ProjectOutcomes.Find(outcome);
            if (resolved is null) return (false, "Choose how the project ended: approved, not approved or cancelled.");
            var text = (note ?? string.Empty).Trim();
            if (text.Length > MaxProjectTextLength) return (false, $"Keep the closing note under {MaxProjectTextLength} characters.");
            var updated = project with { Status = ProjectStatuses.Closed, ClosedAt = DateTime.UtcNow, Outcome = resolved, OutcomeNote = text.Length == 0 ? null : text };
            _data.Projects[index] = WithHistory(updated, "Project closed", $"Closed as {resolved}." + (text.Length == 0 ? "" : $" {text}"));
            Save();
            return (true, $"Project closed as {resolved}.");
        }
    }

    // A shared note is what the requester sees and can reply to in the portal; an internal one never leaves the IT team.
    // Portal notes always arrive as shared - the page calling this decides, never the requester.
    public (bool Ok, string Message) AddProjectNote(int number, string? text, bool isInternal)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0) return (false, "Write a note before sending.");
            if (value.Length > MaxProjectTextLength) return (false, $"Keep notes under {MaxProjectTextLength} characters.");
            var project = _data.Projects[index];
            _data.Projects[index] = project with { Notes = [.. project.Notes, new ProjectNote(value, DateTime.UtcNow, isInternal) { By = CurrentActor() }] };
            Save();
            return (true, isInternal ? "Internal note added." : "Note added.");
        }
    }

    public (bool Ok, string Message) DeleteProject(int number)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Project was not found.");
            var reference = _data.Projects[index].Reference;
            var files = QuoteFiles(_data.Projects[index].Items.SelectMany(x => x.Suppliers)).ToList();
            _data.Projects.RemoveAt(index);
            Save();
            foreach (var id in files) TryDelete(AttachmentFile(id));
            return (true, $"Project {reference} deleted.");
        }
    }

    // What the project lead sees when choosing a technician: how many active projects each person has, and how many of
    // them sit at each priority. The two answers can disagree - one person may have the fewest projects but the most
    // urgent ones - so both are worked out and both are flagged, and the lead decides.
    public IReadOnlyList<ProjectWorkload> ProjectWorkloads()
    {
        lock (_sync)
        {
            var rows = _data.Technicians.Where(CanWorkProjects).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(technician =>
            {
                var counts = new int[ProjectPriorities.Lowest];
                foreach (var project in _data.Projects.Where(x => x.IsActive && x.TechnicianId == technician.Id))
                    counts[Math.Clamp(project.EffectivePriority, ProjectPriorities.Highest, ProjectPriorities.Lowest) - 1]++;
                return new ProjectWorkload(technician, counts);
            }).ToList();
            if (rows.Count == 0) return rows;

            var fewest = rows.Min(x => x.Total);
            // "Lightest on urgent work" compares P1 counts first, then P2 on a tie, and so on: one P1 outweighs any number
            // of P5s, which is how the lead described weighing it.
            var lightest = rows.Select(x => x.ByPriority).Aggregate((best, next) => ComparePriorityLoad(next, best) < 0 ? next : best);
            return rows.Select(x => x with
            {
                FewestProjects = x.Total == fewest,
                LightestUrgentLoad = ComparePriorityLoad(x.ByPriority, lightest) == 0
            }).ToList();
        }
    }

    private static int ComparePriorityLoad(int[] first, int[] second)
    {
        for (var i = 0; i < first.Length; i++)
            if (first[i] != second[i]) return first[i].CompareTo(second[i]);
        return 0;
    }

    private int NextProjectNumber()
    {
        _data.LastProjectNumber = Math.Max(_data.LastProjectNumber, _data.Projects.Select(x => x.Number).DefaultIfEmpty(0).Max()) + 1;
        return _data.LastProjectNumber;
    }

    private ProjectRecord WithHistory(ProjectRecord project, string action, string details) =>
        project with { History = [.. project.History, new ProjectActivity(action, details, DateTime.UtcNow) { By = CurrentActor() }] };

    private static string? CheckProjectText(string? title, string? itemsWanted, string? other)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Give the project a title.";
        if (title.Trim().Length > MaxProjectTitleLength) return $"Keep the title under {MaxProjectTitleLength} characters.";
        if (string.IsNullOrWhiteSpace(itemsWanted)) return "List the items wanted.";
        if (itemsWanted.Trim().Length > MaxProjectTextLength) return $"Keep the items wanted under {MaxProjectTextLength} characters.";
        if ((other ?? string.Empty).Trim().Length > MaxProjectOtherLength) return $"Keep the other purchasing requirements under {MaxProjectOtherLength} characters.";
        return null;
    }

    // Ticked requirements, in the casing and order of the Settings list. Null when one is neither on the list nor already
    // on the project - a stale form, or a hand-made request.
    private List<string>? ResolveRequirements(IEnumerable<string>? requested, IReadOnlyList<string> alreadyOnProject)
    {
        var resolved = new List<string>();
        foreach (var value in NormalizeScope(requested))
        {
            var match = _data.PurchasingRequirements.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))
                ?? alreadyOnProject.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            resolved.Add(match);
        }
        return resolved;
    }

    public static string DescribeRequirements(ProjectRecord project)
    {
        var all = project.PurchasingRequirements.Concat(string.IsNullOrWhiteSpace(project.PurchasingOther) ? [] : [$"Other: {project.PurchasingOther}"]).ToList();
        return all.Count == 0 ? "none" : string.Join(", ", all);
    }

    private static string Day(DateOnly value) => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static void EnsureProjectSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PurchasingRequirements (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS SpendingBands (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, FromAmount TEXT NOT NULL, UpToAmount TEXT NULL,
                QuotesNeeded INTEGER NOT NULL, Requirements TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS Projects (Number INTEGER PRIMARY KEY, Title TEXT NOT NULL, RequesterId TEXT NOT NULL, TechnicianId TEXT NULL,
                DueDate TEXT NOT NULL, ItemsWanted TEXT NOT NULL, PurchasingOther TEXT NOT NULL DEFAULT '', SuggestedPriority INTEGER NOT NULL,
                Priority INTEGER NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL, ClosedAt TEXT NULL, Outcome TEXT NULL, OutcomeNote TEXT NULL,
                FOREIGN KEY (RequesterId) REFERENCES Users(Id), FOREIGN KEY (TechnicianId) REFERENCES Technicians(Id) ON DELETE SET NULL);
            CREATE TABLE IF NOT EXISTS ProjectRequirements (ProjectNumber INTEGER NOT NULL, Requirement TEXT NOT NULL, Position INTEGER NOT NULL,
                PRIMARY KEY (ProjectNumber, Requirement), FOREIGN KEY (ProjectNumber) REFERENCES Projects(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectNotes (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProjectNumber INTEGER NOT NULL, Text TEXT NOT NULL,
                CreatedAt TEXT NOT NULL, IsInternal INTEGER NOT NULL DEFAULT 0, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (ProjectNumber) REFERENCES Projects(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProjectNumber INTEGER NOT NULL, Action TEXT NOT NULL,
                Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (ProjectNumber) REFERENCES Projects(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectItems (Id TEXT PRIMARY KEY, ProjectNumber INTEGER NOT NULL, Position INTEGER NOT NULL, Name TEXT NOT NULL,
                Quantity INTEGER NOT NULL, FOREIGN KEY (ProjectNumber) REFERENCES Projects(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectSubItems (Id TEXT PRIMARY KEY, ItemId TEXT NOT NULL, Position INTEGER NOT NULL, Name TEXT NOT NULL,
                Quantity INTEGER NOT NULL, FOREIGN KEY (ItemId) REFERENCES ProjectItems(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectItemSuppliers (ItemId TEXT NOT NULL, SupplierId TEXT NOT NULL, Position INTEGER NOT NULL, ValidUntil TEXT NULL,
                PRIMARY KEY (ItemId, SupplierId), FOREIGN KEY (ItemId) REFERENCES ProjectItems(Id) ON DELETE CASCADE,
                FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id));
            CREATE TABLE IF NOT EXISTS ProjectQuoteStatusChanges (Id INTEGER PRIMARY KEY AUTOINCREMENT, ItemId TEXT NOT NULL, SupplierId TEXT NOT NULL,
                Status TEXT NOT NULL, At TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (ItemId, SupplierId) REFERENCES ProjectItemSuppliers(ItemId, SupplierId) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectQuoteVersions (Id TEXT PRIMARY KEY, ItemId TEXT NOT NULL, SupplierId TEXT NOT NULL, Position INTEGER NOT NULL,
                ArchivedAt TEXT NOT NULL, Reference TEXT NOT NULL DEFAULT '', ValidUntil TEXT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (ItemId, SupplierId) REFERENCES ProjectItemSuppliers(ItemId, SupplierId) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectQuoteDocuments (Id TEXT PRIMARY KEY, ItemId TEXT NOT NULL, SupplierId TEXT NOT NULL, VersionId TEXT NULL,
                Position INTEGER NOT NULL, FileName TEXT NOT NULL, ContentType TEXT NOT NULL, Size INTEGER NOT NULL, UploadedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (ItemId, SupplierId) REFERENCES ProjectItemSuppliers(ItemId, SupplierId) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS ProjectPaymentLines (Id TEXT PRIMARY KEY, ItemId TEXT NOT NULL, SupplierId TEXT NOT NULL, VersionId TEXT NULL,
                Position INTEGER NOT NULL, Description TEXT NOT NULL, Amount TEXT NOT NULL, Frequency TEXT NOT NULL, TermYears INTEGER NOT NULL, Vat TEXT NOT NULL,
                FOREIGN KEY (ItemId, SupplierId) REFERENCES ProjectItemSuppliers(ItemId, SupplierId) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
        foreach (var sql in new[] { "ALTER TABLE ProjectItems ADD COLUMN ChosenSupplierId TEXT NULL;", "ALTER TABLE ProjectItemSuppliers ADD COLUMN Reference TEXT NOT NULL DEFAULT '';" })
        {
            using var itemMigration = connection.CreateCommand();
            itemMigration.CommandText = sql;
            try { itemMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
        foreach (var sql in new[] { "ALTER TABLE Users ADD COLUMN CanRaiseProjects INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE Users ADD COLUMN IsProjectLead INTEGER NOT NULL DEFAULT 0;" })
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = sql;
            try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
    }

    private static void ReadProjects(SqliteConnection connection, StoreData data)
    {
        ReadStrings(connection, "PurchasingRequirements", data.PurchasingRequirements);
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'ProjectsVersion';") as string, out var version)) data.ProjectsVersion = version;
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LastProjectNumber';") as string, out var last)) data.LastProjectNumber = last;
        data.SpendingBandsIncludeVat = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SpendingBandsIncludeVat';") as string == "1";
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, FromAmount, UpToAmount, QuotesNeeded, Requirements FROM SpendingBands;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                static decimal? Amount(string? text) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
                data.SpendingBands.Add(new SpendingBand(Guid.Parse(reader.GetString(0)), reader.GetString(1), Amount(reader.GetString(2)) ?? 0m,
                    Amount(NullableString(reader, 3)), Math.Max(0, reader.GetInt32(4)), reader.GetString(5)));
            }
            data.SpendingBands = data.SpendingBands.OrderBy(x => x.From).ToList();
        }

        using (var command = connection.CreateCommand())
        {
            // Ordinal-indexed: add new columns to the END of this list (see the note on the Assets reader).
            command.CommandText = "SELECT Number, Title, RequesterId, TechnicianId, DueDate, ItemsWanted, PurchasingOther, SuggestedPriority, Priority, Status, CreatedAt, ClosedAt, Outcome, OutcomeNote FROM Projects ORDER BY Number;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.Projects.Add(new ProjectRecord(reader.GetInt32(0), reader.GetString(1), Guid.Parse(reader.GetString(2)),
                    NullableDateOnly(reader, 4) ?? DateOnly.FromDateTime(Date(reader, 10)), reader.GetString(5), Date(reader, 10))
                {
                    TechnicianId = NullableGuid(reader, 3),
                    PurchasingOther = reader.GetString(6),
                    SuggestedPriority = Math.Clamp(reader.GetInt32(7), ProjectPriorities.Highest, ProjectPriorities.Lowest),
                    Priority = reader.IsDBNull(8) ? null : Math.Clamp(reader.GetInt32(8), ProjectPriorities.Highest, ProjectPriorities.Lowest),
                    Status = ProjectStatuses.Find(reader.GetString(9)) ?? ProjectStatuses.New,
                    ClosedAt = reader.IsDBNull(11) ? null : Date(reader, 11),
                    Outcome = NullableString(reader, 12),
                    OutcomeNote = NullableString(reader, 13)
                });
        }
        var byNumber = data.Projects.ToDictionary(x => x.Number);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ProjectNumber, Requirement FROM ProjectRequirements ORDER BY ProjectNumber, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (byNumber.TryGetValue(reader.GetInt32(0), out var project)) project.PurchasingRequirements.Add(reader.GetString(1));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ProjectNumber, Text, CreatedAt, IsInternal, Actor, ActorId FROM ProjectNotes ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (byNumber.TryGetValue(reader.GetInt32(0), out var project))
                    project.Notes.Add(new ProjectNote(reader.GetString(1), Date(reader, 2), reader.GetInt32(3) != 0) { By = ReadActor(reader, 4, 5) });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ProjectNumber, Action, Details, CreatedAt, Actor, ActorId FROM ProjectActivities ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (byNumber.TryGetValue(reader.GetInt32(0), out var project))
                    project.History.Add(new ProjectActivity(reader.GetString(1), reader.GetString(2), Date(reader, 3)) { By = ReadActor(reader, 4, 5) });
        }
        var items = new Dictionary<Guid, ProjectItem>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, ProjectNumber, Name, Quantity, ChosenSupplierId FROM ProjectItems ORDER BY ProjectNumber, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!byNumber.TryGetValue(reader.GetInt32(1), out var project)) continue;
                var item = new ProjectItem(Guid.Parse(reader.GetString(0)), reader.GetString(2), reader.GetInt32(3)) { ChosenSupplierId = NullableGuid(reader, 4) };
                project.Items.Add(item);
                items[item.Id] = item;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ItemId, Id, Name, Quantity FROM ProjectSubItems ORDER BY ItemId, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (items.TryGetValue(Guid.Parse(reader.GetString(0)), out var item))
                    item.SubItems.Add(new ProjectSubItem(Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3)));
        }
        var itemSuppliers = new Dictionary<(Guid, Guid), ItemSupplier>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ItemId, SupplierId, ValidUntil, Reference FROM ProjectItemSuppliers ORDER BY ItemId, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var itemId = Guid.Parse(reader.GetString(0));
                if (!items.TryGetValue(itemId, out var item)) continue;
                var supplier = new ItemSupplier(Guid.Parse(reader.GetString(1))) { ValidUntil = NullableDateOnly(reader, 2), Reference = NullableString(reader, 3) ?? "" };
                item.Suppliers.Add(supplier);
                itemSuppliers[(itemId, supplier.SupplierId)] = supplier;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ItemId, SupplierId, Status, At, Actor, ActorId FROM ProjectQuoteStatusChanges ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (itemSuppliers.TryGetValue((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))), out var supplier))
                    supplier.StatusHistory.Add(new QuoteStatusChange(QuoteStatuses.Find(reader.GetString(2)) ?? QuoteStatuses.NotRequested, Date(reader, 3)) { By = ReadActor(reader, 4, 5) });
        }
        // Documents and payment lines with no VersionId belong to the quote as it stands; the rest to an archived version.
        var versions = new Dictionary<Guid, QuoteVersion>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ItemId, SupplierId, Id, ArchivedAt, Reference, ValidUntil, Actor, ActorId FROM ProjectQuoteVersions ORDER BY ItemId, SupplierId, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!itemSuppliers.TryGetValue((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))), out var supplier)) continue;
                var archived = new QuoteVersion(Guid.Parse(reader.GetString(2)), Date(reader, 3), reader.GetString(4), NullableDateOnly(reader, 5)) { By = ReadActor(reader, 6, 7) };
                supplier.PreviousVersions.Add(archived);
                versions[archived.Id] = archived;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ItemId, SupplierId, VersionId, Id, FileName, ContentType, Size, UploadedAt, Actor, ActorId FROM ProjectQuoteDocuments ORDER BY Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var document = new QuoteDocument(Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), Date(reader, 7)) { By = ReadActor(reader, 8, 9) };
                if (NullableGuid(reader, 2) is { } versionId) { if (versions.TryGetValue(versionId, out var archived)) archived.Documents.Add(document); }
                else if (itemSuppliers.TryGetValue((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))), out var supplier)) supplier.Documents.Add(document);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ItemId, SupplierId, VersionId, Id, Description, Amount, Frequency, TermYears, Vat FROM ProjectPaymentLines ORDER BY Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var line = new PaymentLine(Guid.Parse(reader.GetString(3)), reader.GetString(4),
                    decimal.TryParse(reader.GetString(5), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
                    PaymentFrequencies.Find(reader.GetString(6)) ?? PaymentFrequencies.OneOff, Math.Max(1, reader.GetInt32(7)), VatTreatments.Find(reader.GetString(8)) ?? VatTreatments.Standard);
                if (NullableGuid(reader, 2) is { } versionId) { if (versions.TryGetValue(versionId, out var archived)) archived.PaymentLines.Add(line); }
                else if (itemSuppliers.TryGetValue((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))), out var supplier)) supplier.PaymentLines.Add(line);
            }
        }
    }

    // Called from WriteData after Users and Technicians have been inserted, which Projects points at.
    private static void WriteProjects(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        InsertStrings(connection, transaction, "PurchasingRequirements", data.PurchasingRequirements);
        SetMetadata(connection, transaction, "ProjectsVersion", data.ProjectsVersion.ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "SpendingBandsIncludeVat", data.SpendingBandsIncludeVat ? "1" : "0");
        foreach (var band in data.SpendingBands)
            Execute(connection, transaction, "INSERT INTO SpendingBands (Id, Name, FromAmount, UpToAmount, QuotesNeeded, Requirements) VALUES ($id,$name,$from,$upto,$quotes,$requirements);",
                ("$id", band.Id.ToString()), ("$name", band.Name), ("$from", band.From.ToString(CultureInfo.InvariantCulture)),
                ("$upto", band.UpTo?.ToString(CultureInfo.InvariantCulture)), ("$quotes", band.QuotesNeeded), ("$requirements", band.Requirements ?? ""));
        SetMetadata(connection, transaction, "LastProjectNumber", Math.Max(data.LastProjectNumber, data.Projects.Select(x => x.Number).DefaultIfEmpty(0).Max()).ToString(CultureInfo.InvariantCulture));
        var technicianIds = data.Technicians.Select(x => x.Id).ToHashSet();
        var supplierIds = data.Suppliers.Select(x => x.Id).ToHashSet();
        foreach (var project in data.Projects)
        {
            Execute(connection, transaction, "INSERT INTO Projects (Number, Title, RequesterId, TechnicianId, DueDate, ItemsWanted, PurchasingOther, SuggestedPriority, Priority, Status, CreatedAt, ClosedAt, Outcome, OutcomeNote) VALUES ($number,$title,$requester,$technician,$due,$items,$other,$suggested,$priority,$status,$created,$closed,$outcome,$note);",
                ("$number", project.Number), ("$title", project.Title), ("$requester", project.RequesterId.ToString()),
                // A technician deleted without the project being unpicked would otherwise break the whole save.
                ("$technician", project.TechnicianId is { } tech && technicianIds.Contains(tech) ? tech.ToString() : null),
                ("$due", IsoDay(project.DueDate)), ("$items", project.ItemsWanted), ("$other", project.PurchasingOther ?? string.Empty),
                ("$suggested", project.SuggestedPriority), ("$priority", project.Priority), ("$status", project.Status),
                ("$created", Iso(project.CreatedAt)), ("$closed", project.ClosedAt.HasValue ? Iso(project.ClosedAt.Value) : null),
                ("$outcome", project.Outcome), ("$note", project.OutcomeNote));
            var position = 0;
            foreach (var requirement in project.PurchasingRequirements.Distinct(StringComparer.OrdinalIgnoreCase))
                Execute(connection, transaction, "INSERT INTO ProjectRequirements (ProjectNumber, Requirement, Position) VALUES ($number,$requirement,$position);",
                    ("$number", project.Number), ("$requirement", requirement), ("$position", position++));
            foreach (var note in project.Notes)
                Execute(connection, transaction, "INSERT INTO ProjectNotes (ProjectNumber, Text, CreatedAt, IsInternal, Actor, ActorId) VALUES ($number,$text,$created,$internal,$actor,$actorid);",
                    ("$number", project.Number), ("$text", note.Text), ("$created", Iso(note.CreatedAt)), ("$internal", note.IsInternal ? 1 : 0),
                    ("$actor", note.By?.Name), ("$actorid", note.By?.Id?.ToString()));
            foreach (var activity in project.History)
                Execute(connection, transaction, "INSERT INTO ProjectActivities (ProjectNumber, Action, Details, CreatedAt, Actor, ActorId) VALUES ($number,$action,$details,$created,$actor,$actorid);",
                    ("$number", project.Number), ("$action", activity.Action), ("$details", activity.Details), ("$created", Iso(activity.CreatedAt)),
                    ("$actor", activity.By?.Name), ("$actorid", activity.By?.Id?.ToString()));
            for (var i = 0; i < project.Items.Count; i++)
            {
                var item = project.Items[i];
                Execute(connection, transaction, "INSERT INTO ProjectItems (Id, ProjectNumber, Position, Name, Quantity, ChosenSupplierId) VALUES ($id,$number,$position,$name,$quantity,$chosen);",
                    ("$id", item.Id.ToString()), ("$number", project.Number), ("$position", i), ("$name", item.Name), ("$quantity", item.Quantity),
                    ("$chosen", item.ChosenSupplierId is { } chosen && item.Suppliers.Any(x => x.SupplierId == chosen && supplierIds.Contains(chosen)) ? chosen.ToString() : null));
                for (var j = 0; j < item.SubItems.Count; j++)
                    Execute(connection, transaction, "INSERT INTO ProjectSubItems (Id, ItemId, Position, Name, Quantity) VALUES ($id,$item,$position,$name,$quantity);",
                        ("$id", item.SubItems[j].Id.ToString()), ("$item", item.Id.ToString()), ("$position", j), ("$name", item.SubItems[j].Name), ("$quantity", item.SubItems[j].Quantity));
                // A supplier missing from the directory would fail the foreign key and lose the whole save; DeleteSupplier
                // refuses while one is on a project, so this only guards against a record changed by hand.
                var supplierPosition = 0;
                foreach (var supplier in item.Suppliers.Where(x => supplierIds.Contains(x.SupplierId)).DistinctBy(x => x.SupplierId))
                {
                    Execute(connection, transaction, "INSERT INTO ProjectItemSuppliers (ItemId, SupplierId, Position, ValidUntil, Reference) VALUES ($item,$supplier,$position,$valid,$reference);",
                        ("$item", item.Id.ToString()), ("$supplier", supplier.SupplierId.ToString()), ("$position", supplierPosition++), ("$valid", IsoDay(supplier.ValidUntil)), ("$reference", supplier.Reference ?? ""));
                    foreach (var change in supplier.StatusHistory)
                        Execute(connection, transaction, "INSERT INTO ProjectQuoteStatusChanges (ItemId, SupplierId, Status, At, Actor, ActorId) VALUES ($item,$supplier,$status,$at,$actor,$actorid);",
                            ("$item", item.Id.ToString()), ("$supplier", supplier.SupplierId.ToString()), ("$status", change.Status), ("$at", Iso(change.At)),
                            ("$actor", change.By?.Name), ("$actorid", change.By?.Id?.ToString()));
                    WriteQuote(connection, transaction, item.Id, supplier.SupplierId, null, supplier.Documents, supplier.PaymentLines);
                    for (var v = 0; v < supplier.PreviousVersions.Count; v++)
                    {
                        var version = supplier.PreviousVersions[v];
                        Execute(connection, transaction, "INSERT INTO ProjectQuoteVersions (Id, ItemId, SupplierId, Position, ArchivedAt, Reference, ValidUntil, Actor, ActorId) VALUES ($id,$item,$supplier,$position,$at,$reference,$valid,$actor,$actorid);",
                            ("$id", version.Id.ToString()), ("$item", item.Id.ToString()), ("$supplier", supplier.SupplierId.ToString()), ("$position", v), ("$at", Iso(version.ArchivedAt)),
                            ("$reference", version.Reference ?? ""), ("$valid", IsoDay(version.ValidUntil)), ("$actor", version.By?.Name), ("$actorid", version.By?.Id?.ToString()));
                        WriteQuote(connection, transaction, item.Id, supplier.SupplierId, version.Id, version.Documents, version.PaymentLines);
                    }
                }
            }
        }
    }
}

public sealed partial class HelpdeskStore
{
    // A quote's files and payment lines - the current ones (versionId null) or an archived version's.
    private static void WriteQuote(SqliteConnection connection, SqliteTransaction transaction, Guid itemId, Guid supplierId, Guid? versionId,
        List<QuoteDocument> documents, List<PaymentLine> lines)
    {
        for (var i = 0; i < documents.Count; i++)
            Execute(connection, transaction, "INSERT INTO ProjectQuoteDocuments (Id, ItemId, SupplierId, VersionId, Position, FileName, ContentType, Size, UploadedAt, Actor, ActorId) VALUES ($id,$item,$supplier,$version,$position,$name,$type,$size,$at,$actor,$actorid);",
                ("$id", documents[i].Id.ToString()), ("$item", itemId.ToString()), ("$supplier", supplierId.ToString()), ("$version", versionId?.ToString()), ("$position", i),
                ("$name", documents[i].FileName), ("$type", documents[i].ContentType), ("$size", documents[i].Size), ("$at", Iso(documents[i].UploadedAt)),
                ("$actor", documents[i].By?.Name), ("$actorid", documents[i].By?.Id?.ToString()));
        for (var i = 0; i < lines.Count; i++)
            Execute(connection, transaction, "INSERT INTO ProjectPaymentLines (Id, ItemId, SupplierId, VersionId, Position, Description, Amount, Frequency, TermYears, Vat) VALUES ($id,$item,$supplier,$version,$position,$description,$amount,$frequency,$term,$vat);",
                ("$id", lines[i].Id.ToString()), ("$item", itemId.ToString()), ("$supplier", supplierId.ToString()), ("$version", versionId?.ToString()), ("$position", i),
                ("$description", lines[i].Description), ("$amount", lines[i].Amount.ToString(CultureInfo.InvariantCulture)), ("$frequency", lines[i].Frequency),
                ("$term", lines[i].TermYears), ("$vat", lines[i].Vat));
    }
}

// One technician's active projects, counted by priority (index 0 is P1). The two flags are set by
// HelpdeskStore.ProjectWorkloads, which is the only place that can compare everyone at once.
public sealed record ProjectWorkload(TechnicianRecord Technician, int[] ByPriority)
{
    public int Total => ByPriority.Sum();
    public bool FewestProjects { get; init; }
    public bool LightestUrgentLoad { get; init; }
    public int CountAt(int priority) => ByPriority[priority - 1];

    // "2× P1, 1× P4" - the shape of someone's load in a few characters, for the picker and its summary line.
    public string Describe() => Total == 0
        ? "no active projects"
        : string.Join(", ", ProjectPriorities.All.Where(p => CountAt(p) > 0).Select(p => $"{CountAt(p)}× P{p}"));

    // The two readings of "who has the most room", in one sentence. They often point at different people, and when they
    // do the lead needs both in front of them rather than a single recommendation that hides the trade-off.
    public static string? Summary(IReadOnlyList<ProjectWorkload> rows)
    {
        if (rows.Count < 2) return null;
        static string Names(IEnumerable<ProjectWorkload> group) => string.Join(" and ", group.Select(x => x.Technician.Name));
        var fewest = rows.Where(x => x.FewestProjects).ToList();
        var lightest = rows.Where(x => x.LightestUrgentLoad).ToList();
        if (fewest.Select(x => x.Technician.Id).SequenceEqual(lightest.Select(x => x.Technician.Id)))
            return $"{Names(fewest)} {(fewest.Count == 1 ? "has" : "have")} both the fewest projects and the least urgent work.";
        return $"{Names(fewest)} {(fewest.Count == 1 ? "has" : "have")} the fewest projects ({fewest[0].Total}). "
            + $"{Names(lightest)} {(lightest.Count == 1 ? "has" : "have")} the least urgent work ({lightest[0].Describe()}).";
    }
}

// What the shared technician picker (Pages/Shared/_WorkloadPicker) needs: used by the lead in the staff portal and by the
// backup Assign on the helpdesk project page, so both show exactly the same comparison.
public sealed record WorkloadPicker(IReadOnlyList<ProjectWorkload> Rows, Guid? Selected, string NobodyLabel = "Nobody yet");
