using System.Globalization;
using System.Text.Json;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Importing from Spiceworks: turning the preview (HelpdeskStore.SpiceworksImport.cs) into tickets, again and again while
// the school runs both systems, and undoing the last import. A backup is taken first; everything goes in with one save.
//
// Each Spiceworks ticket, comment, history line, time entry and requester is linked to what it became (SpiceworksLinks).
// For each imported ticket the store also keeps what Spiceworks said about it at the last import (SpiceworksTicketStates):
// comparing that with the next export shows what changed in Spiceworks, and comparing it with the ticket shows whether
// it was changed here. Changed only in Spiceworks: the ticket is updated. Changed in both: new comments and history still
// come across, and each field that differs is listed in the preview - kept as it is here unless Spiceworks' is chosen.
//
// Each import also keeps, per ticket it made or changed, what undoing it needs (SpiceworksUndo): the ticket's fields
// before, and how it was left - undo is refused once anyone has changed a ticket since, so no work done here is lost.
public sealed partial class HelpdeskStore
{
    public sealed record SpiceworksImportRecord(Guid Id, DateTime At, string FileName, string Summary)
    {
        public Actor? By { get; init; }
    }

    // Kind is Ticket, Comment, Change, Labor or Requester; SpiceworksId is Spiceworks' own id for it (0 for the
    // placeholder requester). EntityKey is the ticket number - "1042|<ticks>" for a comment or history line, so undo can
    // find it again - or the person's id. Created says the import made it rather than matching something already here.
    public sealed record SpiceworksLink(string Kind, long SpiceworksId, string EntityKey, Guid ImportId, bool Created);

    // The fields an import sets on a ticket, as they were - enough to put a ticket back when an import is undone.
    public sealed record SpiceworksTicketFields(string Title, string Description, string Status, string Priority, string Category,
        Guid? TechnicianId, Guid RequesterId, DateTime? DueDate, bool DueDateOverridden, DateTime? ClosedAt);

    // One ticket one import made (Before is null) or changed. PreviousState is what Spiceworks said before this import;
    // After is how the import left the ticket, with its comment and history counts - if the ticket no longer matches
    // them, someone has worked on it since and the import can't be undone.
    public sealed record SpiceworksUndoEntry(Guid ImportId, int TicketNumber, SpiceworksTicketFields? Before, string[]? PreviousState,
        string[] After, int CommentsAfter, int HistoryAfter);

    public const string SpiceworksPlaceholderName = "Logged in Spiceworks";
    public const string SpiceworksPlaceholderEmail = "logged-in-spiceworks@eduhelpdesk.invalid";
    public const string SpiceworksNumberAttribute = "Spiceworks number";

    // The fields compared between an import and the ticket, in this order in every state and comparison.
    public static readonly string[] SpiceworksFields = ["Title", "Description", "Status", "Priority", "Category", "Technician", "Requester", "Due date"];

    public sealed record SpiceworksResult(int Tickets, int Updated, int Unchanged, int Comments, int HistoryLines, int PeopleAdded, int ValuesAdded, int AttributesAdded, string BackupName);

    public sealed record SpiceworksFieldChange(int Number, string Field, string Here, string InSpiceworks, bool TakeSpiceworks)
    {
        public string Key => SpiceworksConflictKey(Number, Field);
    }

    // A ticket already brought across that has something new in the export. EditedHere means it was changed here since
    // the last import, so its field changes wait for a decision rather than being made.
    public sealed record SpiceworksUpdate(int Number, int SpiceworksNumber, string Title, bool EditedHere, IReadOnlyList<SpiceworksFieldChange> Changes, int NewComments, int NewHistory);

    public sealed record SpiceworksSync(int NewTickets, IReadOnlyList<SpiceworksUpdate> Updates, int Unchanged, int DeletedHere)
    {
        public IReadOnlyList<SpiceworksUpdate> Conflicts => Updates.Where(x => x.EditedHere && x.Changes.Count > 0).ToList();
        public int NewComments => Updates.Sum(x => x.NewComments);
        public int NewHistory => Updates.Sum(x => x.NewHistory);
    }

    public static string SpiceworksConflictKey(int number, string field) => $"conflict|{number}|{field}";
    public const string SpiceworksTakeTheirs = "spiceworks";

    public IReadOnlyList<SpiceworksImportRecord> SpiceworksImports { get { lock (_sync) return _data.SpiceworksImports.OrderByDescending(x => x.At).ToList(); } }

    // ---- Working out what Spiceworks' values become, shared by the comparison and the import ----

    private sealed class SpiceworksResolver
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<long, SpiceworksExport.Person> EndUsers;
        public readonly Dictionary<long, (SpiceworksExport.Person Person, TechnicianRecord? Account)> Technicians;
        public readonly Dictionary<long, string> Categories;

