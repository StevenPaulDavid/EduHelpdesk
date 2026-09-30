using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Importing from Spiceworks: turning the preview (HelpdeskStore.SpiceworksImport.cs) into tickets. A backup is taken
// first. Everything goes in with one save, so an import either happens whole or not at all.
//
// Each Spiceworks ticket, comment, history line, time entry and requester is linked to what it became (SpiceworksLinks),
// with the import it came in, so a later import can recognise it and the last import can be undone.
public sealed partial class HelpdeskStore
{
    public sealed record SpiceworksImportRecord(Guid Id, DateTime At, string FileName, string Summary)
    {
        public Actor? By { get; init; }
    }

    // Kind is Ticket, Comment, Change, Labor or Requester; SpiceworksId is Spiceworks' own id for it (0 for the
    // placeholder requester). EntityKey is the ticket number, or the person's id. Created says the import made it,
    // rather than matching something already here - only what an import made is removed by undoing it.
    public sealed record SpiceworksLink(string Kind, long SpiceworksId, string EntityKey, Guid ImportId, bool Created);

    public const string SpiceworksPlaceholderName = "Logged in Spiceworks";
    public const string SpiceworksPlaceholderEmail = "logged-in-spiceworks@eduhelpdesk.invalid";
    public const string SpiceworksNumberAttribute = "Spiceworks number";

    public sealed record SpiceworksResult(int Tickets, int AlreadyImported, int Comments, int HistoryLines, int PeopleAdded, int ValuesAdded, int AttributesAdded, string BackupName);

    public IReadOnlyList<SpiceworksImportRecord> SpiceworksImports { get { lock (_sync) return _data.SpiceworksImports.OrderByDescending(x => x.At).ToList(); } }

    // How many of the export's tickets have been brought across already (they are left as they are, for now).
    public int SpiceworksAlreadyImported(SpiceworksExport export)
    {
        lock (_sync)
        {
            var linked = _data.SpiceworksLinks.Where(x => x.Kind == "Ticket").Select(x => x.SpiceworksId).ToHashSet();
            return export.Tickets.Count(x => linked.Contains(x.Id));
        }
    }

    public (bool Ok, string Message, SpiceworksResult? Result) ApplySpiceworksImport(SpiceworksExport export, IReadOnlyDictionary<string, string> choices, string fileName)
    {
        // A backup first, so the whole import can be walked back from Settings → Backups whatever happens.
        var (backedUp, backup) = CreateBackup(manual: false, label: "before-spiceworks");
        if (!backedUp) return (false, $"Nothing was imported: the backup taken first didn't work ({backup}). Fix that in Settings → Backups & data and try again.", null);
        var backupName = LastBackupFileName();

        lock (_sync)
        {
            var plan = PlanSpiceworksImport(export, choices);
            var now = DateTime.UtcNow;
            var me = CurrentActor();
            var importId = Guid.NewGuid();
            var links = _data.SpiceworksLinks.ToDictionary(x => (x.Kind, x.SpiceworksId));
            var newLinks = new List<SpiceworksLink>();
            void Link(string kind, long id, string key, bool created)
            {
                var link = new SpiceworksLink(kind, id, key, importId, created);
                if (links.TryAdd((kind, id), link)) newLinks.Add(link);
            }

            // ---- Values: what each Spiceworks status, priority and category becomes ----
            var valuesAdded = 0;
            var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in plan.Values)
            {
                var target = choice.Chosen;
                if (target == SpiceworksNewValue)
                {
                    var list = choice.Kind switch { "Status" => _data.Statuses, "Priority" => _data.Priorities, _ => _data.Categories };
                    target = choice.Kind == "Priority" ? SpiceworksPriorityName(choice.Value) : choice.Value;
                    if (list.FirstOrDefault(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase)) is { } already) target = already;
                    else { list.Add(target); valuesAdded++; }
                }
                mapped[choice.Key] = target;
            }
            string Value(string kind, string value) => mapped.GetValueOrDefault(ChoiceKey(kind, value)) ?? value;

            // ---- Custom fields, and the Spiceworks number, as ticket custom attributes ----
            var attributesAdded = 0;
            Guid AttributeFor(string label)
            {
                if (_data.TicketAttributeDefinitions.FirstOrDefault(x => string.Equals(x.Name, label, StringComparison.OrdinalIgnoreCase)) is { } existing) return existing.Id;
                var definition = new TicketAttributeDefinition(Guid.NewGuid(), label);
                _data.TicketAttributeDefinitions.Add(definition);
                attributesAdded++;
                return definition.Id;
            }
            var spiceworksNumber = AttributeFor(SpiceworksNumberAttribute);
            var attributeIds = export.Attributes.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Label);
            var valuesByTicket = export.AttributeValues.Where(x => attributeIds.ContainsKey(x.AttributeId)).ToLookup(x => x.TicketId);
            var attributeFor = new Dictionary<long, Guid>();

            // ---- People ----
            var usersByEmail = _data.Users.Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var endUsers = export.EndUsers.Where(x => !string.IsNullOrWhiteSpace(x.Email)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var peopleAdded = 0;
            UserRecord? Requester(long? endUserId)
            {
                if (endUserId is not { } id || !endUsers.TryGetValue(id, out var person)) return null;
                if (usersByEmail.TryGetValue(person.Email.Trim(), out var existing))
                {
                    Link("Requester", id, existing.Id.ToString(), created: false);
                    return existing;
                }
                var user = new UserRecord(Guid.NewGuid(), person.Name, person.Email.Trim(), "", "", IsActive: true);
                _data.Users.Add(WithLeaverDates(user, null));
                usersByEmail[user.Email] = user;
                Link("Requester", id, user.Id.ToString(), created: true);
                peopleAdded++;
                return user;
            }
            UserRecord Placeholder()
            {
                if (usersByEmail.TryGetValue(SpiceworksPlaceholderEmail, out var existing)) return existing;
                // Not a real person, so not active: it doesn't appear in pickers, and nobody can sign in as it.
                var user = new UserRecord(Guid.NewGuid(), SpiceworksPlaceholderName, SpiceworksPlaceholderEmail, "", "", IsActive: false);
                _data.Users.Add(user);
                usersByEmail[user.Email] = user;
                Link("Requester", 0, user.Id.ToString(), created: true);
                peopleAdded++;
                return user;
            }
            var technicians = export.Technicians.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => (Person: g.First(), Account: MatchTechnician(g.First())));
            Actor ActorFor(long? technicianId, long? endUserId = null)
            {
                if (technicianId is { } tech && technicians.TryGetValue(tech, out var found))
                    return found.Account is { } account ? new Actor(account.Id, account.Name) : new Actor(null, found.Person.Name);
                if (endUserId is { } user && endUsers.TryGetValue(user, out var person))
                    return new Actor(null, usersByEmail.TryGetValue(person.Email.Trim(), out var existing) ? existing.Name : person.Name);
                return new Actor(null, "Spiceworks");
            }

            // ---- Tickets ----
            var commentsByTicket = export.Comments.Where(x => x.Body.Length > 0).ToLookup(x => x.TicketId);
            var changesByTicket = export.Changes.ToLookup(x => x.TicketId);
            var laborsByTicket = export.Labors.ToLookup(x => x.TicketId);
            int created = 0, skipped = 0, comments = 0, historyLines = 0;
            foreach (var source in export.Tickets.OrderBy(x => x.Number))
            {
                if (links.ContainsKey(("Ticket", source.Id))) { skipped++; continue; }
                var number = ++_data.LastTicketNumber;
                var status = Value("Status", source.Status.Trim());
                var closed = string.Equals(status, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase);
                var requester = Requester(source.EndUserId);
                var assignee = source.AssigneeId is { } assigneeId && technicians.TryGetValue(assigneeId, out var tech) ? tech : default;
                var createdAt = source.CreatedAt.UtcDateTime;

                var history = new List<TicketActivity>();
                foreach (var change in changesByTicket[source.Id].OrderBy(x => x.CreatedAt))
                {
                    history.Add(new TicketActivity(change.Action == "ticket merged" ? "Merged in Spiceworks" : "Updated in Spiceworks",
                        change.Body.Length > 0 ? change.Body : change.Action, change.CreatedAt.UtcDateTime) { By = ActorFor(change.CreatorId) });
                    Link("Change", change.Id, number.ToString(CultureInfo.InvariantCulture), created: true);
                }
                foreach (var labor in laborsByTicket[source.Id].OrderBy(x => x.CreatedAt))
                {
                    history.Add(new TicketActivity("Time logged in Spiceworks", labor.Body.Length > 0 ? labor.Body : $"{labor.Minutes} minutes", labor.CreatedAt.UtcDateTime) { By = ActorFor(labor.UserId) });
                    Link("Labor", labor.Id, number.ToString(CultureInfo.InvariantCulture), created: true);
                }
                var notes = new List<string> { $"Spiceworks #{source.Number}, from {fileName}." };
                if (requester is null)
                    notes.Add(source.CreatorId is { } creator && technicians.TryGetValue(creator, out var by)
                        ? $"Logged by {by.Person.Name} in Spiceworks, with no requester."
                        : "Its requester isn't in the export.");
                if (assignee.Person is { } person && assignee.Account is null) notes.Add($"Was assigned to {person.Name} in Spiceworks, who has no account here.");
                if (source.MasterNumber is { } master) notes.Add($"Merged into Spiceworks #{master}.");
                history.Add(new TicketActivity("Imported from Spiceworks", string.Join(" ", notes), now) { By = me });
                historyLines += history.Count;

                var ticketComments = new List<TicketComment>();
                foreach (var comment in commentsByTicket[source.Id].OrderBy(x => x.CreatedAt))
                {
                    ticketComments.Add(new TicketComment(comment.Body, comment.CreatedAt.UtcDateTime, comment.Private)
                    {
                        By = ActorFor(comment.CreatorId, comment.EndUserId),
                        FromRequester = comment.EndUserId is not null && comment.CreatorId is null
                    });
                    Link("Comment", comment.Id, number.ToString(CultureInfo.InvariantCulture), created: true);
                }
                comments += ticketComments.Count;

                var category = Value("Category", source.CategoryId is { } categoryId && export.Categories.FirstOrDefault(x => x.Id == categoryId) is { } found && found.Name.Trim().Length > 0 ? found.Name.Trim() : SpiceworksNoCategory);
                _data.Tickets.Add(new TicketRecord(number, source.Summary.Length > 0 ? source.Summary : $"Spiceworks #{source.Number}", source.Description,
                    (requester ?? Placeholder()).Id, [], assignee.Account?.Id, Value("Priority", source.Priority.Trim()), status, category,
                    createdAt, closed ? (source.ClosedAt ?? source.UpdatedAt ?? source.CreatedAt).UtcDateTime : null,
                    SlaId: null, DueDate: source.DueAt?.UtcDateTime, DueDateOverridden: source.DueAt is not null, SlaOverridden: false,
                    TeamName: assignee.Account is { Team.Length: > 0 } account && _data.TechnicianTeams.Contains(account.Team, StringComparer.OrdinalIgnoreCase) ? account.Team : null)
                {
                    Comments = ticketComments,
                    History = history,
                    // Seen, as far as the portal's "New reply" flag is concerned: none of this is news to the requester.
                    RequesterSeenAt = now
                });
                _data.TicketAttributeValues.Add(new TicketAttributeValue(number, spiceworksNumber, source.Number.ToString(CultureInfo.InvariantCulture)));
                foreach (var value in valuesByTicket[source.Id].GroupBy(x => x.AttributeId).Select(g => g.First()))
                {
                    if (!attributeFor.TryGetValue(value.AttributeId, out var definition)) attributeFor[value.AttributeId] = definition = AttributeFor(attributeIds[value.AttributeId]);
                    _data.TicketAttributeValues.Add(new TicketAttributeValue(number, definition, value.Value));
                }
                Link("Ticket", source.Id, number.ToString(CultureInfo.InvariantCulture), created: true);
                created++;
            }

            // ---- Merges: linked to the ticket they went into, once both are here ----
            var numbers = links.Values.Where(x => x.Kind == "Ticket").ToDictionary(x => x.SpiceworksId, x => int.Parse(x.EntityKey, CultureInfo.InvariantCulture));
            var bySpiceworksNumber = export.Tickets.Where(x => numbers.ContainsKey(x.Id)).GroupBy(x => x.Number).ToDictionary(g => g.Key, g => numbers[g.First().Id]);
            foreach (var source in export.Tickets.Where(x => x.MasterNumber is not null))
                if (numbers.TryGetValue(source.Id, out var from) && bySpiceworksNumber.TryGetValue(source.MasterNumber!.Value, out var into) && from != into
                    && !_data.TicketLinks.Any(x => (x.TicketNumber == from && x.LinkedNumber == into) || (x.TicketNumber == into && x.LinkedNumber == from)))
                    _data.TicketLinks.Add(new TicketLink(from, into, "related"));

            var summary = $"{created:N0} ticket{(created == 1 ? "" : "s")}, {comments:N0} comment{(comments == 1 ? "" : "s")}, {historyLines:N0} history line{(historyLines == 1 ? "" : "s")}, {peopleAdded:N0} {(peopleAdded == 1 ? "person" : "people")} added"
                + (skipped > 0 ? $"; {skipped:N0} already imported and left as they were" : "");
            _data.SpiceworksLinks.AddRange(newLinks);
            _data.SpiceworksImports.Add(new SpiceworksImportRecord(importId, now, fileName, summary) { By = me });
            _pendingAudit.Add(new AuditEntry(now, "Tickets", null, null, "Spiceworks import", "Imported from Spiceworks", $"{fileName}: {summary}. Backup taken first: {backupName}."));
            Save();
            return (true, $"Imported from Spiceworks: {summary}.", new SpiceworksResult(created, skipped, comments, historyLines, peopleAdded, valuesAdded, attributesAdded, backupName));
        }
    }

    private string LastBackupFileName() { lock (_sync) return _data.LastBackupFile; }

    // ---- Storage ----

    private static void EnsureSpiceworksSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SpiceworksImports (Id TEXT PRIMARY KEY, At TEXT NOT NULL, FileName TEXT NOT NULL, Summary TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL);
            CREATE TABLE IF NOT EXISTS SpiceworksLinks (Kind TEXT NOT NULL, SpiceworksId INTEGER NOT NULL, EntityKey TEXT NOT NULL, ImportId TEXT NOT NULL, Created INTEGER NOT NULL,
                PRIMARY KEY (Kind, SpiceworksId));
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadSpiceworks(SqliteConnection connection, StoreData data)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, At, FileName, Summary, Actor, ActorId FROM SpiceworksImports ORDER BY At;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.SpiceworksImports.Add(new SpiceworksImportRecord(Guid.Parse(reader.GetString(0)), Date(reader, 1), reader.GetString(2), reader.GetString(3)) { By = ReadActor(reader, 4, 5) });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Kind, SpiceworksId, EntityKey, ImportId, Created FROM SpiceworksLinks ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.SpiceworksLinks.Add(new SpiceworksLink(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), Guid.Parse(reader.GetString(3)), reader.GetInt32(4) != 0));
        }
    }

    private static void WriteSpiceworks(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        foreach (var import in data.SpiceworksImports)
            Execute(connection, transaction, "INSERT INTO SpiceworksImports (Id, At, FileName, Summary, Actor, ActorId) VALUES ($id,$at,$file,$summary,$actor,$actorid);",
                ("$id", import.Id.ToString()), ("$at", Iso(import.At)), ("$file", import.FileName), ("$summary", import.Summary), ("$actor", import.By?.Name), ("$actorid", import.By?.Id?.ToString()));
        foreach (var link in data.SpiceworksLinks.DistinctBy(x => (x.Kind, x.SpiceworksId)))
            Execute(connection, transaction, "INSERT INTO SpiceworksLinks (Kind, SpiceworksId, EntityKey, ImportId, Created) VALUES ($kind,$id,$key,$import,$created);",
                ("$kind", link.Kind), ("$id", link.SpiceworksId), ("$key", link.EntityKey), ("$import", link.ImportId.ToString()), ("$created", link.Created ? 1 : 0));
    }
}