        public SpiceworksResolver(HelpdeskStore store, SpiceworksExport export, SpiceworksPlan plan)
        {
            foreach (var choice in plan.Values)
                _values[choice.Key] = choice.Chosen == SpiceworksNewValue ? (choice.Kind == "Priority" ? SpiceworksPriorityName(choice.Value) : choice.Value) : choice.Chosen;
            EndUsers = export.EndUsers.Where(x => !string.IsNullOrWhiteSpace(x.Email)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            Technicians = export.Technicians.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => (g.First(), store.MatchTechnician(g.First())));
            Categories = export.Categories.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name.Trim());
        }

        public string Value(string kind, string value) => _values.GetValueOrDefault(ChoiceKey(kind, value)) ?? value;
        public string Status(SpiceworksExport.Ticket t) => Value("Status", t.Status.Trim());
        public string Priority(SpiceworksExport.Ticket t) => Value("Priority", t.Priority.Trim());
        public string Category(SpiceworksExport.Ticket t) =>
            Value("Category", t.CategoryId is { } id && Categories.TryGetValue(id, out var name) && name.Length > 0 ? name : SpiceworksNoCategory);
        public TechnicianRecord? Assignee(SpiceworksExport.Ticket t) => t.AssigneeId is { } id && Technicians.TryGetValue(id, out var found) ? found.Account : null;
        // The requester's email - the placeholder's for a ticket with nobody who can be brought across.
        public string RequesterEmail(SpiceworksExport.Ticket t) => t.EndUserId is { } id && EndUsers.TryGetValue(id, out var person) ? person.Email.Trim() : SpiceworksPlaceholderEmail;
        public string Title(SpiceworksExport.Ticket t) => t.Summary.Length > 0 ? t.Summary : $"Spiceworks #{t.Number}";

        // What Spiceworks says, in the same terms as State.
        public string[] State(SpiceworksExport.Ticket t) =>
            [Title(t), t.Description, Status(t).ToLowerInvariant(), Priority(t).ToLowerInvariant(), Category(t).ToLowerInvariant(),
             Assignee(t)?.Id.ToString() ?? "", RequesterEmail(t).ToLowerInvariant(), t.DueAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? ""];
    }

    // A ticket in the terms the comparison uses. Called under the lock.
    private string[] SpiceworksState(TicketRecord ticket) =>
        [ticket.Title, ticket.Description, ticket.Status.ToLowerInvariant(), ticket.Priority.ToLowerInvariant(), ticket.Category.ToLowerInvariant(),
         ticket.TechnicianId?.ToString() ?? "", (_data.Users.FirstOrDefault(x => x.Id == ticket.RequesterId)?.Email ?? "").Trim().ToLowerInvariant(),
         ticket.DueDate?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? ""];

    private static SpiceworksTicketFields FieldsOf(TicketRecord t) =>
        new(t.Title, t.Description, t.Status, t.Priority, t.Category, t.TechnicianId, t.RequesterId, t.DueDate, t.DueDateOverridden, t.ClosedAt);

    // How a field reads on the page.
    private string ShowField(string field, TicketRecord? ticket, SpiceworksExport.Ticket? source, SpiceworksResolver resolver)
    {
        string Short(string text) => text.Length <= 90 ? text : text[..87] + "…";
        string Due(DateTime? due) => due is { } d ? d.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) : "None";
        if (ticket is not null)
            return field switch
            {
                "Title" => ticket.Title, "Description" => Short(ticket.Description), "Status" => ticket.Status, "Priority" => ticket.Priority, "Category" => ticket.Category,
                "Technician" => _data.Technicians.FirstOrDefault(x => x.Id == ticket.TechnicianId)?.Name ?? "Unassigned",
                "Requester" => _data.Users.FirstOrDefault(x => x.Id == ticket.RequesterId)?.Name ?? "Unknown",
                _ => Due(ticket.DueDate)
            };
        var t = source!;
        return field switch
        {
            "Title" => resolver.Title(t), "Description" => Short(t.Description), "Status" => resolver.Status(t), "Priority" => resolver.Priority(t), "Category" => resolver.Category(t),
            "Technician" => resolver.Assignee(t)?.Name ?? (t.AssigneeId is { } id && resolver.Technicians.TryGetValue(id, out var found) ? $"Unassigned ({found.Person.Name} has no account here)" : "Unassigned"),
            "Requester" => t.EndUserId is { } user && resolver.EndUsers.TryGetValue(user, out var person) ? person.Name : SpiceworksPlaceholderName,
            _ => Due(t.DueAt?.UtcDateTime)
        };
    }

    // ---- What a new export would change ----

    public SpiceworksSync CompareSpiceworks(SpiceworksExport export, IReadOnlyDictionary<string, string> choices)
    {
        lock (_sync)
        {
            var resolver = new SpiceworksResolver(this, export, PlanSpiceworksImport(export, choices));
            var links = _data.SpiceworksLinks.ToDictionary(x => (x.Kind, x.SpiceworksId));
            var tickets = _data.Tickets.ToDictionary(x => x.Number);
            var imports = _data.SpiceworksImports.ToDictionary(x => x.Id, x => x.At);
            var comments = export.Comments.Where(x => x.Body.Length > 0).ToLookup(x => x.TicketId);
            var changes = export.Changes.ToLookup(x => x.TicketId);
            var labors = export.Labors.ToLookup(x => x.TicketId);
            int fresh = 0, unchanged = 0, deleted = 0;
            var updates = new List<SpiceworksUpdate>();
            foreach (var source in export.Tickets.OrderBy(x => x.Number))
            {
                if (!links.TryGetValue(("Ticket", source.Id), out var link)) { fresh++; continue; }
                if (!tickets.TryGetValue(int.Parse(link.EntityKey, CultureInfo.InvariantCulture), out var ticket)) { deleted++; continue; }
                var theirs = resolver.State(source);
                var current = SpiceworksState(ticket);
                bool editedHere;
                string[] last;
                if (_data.SpiceworksTicketStates.TryGetValue(ticket.Number, out var state))
                {
                    last = state;
                    editedHere = !current.SequenceEqual(last);
                }
                else
                {
                    // Imported before states were kept, so what Spiceworks said then isn't known. Every change made here
                    // leaves a line in the ticket's history or a comment, so anything newer than the import means it has
                    // been worked on - and then its differences are the user's to decide. Otherwise it is as imported.
                    var importedAt = imports.GetValueOrDefault(link.ImportId);
                    editedHere = ticket.Comments.Any(x => x.CreatedAt > importedAt)
                        || ticket.History.Any(x => x.CreatedAt > importedAt && !x.Action.Contains("Spiceworks", StringComparison.Ordinal));
                    last = current;
                }
                var fieldChanges = new List<SpiceworksFieldChange>();
                for (var i = 0; i < SpiceworksFields.Length; i++)
                {
                    // Spiceworks hasn't changed it, or it already reads the same here.
                    if (theirs[i] == last[i] || theirs[i] == current[i]) continue;
                    var field = SpiceworksFields[i];
                    var take = !editedHere || choices.GetValueOrDefault(SpiceworksConflictKey(ticket.Number, field)) == SpiceworksTakeTheirs;
                    fieldChanges.Add(new SpiceworksFieldChange(ticket.Number, field, ShowField(field, ticket, null, resolver), ShowField(field, null, source, resolver), take));
                }
                var newComments = comments[source.Id].Count(x => !links.ContainsKey(("Comment", x.Id)));
                var newHistory = changes[source.Id].Count(x => !links.ContainsKey(("Change", x.Id))) + labors[source.Id].Count(x => !links.ContainsKey(("Labor", x.Id)));
                if (fieldChanges.Count == 0 && newComments == 0 && newHistory == 0) { unchanged++; continue; }
                updates.Add(new SpiceworksUpdate(ticket.Number, source.Number, ticket.Title, editedHere, fieldChanges, newComments, newHistory));
            }
            return new SpiceworksSync(fresh, updates, unchanged, deleted);
        }
    }

    // ---- Importing ----

    public (bool Ok, string Message, SpiceworksResult? Result) ApplySpiceworksImport(SpiceworksExport export, IReadOnlyDictionary<string, string> choices, string fileName)
    {
        // A backup first, so the whole import can be walked back from Settings → Backups whatever happens.
        var (backedUp, backup) = CreateBackup(manual: false, label: "before-spiceworks");
        if (!backedUp) return (false, $"Nothing was imported: the backup taken first didn't work ({backup}). Fix that in Settings → Backups & data and try again.", null);
        var backupName = LastBackupFileName();

        lock (_sync)
        {
            var plan = PlanSpiceworksImport(export, choices);
            var sync = CompareSpiceworks(export, choices);
            var resolver = new SpiceworksResolver(this, export, plan);
            var now = DateTime.UtcNow;
            var me = CurrentActor();
            var importId = Guid.NewGuid();
            var links = _data.SpiceworksLinks.ToDictionary(x => (x.Kind, x.SpiceworksId));
            var newLinks = new List<SpiceworksLink>();
            var undo = new List<SpiceworksUndoEntry>();
            void Link(string kind, long id, string key, bool created)
            {
                var link = new SpiceworksLink(kind, id, key, importId, created);
                if (links.TryAdd((kind, id), link)) newLinks.Add(link);
            }
            string Key(int number, DateTime at) => $"{number.ToString(CultureInfo.InvariantCulture)}|{at.Ticks.ToString(CultureInfo.InvariantCulture)}";

            // ---- Values: any the preview said to add ----
            var valuesAdded = 0;
            foreach (var choice in plan.Values.Where(x => x.Chosen == SpiceworksNewValue))
            {
                var list = choice.Kind switch { "Status" => _data.Statuses, "Priority" => _data.Priorities, _ => _data.Categories };
                var name = resolver.Value(choice.Kind, choice.Value);
                if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) { list.Add(name); valuesAdded++; }
            }
            string Listed(List<string> list, string value) => list.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) ?? value;

            // ---- Custom fields, and the Spiceworks number, as ticket custom attributes (brought across with a ticket) ----
            var attributesAdded = 0;
            Guid AttributeFor(string label)
            {
                if (_data.TicketAttributeDefinitions.FirstOrDefault(x => string.Equals(x.Name, label, StringComparison.OrdinalIgnoreCase)) is { } existing) return existing.Id;
                var definition = new TicketAttributeDefinition(Guid.NewGuid(), label);
                _data.TicketAttributeDefinitions.Add(definition);
                attributesAdded++;
                return definition.Id;
            }
            var attributeLabels = export.Attributes.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Label);
            var attributeValues = export.AttributeValues.Where(x => attributeLabels.ContainsKey(x.AttributeId)).ToLookup(x => x.TicketId);

            // ---- People ----
            var usersByEmail = _data.Users.Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var peopleAdded = 0;
            UserRecord RequesterFor(SpiceworksExport.Ticket source)
            {
                var email = resolver.RequesterEmail(source);
                if (usersByEmail.TryGetValue(email, out var existing))
                {
                    if (source.EndUserId is { } known && email != SpiceworksPlaceholderEmail) Link("Requester", known, existing.Id.ToString(), created: false);
                    return existing;
                }
                var placeholder = email == SpiceworksPlaceholderEmail;
                // The placeholder isn't a real person, so it isn't active: it doesn't appear in pickers, and nobody can sign in as it.
                var user = placeholder
                    ? new UserRecord(Guid.NewGuid(), SpiceworksPlaceholderName, SpiceworksPlaceholderEmail, "", "", IsActive: false)
                    : WithLeaverDates(new UserRecord(Guid.NewGuid(), resolver.EndUsers[source.EndUserId!.Value].Name, email, "", "", IsActive: true), null);
                _data.Users.Add(user);
                usersByEmail[email] = user;
                Link("Requester", placeholder ? 0 : source.EndUserId!.Value, user.Id.ToString(), created: true);
                peopleAdded++;
                return user;
            }
            Actor ActorFor(long? technicianId, long? endUserId = null)
            {
                if (technicianId is { } tech && resolver.Technicians.TryGetValue(tech, out var found))
                    return found.Account is { } account ? new Actor(account.Id, account.Name) : new Actor(null, found.Person.Name);
                if (endUserId is { } user && resolver.EndUsers.TryGetValue(user, out var person))
                    return new Actor(null, usersByEmail.TryGetValue(person.Email.Trim(), out var existing) ? existing.Name : person.Name);
                return new Actor(null, "Spiceworks");
            }

            // New comments and history lines for a ticket, linked as they're added.
            var commentsByTicket = export.Comments.Where(x => x.Body.Length > 0).ToLookup(x => x.TicketId);
            var changesByTicket = export.Changes.ToLookup(x => x.TicketId);
            var laborsByTicket = export.Labors.ToLookup(x => x.TicketId);
            List<TicketComment> NewComments(SpiceworksExport.Ticket source, int number) =>
                commentsByTicket[source.Id].Where(x => !links.ContainsKey(("Comment", x.Id))).OrderBy(x => x.CreatedAt).Select(comment =>
                {
                    var at = comment.CreatedAt.UtcDateTime;
                    Link("Comment", comment.Id, Key(number, at), created: true);
                    return new TicketComment(comment.Body, at, comment.Private)
                    {
                        By = ActorFor(comment.CreatorId, comment.EndUserId),
                        FromRequester = comment.EndUserId is not null && comment.CreatorId is null
                    };
                }).ToList();
            List<TicketActivity> NewHistory(SpiceworksExport.Ticket source, int number)
            {
                var lines = new List<TicketActivity>();
                foreach (var change in changesByTicket[source.Id].Where(x => !links.ContainsKey(("Change", x.Id))).OrderBy(x => x.CreatedAt))
                {
                    var at = change.CreatedAt.UtcDateTime;
                    lines.Add(new TicketActivity(change.Action == "ticket merged" ? "Merged in Spiceworks" : "Updated in Spiceworks",
                        change.Body.Length > 0 ? change.Body : change.Action, at) { By = ActorFor(change.CreatorId) });
                    Link("Change", change.Id, Key(number, at), created: true);
                }
                foreach (var labor in laborsByTicket[source.Id].Where(x => !links.ContainsKey(("Labor", x.Id))).OrderBy(x => x.CreatedAt))
                {
                    var at = labor.CreatedAt.UtcDateTime;
                    lines.Add(new TicketActivity("Time logged in Spiceworks", labor.Body.Length > 0 ? labor.Body : $"{labor.Minutes} minutes", at) { By = ActorFor(labor.UserId) });
                    Link("Labor", labor.Id, Key(number, at), created: true);
                }
                return lines;
            }
            DateTime? ClosedAt(SpiceworksExport.Ticket source, string status) =>
                string.Equals(status, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase) ? (source.ClosedAt ?? source.UpdatedAt ?? source.CreatedAt).UtcDateTime : null;
            string? TeamOf(TechnicianRecord? technician) =>
                technician is { Team.Length: > 0 } && _data.TechnicianTeams.Contains(technician.Team, StringComparer.OrdinalIgnoreCase) ? technician.Team : null;

            // ---- New tickets ----
            var spiceworksNumber = AttributeFor(SpiceworksNumberAttribute);
            int created = 0, comments = 0, historyLines = 0;
            foreach (var source in export.Tickets.OrderBy(x => x.Number).Where(x => !links.ContainsKey(("Ticket", x.Id))))
            {
                var number = ++_data.LastTicketNumber;
                var status = Listed(_data.Statuses, resolver.Status(source));
                var requester = RequesterFor(source);
                var assignee = resolver.Assignee(source);
                var history = NewHistory(source, number);
                var notes = new List<string> { $"Spiceworks #{source.Number}, from {fileName}." };
                if (requester.Email == SpiceworksPlaceholderEmail)
                    notes.Add(source.CreatorId is { } creator && resolver.Technicians.TryGetValue(creator, out var by)
                        ? $"Logged by {by.Person.Name} in Spiceworks, with no requester."
                        : "Its requester isn't in the export.");
                if (assignee is null && source.AssigneeId is { } assigneeId && resolver.Technicians.TryGetValue(assigneeId, out var had))
                    notes.Add($"Was assigned to {had.Person.Name} in Spiceworks, who has no account here.");
                if (source.MasterNumber is { } master) notes.Add($"Merged into Spiceworks #{master}.");
                history.Add(new TicketActivity("Imported from Spiceworks", string.Join(" ", notes), now) { By = me });
                var ticketComments = NewComments(source, number);
                comments += ticketComments.Count;
                historyLines += history.Count;

                var ticket = new TicketRecord(number, resolver.Title(source), source.Description, requester.Id, [], assignee?.Id,
                    Listed(_data.Priorities, resolver.Priority(source)), status, Listed(_data.Categories, resolver.Category(source)),
                    source.CreatedAt.UtcDateTime, ClosedAt(source, status),
                    SlaId: null, DueDate: source.DueAt?.UtcDateTime, DueDateOverridden: source.DueAt is not null, SlaOverridden: false, TeamName: TeamOf(assignee))
                {
                    Comments = ticketComments,
                    History = history,
                    // Seen, as far as the portal's "New reply" flag is concerned: none of this is news to the requester.
                    RequesterSeenAt = now
                };
                _data.Tickets.Add(ticket);
                _data.TicketAttributeValues.Add(new TicketAttributeValue(number, spiceworksNumber, source.Number.ToString(CultureInfo.InvariantCulture)));
                foreach (var value in attributeValues[source.Id].GroupBy(x => x.AttributeId).Select(g => g.First()))
                    _data.TicketAttributeValues.Add(new TicketAttributeValue(number, AttributeFor(attributeLabels[value.AttributeId]), value.Value));
                Link("Ticket", source.Id, number.ToString(CultureInfo.InvariantCulture), created: true);
                _data.SpiceworksTicketStates[number] = resolver.State(source);
                undo.Add(new SpiceworksUndoEntry(importId, number, null, null, SpiceworksState(ticket), ticket.Comments.Count, ticket.History.Count));
                created++;
            }

            // ---- Tickets brought across before ----
            var bySpiceworksId = export.Tickets.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var ticketLinks = _data.SpiceworksLinks.Where(x => x.Kind == "Ticket").ToDictionary(x => int.Parse(x.EntityKey, CultureInfo.InvariantCulture), x => x.SpiceworksId);
            var updated = 0;
            foreach (var update in sync.Updates)
            {
                var index = _data.Tickets.FindIndex(x => x.Number == update.Number);
                if (index < 0 || !ticketLinks.TryGetValue(update.Number, out var spiceworksId) || !bySpiceworksId.TryGetValue(spiceworksId, out var source)) continue;
                var ticket = _data.Tickets[index];
                var before = FieldsOf(ticket);
                var taken = update.Changes.Where(x => x.TakeSpiceworks).Select(x => x.Field).ToHashSet();
                var kept = update.Changes.Where(x => !x.TakeSpiceworks).ToList();
                if (taken.Contains("Title")) ticket = ticket with { Title = resolver.Title(source) };
                if (taken.Contains("Description")) ticket = ticket with { Description = source.Description };
                if (taken.Contains("Status"))
                {
                    var status = Listed(_data.Statuses, resolver.Status(source));
                    ticket = ticket with { Status = status, ClosedAt = ClosedAt(source, status) };
                }
                if (taken.Contains("Priority")) ticket = ticket with { Priority = Listed(_data.Priorities, resolver.Priority(source)) };
                if (taken.Contains("Category")) ticket = ticket with { Category = Listed(_data.Categories, resolver.Category(source)) };
                if (taken.Contains("Technician")) { var assignee = resolver.Assignee(source); ticket = ticket with { TechnicianId = assignee?.Id, TeamName = TeamOf(assignee) }; }
                if (taken.Contains("Requester")) ticket = ticket with { RequesterId = RequesterFor(source).Id };
                if (taken.Contains("Due date")) ticket = ticket with { DueDate = source.DueAt?.UtcDateTime, DueDateOverridden = source.DueAt is not null };

                var newComments = NewComments(source, ticket.Number);
                var history = ticket.History.ToList();
                history.AddRange(NewHistory(source, ticket.Number));
                var notes = new List<string>();
                if (taken.Count > 0) notes.Add("Changed to match Spiceworks: " + string.Join("; ", update.Changes.Where(x => x.TakeSpiceworks).Select(x => $"{x.Field}: {x.Here} → {x.InSpiceworks}")) + ".");
                if (kept.Count > 0) notes.Add("Kept as it is here, though Spiceworks differs: " + string.Join("; ", kept.Select(x => $"{x.Field} ({x.InSpiceworks} in Spiceworks)")) + ".");
                if (newComments.Count > 0) notes.Add($"{newComments.Count} new comment{(newComments.Count == 1 ? "" : "s")}.");
                history.Add(new TicketActivity("Updated from Spiceworks", string.Join(" ", notes.DefaultIfEmpty("New history from Spiceworks.")), now) { By = me });
                historyLines += history.Count - ticket.History.Count;
                comments += newComments.Count;
                ticket = ticket with { Comments = [.. ticket.Comments, .. newComments], History = history, RequesterSeenAt = ticket.RequesterSeenAt ?? now };
                _data.Tickets[index] = ticket;

                var previous = _data.SpiceworksTicketStates.GetValueOrDefault(ticket.Number);
                _data.SpiceworksTicketStates[ticket.Number] = resolver.State(source);
                undo.Add(new SpiceworksUndoEntry(importId, ticket.Number, before, previous, SpiceworksState(ticket), ticket.Comments.Count, ticket.History.Count));
                updated++;
            }

            // ---- Merges: linked to the ticket they went into, once both are here ----
            var numbers = links.Values.Where(x => x.Kind == "Ticket").ToDictionary(x => x.SpiceworksId, x => int.Parse(x.EntityKey, CultureInfo.InvariantCulture));
            var bySpiceworksNumber = export.Tickets.Where(x => numbers.ContainsKey(x.Id)).GroupBy(x => x.Number).ToDictionary(g => g.Key, g => numbers[g.First().Id]);
            foreach (var source in export.Tickets.Where(x => x.MasterNumber is not null))
                if (numbers.TryGetValue(source.Id, out var from) && bySpiceworksNumber.TryGetValue(source.MasterNumber!.Value, out var into) && from != into
                    && _data.Tickets.Any(x => x.Number == from) && _data.Tickets.Any(x => x.Number == into)
                    && !_data.TicketLinks.Any(x => (x.TicketNumber == from && x.LinkedNumber == into) || (x.TicketNumber == into && x.LinkedNumber == from)))
                    _data.TicketLinks.Add(new TicketLink(from, into, "related"));

            var unchanged = sync.Unchanged + sync.DeletedHere;
            var parts = new List<string> { $"{created:N0} new ticket{(created == 1 ? "" : "s")}" };
            if (updated > 0) parts.Add($"{updated:N0} updated");
            parts.Add($"{comments:N0} comment{(comments == 1 ? "" : "s")}");
            parts.Add($"{historyLines:N0} history line{(historyLines == 1 ? "" : "s")}");
            parts.Add($"{peopleAdded:N0} {(peopleAdded == 1 ? "person" : "people")} added");
            var summary = string.Join(", ", parts) + (unchanged > 0 ? $"; {unchanged:N0} already here and unchanged" : "");
            _data.SpiceworksLinks.AddRange(newLinks);
            _data.SpiceworksUndo.AddRange(undo);
            _data.SpiceworksImports.Add(new SpiceworksImportRecord(importId, now, fileName, summary) { By = me });
            _pendingAudit.Add(new AuditEntry(now, "Tickets", null, null, "Spiceworks import", "Imported from Spiceworks", $"{fileName}: {summary}. Backup taken first: {backupName}."));
            Save();
            return (true, $"Imported from Spiceworks: {summary}.", new SpiceworksResult(created, updated, unchanged, comments, historyLines, peopleAdded, valuesAdded, attributesAdded, backupName));
        }
    }

    private string LastBackupFileName() { lock (_sync) return _data.LastBackupFile; }

    // ---- Undoing the last import ----

    // Whether the most recent import can be undone, and if not, why - null when it can.
    public string? SpiceworksUndoProblem()
    {
        lock (_sync)
        {
            if (_data.SpiceworksImports.OrderByDescending(x => x.At).FirstOrDefault() is not { } last) return "There's no import to undo.";
            var entries = _data.SpiceworksUndo.Where(x => x.ImportId == last.Id).ToList();
            if (entries.Count == 0 && _data.SpiceworksLinks.Any(x => x.ImportId == last.Id && x.Kind == "Ticket"))
                return "It was made before imports could be undone. Restore the backup taken before it instead (Settings → Backups & data).";
            var tickets = _data.Tickets.ToDictionary(x => x.Number);
            var changed = entries.Where(x => !tickets.TryGetValue(x.TicketNumber, out var t)
                    || !SpiceworksState(t).SequenceEqual(x.After) || t.Comments.Count != x.CommentsAfter || t.History.Count != x.HistoryAfter)
                .Select(x => x.TicketNumber).ToList();
            if (changed.Count == 0) return null;
            return $"{changed.Count:N0} of the tickets it brought across or updated {(changed.Count == 1 ? "has" : "have")} been worked on since " +
                $"(#{string.Join(", #", changed.Take(5))}{(changed.Count > 5 ? ", …" : "")}), so undoing it would lose that work. Restore the backup taken before it instead, if you're sure.";
        }
    }

    public (bool Ok, string Message) UndoLastSpiceworksImport()
    {
        lock (_sync)
        {
            if (SpiceworksUndoProblem() is { } problem) return (false, problem);
            var last = _data.SpiceworksImports.OrderByDescending(x => x.At).First();
            var entries = _data.SpiceworksUndo.Where(x => x.ImportId == last.Id).ToList();
            var itsLinks = _data.SpiceworksLinks.Where(x => x.ImportId == last.Id).ToList();
            // The comments and history lines it added, by ticket, by when they were written.
            var added = itsLinks.Where(x => x.Kind is "Comment" or "Change" or "Labor" && x.EntityKey.Contains('|'))
                .Select(x => x.EntityKey.Split('|')).GroupBy(x => int.Parse(x[0], CultureInfo.InvariantCulture))
                .ToDictionary(g => g.Key, g => g.Select(x => long.Parse(x[1], CultureInfo.InvariantCulture)).ToHashSet());
            int removed = 0, restored = 0;
            foreach (var entry in entries)
            {
                var index = _data.Tickets.FindIndex(x => x.Number == entry.TicketNumber);
                if (entry.Before is not { } before)
                {
                    _data.Tickets.RemoveAt(index);
                    _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == entry.TicketNumber);
                    _data.TicketLinks.RemoveAll(x => x.TicketNumber == entry.TicketNumber || x.LinkedNumber == entry.TicketNumber);
                    _data.SpiceworksTicketStates.Remove(entry.TicketNumber);
                    removed++;
                    continue;
                }
                var ticket = _data.Tickets[index];
                var ticks = added.GetValueOrDefault(entry.TicketNumber) ?? [];
                _data.Tickets[index] = ticket with
                {
                    Title = before.Title, Description = before.Description, Status = before.Status, Priority = before.Priority, Category = before.Category,
                    TechnicianId = before.TechnicianId, RequesterId = before.RequesterId, DueDate = before.DueDate, DueDateOverridden = before.DueDateOverridden, ClosedAt = before.ClosedAt,
                    Comments = ticket.Comments.Where(x => !ticks.Contains(x.CreatedAt.Ticks)).ToList(),
                    History = ticket.History.Where(x => !(ticks.Contains(x.CreatedAt.Ticks) && x.Action.EndsWith("in Spiceworks", StringComparison.Ordinal))
                        && !(x.Action == "Updated from Spiceworks" && x.CreatedAt == last.At)).ToList()
                };
                if (entry.PreviousState is { } previous) _data.SpiceworksTicketStates[entry.TicketNumber] = previous;
                else _data.SpiceworksTicketStates.Remove(entry.TicketNumber);
                restored++;
            }

            // People it added, unless something else now points at them.
            var peopleRemoved = 0;
            foreach (var link in itsLinks.Where(x => x.Kind == "Requester" && x.Created))
            {
                if (!Guid.TryParse(link.EntityKey, out var id)) continue;
                var used = _data.Tickets.Any(x => x.RequesterId == id) || _data.Assets.Any(x => x.AssignedUserId == id || x.Assignments.Any(a => a.UserId == id))
                    || _data.Projects.Any(x => x.RequesterId == id) || _data.Onboardings.Any(x => x.StarterId == id || x.LineManagerId == id) || _data.KitLoans.Any(x => x.BorrowerUserId == id);
                if (!used && _data.Users.RemoveAll(x => x.Id == id) > 0) peopleRemoved++;
            }

            _data.SpiceworksLinks.RemoveAll(x => x.ImportId == last.Id);
            _data.SpiceworksUndo.RemoveAll(x => x.ImportId == last.Id);
            _data.SpiceworksImports.Remove(last);
            var summary = $"{removed:N0} ticket{(removed == 1 ? "" : "s")} removed, {restored:N0} put back as {(restored == 1 ? "it was" : "they were")}, {peopleRemoved:N0} {(peopleRemoved == 1 ? "person" : "people")} removed";
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Tickets", null, null, "Spiceworks import", "Import undone", $"The import of {last.FileName} on {last.At.ToLocalTime():d MMM yyyy, HH:mm}: {summary}."));
            Save();
            return (true, $"Import undone: {summary}. Any statuses, priorities, categories or custom fields it added are still in their lists.");
        }
    }

    // ---- Storage ----

    private static void EnsureSpiceworksSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SpiceworksImports (Id TEXT PRIMARY KEY, At TEXT NOT NULL, FileName TEXT NOT NULL, Summary TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL);
            CREATE TABLE IF NOT EXISTS SpiceworksLinks (Kind TEXT NOT NULL, SpiceworksId INTEGER NOT NULL, EntityKey TEXT NOT NULL, ImportId TEXT NOT NULL, Created INTEGER NOT NULL,
                PRIMARY KEY (Kind, SpiceworksId));
            CREATE TABLE IF NOT EXISTS SpiceworksTicketStates (TicketNumber INTEGER PRIMARY KEY, State TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS SpiceworksUndo (ImportId TEXT NOT NULL, TicketNumber INTEGER NOT NULL, Before TEXT NULL, PreviousState TEXT NULL, After TEXT NOT NULL,
                CommentsAfter INTEGER NOT NULL, HistoryAfter INTEGER NOT NULL, PRIMARY KEY (ImportId, TicketNumber));
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
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, State FROM SpiceworksTicketStates;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (JsonSerializer.Deserialize<string[]>(reader.GetString(1)) is { Length: > 0 } state) data.SpiceworksTicketStates[reader.GetInt32(0)] = state;
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ImportId, TicketNumber, Before, PreviousState, After, CommentsAfter, HistoryAfter FROM SpiceworksUndo ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.SpiceworksUndo.Add(new SpiceworksUndoEntry(Guid.Parse(reader.GetString(0)), reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<SpiceworksTicketFields>(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<string[]>(reader.GetString(3)),
                    JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [], reader.GetInt32(5), reader.GetInt32(6)));
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
        foreach (var (number, state) in data.SpiceworksTicketStates.OrderBy(x => x.Key))
            Execute(connection, transaction, "INSERT INTO SpiceworksTicketStates (TicketNumber, State) VALUES ($number,$state);",
                ("$number", number), ("$state", JsonSerializer.Serialize(state)));
        foreach (var entry in data.SpiceworksUndo.DistinctBy(x => (x.ImportId, x.TicketNumber)))
            Execute(connection, transaction, "INSERT INTO SpiceworksUndo (ImportId, TicketNumber, Before, PreviousState, After, CommentsAfter, HistoryAfter) VALUES ($import,$number,$before,$previous,$after,$comments,$history);",
                ("$import", entry.ImportId.ToString()), ("$number", entry.TicketNumber),
                ("$before", entry.Before is null ? null : JsonSerializer.Serialize(entry.Before)), ("$previous", entry.PreviousState is null ? null : JsonSerializer.Serialize(entry.PreviousState)),
                ("$after", JsonSerializer.Serialize(entry.After)), ("$comments", entry.CommentsAfter), ("$history", entry.HistoryAfter));
    }
}
