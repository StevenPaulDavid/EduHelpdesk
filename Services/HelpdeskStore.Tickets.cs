using System.Net;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Tickets: logging, editing, comments, bulk changes, merging, deleting, the parts used on them and the printed job sheet.
public sealed partial class HelpdeskStore
{
    public bool RequiresCloseMessage(TicketRecord ticket)
    {
        lock (_sync)
        {
            return _data.RequireCloseMessagePriorities.Contains(ticket.Priority, StringComparer.OrdinalIgnoreCase)
                || _data.RequireCloseMessageCategories.Contains(ticket.Category, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<(PartRecord Part, int Quantity)> GetTicketParts(int number)
    {
        lock (_sync)
        {
            return _data.TicketParts
                .Where(x => x.TicketNumber == number)
                .Join(_data.Parts, x => x.PartId, p => p.Id, (x, p) => (p, x.Quantity))
                .OrderBy(x => x.p.Name)
                .ToList();
        }
    }
    public string? SetTicketPartQuantity(int number, Guid partId, int quantity)
    {
        lock (_sync)
        {
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
            if (ticketIndex < 0) return "Ticket was not found.";
            var partIndex = _data.Parts.FindIndex(x => x.Id == partId);
            if (partIndex < 0) return "Part was not found.";

            var part = _data.Parts[partIndex];
            var assignmentIndex = _data.TicketParts.FindIndex(x => x.TicketNumber == number && x.PartId == partId);
            var previousQuantity = assignmentIndex >= 0 ? _data.TicketParts[assignmentIndex].Quantity : 0;
            var delta = quantity - previousQuantity;

            if (delta > 0 && part.QuantityOnHand < delta) return $"Only {part.QuantityOnHand} {part.Name} left in stock.";

            _data.Parts[partIndex] = part with { QuantityOnHand = part.QuantityOnHand - delta };

            var history = _data.Tickets[ticketIndex].History.ToList();
            var from = history.Count;
            if (quantity <= 0)
            {
                if (assignmentIndex >= 0)
                {
                    _data.TicketParts.RemoveAt(assignmentIndex);
                    history.Add(new("Part removed", $"Removed {previousQuantity}x {part.Name} - returned to stock.", DateTime.UtcNow));
                }
            }
            else if (assignmentIndex >= 0)
            {
                _data.TicketParts[assignmentIndex] = _data.TicketParts[assignmentIndex] with { Quantity = quantity };
                history.Add(new("Part quantity changed", $"{part.Name}: {previousQuantity} -> {quantity}", DateTime.UtcNow));
            }
            else
            {
                _data.TicketParts.Add(new(number, partId, quantity));
                history.Add(new("Part assigned", $"Assigned {quantity}x {part.Name}.", DateTime.UtcNow));
            }
            StampActor(history, from);
            _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };

            Save();
            return null;
        }
    }
    public int AddTicket(TicketRecord item)
    {
        lock (_sync)
        {
            var number = ++_data.LastTicketNumber;
            var history = new List<TicketActivity>
            {
                new("Ticket created", "The ticket was created.", DateTime.UtcNow) { By = CurrentActor() }
            };
            // Created straight into a status that stops the clock (unusual, but a school can set it up that way).
            var created = WithSlaClock(item with { Number = number }, DateTime.UtcNow);
            if (created.IsSlaPaused) history.Add(new("SLA paused", $"The SLA clock is stopped while the ticket is {created.Status}.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets.Add(created with { History = history });
            Save();
            return number;
        }
    }
    public string? DeleteTicket(int number)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return "Ticket was not found.";

            foreach (var assignment in _data.TicketParts.Where(x => x.TicketNumber == number).ToList())
            {
                var partIndex = _data.Parts.FindIndex(x => x.Id == assignment.PartId);
                if (partIndex >= 0) _data.Parts[partIndex] = _data.Parts[partIndex] with { QuantityOnHand = _data.Parts[partIndex].QuantityOnHand + assignment.Quantity };
            }
            _data.TicketParts.RemoveAll(x => x.TicketNumber == number);
            _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == number);
            RemoveTicketExtras(number);
            _data.Tickets.RemoveAt(index);
            Save();
            return null;
        }
    }
    public string? MergeTicket(int sourceNumber, int targetNumber)
    {
        lock (_sync)
        {
            var error = MergeTicketCore(sourceNumber, targetNumber);
            if (error is null) Save();
            return error;
        }
    }
    // Merges without saving, so a batch of merges can be saved once.
    private string? MergeTicketCore(int sourceNumber, int targetNumber)
    {
        if (sourceNumber == targetNumber) return "Select two different tickets to merge.";
        var sourceIndex = _data.Tickets.FindIndex(x => x.Number == sourceNumber);
        var targetIndex = _data.Tickets.FindIndex(x => x.Number == targetNumber);
        if (sourceIndex < 0 || targetIndex < 0) return "Ticket was not found.";

        var source = _data.Tickets[sourceIndex];
        var target = _data.Tickets[targetIndex];
        var now = DateTime.UtcNow;

        var mergedComments = target.Comments.ToList();
        // Carried comments keep their original author, not whoever performed the merge.
        mergedComments.AddRange(source.Comments.Select(c => new TicketComment($"(Merged from #{source.Number}) {c.Text}", c.CreatedAt, c.IsInternal) { By = c.By }));

        var mergedAssetIds = target.AssetIds.Concat(source.AssetIds).Distinct().ToList();

        var targetHistory = target.History.ToList();
        targetHistory.Add(new("Ticket merged", $"Merged ticket #{source.Number} - {source.Title} into this ticket.", now) { By = CurrentActor() });

        _data.Tickets[targetIndex] = target with
        {
            AssetIds = mergedAssetIds,
            Comments = mergedComments,
            History = targetHistory
        };

        foreach (var assignment in _data.TicketParts.Where(x => x.TicketNumber == sourceNumber).ToList())
        {
            var existingIndex = _data.TicketParts.FindIndex(x => x.TicketNumber == targetNumber && x.PartId == assignment.PartId);
            if (existingIndex >= 0)
                _data.TicketParts[existingIndex] = _data.TicketParts[existingIndex] with { Quantity = _data.TicketParts[existingIndex].Quantity + assignment.Quantity };
            else
                _data.TicketParts.Add(assignment with { TicketNumber = targetNumber });
        }
        _data.TicketParts.RemoveAll(x => x.TicketNumber == sourceNumber);

        foreach (var value in _data.TicketAttributeValues.Where(x => x.TicketNumber == sourceNumber).ToList())
        {
            if (!_data.TicketAttributeValues.Any(x => x.TicketNumber == targetNumber && x.AttributeDefinitionId == value.AttributeDefinitionId))
                _data.TicketAttributeValues.Add(value with { TicketNumber = targetNumber });
        }
        _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == sourceNumber);
        MoveTicketExtras(sourceNumber, targetNumber);

        var sourceHistory = source.History.ToList();
        sourceHistory.Add(new("Ticket merged", $"Merged into ticket #{target.Number} - {target.Title}.", now) { By = CurrentActor() });
        var closedSource = WithSlaClock(source with { Status = "Closed", ClosedAt = source.ClosedAt ?? now }, now);
        if (SlaClockActivity(source, closedSource) is { } clockLine) sourceHistory.Add(clockLine with { By = CurrentActor() });
        _data.Tickets[sourceIndex] = closedSource with { History = sourceHistory };

        return null;
    }

    // A ticket with its priority or category changed. Unless the SLA or due date was set by hand, they follow the change.
    public TicketRecord WithPriority(TicketRecord ticket, string priority)
    {
        lock (_sync)
        {
            if (ticket.SlaOverridden) return ticket with { Priority = priority };
            var sla = SlaFor(priority, ticket.Category);
            return ticket with { Priority = priority, SlaId = sla, DueDate = ticket.DueDateOverridden ? ticket.DueDate : CalculateDueDate(sla, ticket.CreatedAt, ticket.SlaPauses) };
        }
    }
    public TicketRecord WithCategory(TicketRecord ticket, string category)
    {
        lock (_sync)
        {
            if (ticket.SlaOverridden) return ticket with { Category = category };
            var sla = SlaFor(ticket.Priority, category);
            return ticket with { Category = category, SlaId = sla, DueDate = ticket.DueDateOverridden ? ticket.DueDate : CalculateDueDate(sla, ticket.CreatedAt, ticket.SlaPauses) };
        }
    }

    // One change applied to many tickets. Operation is status, technician, team, priority, category, type, comment, close or merge.
    // Value is the new status, team, priority or category (TicketListQuery.None clears the team); Text is the comment or closing message;
    // TargetNumber is the ticket to merge into.
    public sealed record TicketBulkChange(string Operation, string? Value = null, Guid? TechnicianId = null, string? Text = null, int? TargetNumber = null, bool Internal = false);
    // Skipped tickets could not be changed for a reason worth telling the user; unchanged ones already had the value.
    public sealed record TicketBulkResult(int Updated, int Unchanged, int Skipped, string? SkippedReason, string? Error);

    // Saves once for the whole batch, however many tickets change.
    public TicketBulkResult BulkUpdateTickets(IReadOnlyCollection<int> numbers, TicketBulkChange change)
    {
        lock (_sync)
        {
            static TicketBulkResult Fail(string message) => new(0, 0, 0, null, message);
            var chosen = numbers.ToHashSet();
            string? canonical = null;
            string? text = string.IsNullOrWhiteSpace(change.Text) ? null : change.Text.Trim();
            switch (change.Operation)
            {
                case "status":
                    canonical = _data.Statuses.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid status.");
                    break;
                case "priority":
                    canonical = _data.Priorities.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid priority.");
                    break;
                case "category":
                    canonical = _data.Categories.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid category.");
                    break;
                case "type":
                    canonical = TicketTypes.All.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid type.");
                    break;
                case "team":
                    if (change.Value == TicketListQuery.None) break;
                    canonical = _data.TechnicianTeams.FirstOrDefault(x => string.Equals(x, change.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (canonical is null) return Fail("Select a valid team.");
                    break;
                case "technician":
                    if (change.TechnicianId is { } id && !_data.Technicians.Any(x => x.Id == id)) return Fail("Select a valid technician.");
                    break;
                case "comment":
                    if (text is null) return Fail("Enter a comment to add.");
                    break;
                case "close":
                    break;
                case "merge":
                    if (change.TargetNumber is not { } target || !_data.Tickets.Any(x => x.Number == target)) return Fail("Enter the number of an existing ticket to merge into.");
                    break;
                default:
                    return Fail("Choose what to change.");
            }

            int updated = 0, unchanged = 0, skipped = 0;
            string? skippedReason = null;
            void Skip(string reason) { skipped++; skippedReason ??= reason; }

            foreach (var number in _data.Tickets.Where(x => chosen.Contains(x.Number)).Select(x => x.Number).ToList())
            {
                var index = _data.Tickets.FindIndex(x => x.Number == number);
                var ticket = _data.Tickets[index];
                TicketRecord changed;
                switch (change.Operation)
                {
                    case "status":
                        if (string.Equals(ticket.Status, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = ticket with { Status = canonical!, ClosedAt = string.Equals(canonical, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase) ? ticket.ClosedAt ?? DateTime.UtcNow : null };
                        break;
                    case "priority":
                        if (string.Equals(ticket.Priority, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = WithPriority(ticket, canonical!);
                        break;
                    case "category":
                        if (string.Equals(ticket.Category, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = WithCategory(ticket, canonical!);
                        break;
                    case "type":
                        if (string.Equals(ticket.Type, canonical, StringComparison.Ordinal)) { unchanged++; continue; }
                        changed = ticket with { Type = canonical! };
                        break;
                    case "team":
                    {
                        var team = change.Value == TicketListQuery.None ? null : canonical;
                        if (string.Equals(ticket.TeamName, team, StringComparison.Ordinal)) { unchanged++; continue; }
                        var keep = ticket.TechnicianId is not { } current || _data.Technicians.FirstOrDefault(x => x.Id == current) is not { } holder || TechnicianInTeam(holder, team);
                        changed = ticket with { TeamName = team, TechnicianId = keep ? ticket.TechnicianId : null };
                        break;
                    }
                    case "technician":
                    {
                        if (ticket.TechnicianId == change.TechnicianId) { unchanged++; continue; }
                        if (change.TechnicianId is { } technicianId && _data.Technicians.First(x => x.Id == technicianId) is { } technician && !TechnicianInTeam(technician, ticket.TeamName))
                        { Skip($"{technician.Name} is not in the team a ticket is assigned to."); continue; }
                        changed = ticket with { TechnicianId = change.TechnicianId };
                        break;
                    }
                    case "comment":
                    {
                        var comments = ticket.Comments.ToList();
                        comments.Add(new TicketComment(text!, DateTime.UtcNow, change.Internal) { By = CurrentActor() });
                        _data.Tickets[index] = ticket with { Comments = comments };
                        updated++;
                        continue;
                    }
                    case "close":
                    {
                        if (TicketInsights.IsClosed(ticket)) { unchanged++; continue; }
                        if (text is null && RequiresCloseMessage(ticket)) { Skip("A closing message is required for some of these tickets."); continue; }
                        var comments = ticket.Comments.ToList();
                        if (text is not null) comments.Add(new TicketComment(text, DateTime.UtcNow) { By = CurrentActor() });
                        changed = ticket with { Status = TicketInsights.ClosedStatus, ClosedAt = ticket.ClosedAt ?? DateTime.UtcNow, Comments = comments };
                        break;
                    }
                    default: // merge
                    {
                        if (number == change.TargetNumber) { unchanged++; continue; }
                        if (MergeTicketCore(number, change.TargetNumber!.Value) is { } mergeError) { Skip(mergeError); continue; }
                        updated++;
                        continue;
                    }
                }
                changed = WithSlaClock(changed, DateTime.UtcNow);
                var history = ticket.History.ToList();
                var from = history.Count;
                AddTicketActivities(history, ticket, changed);
                StampActor(history, from);
                _data.Tickets[index] = changed with { History = history };
                updated++;
            }

            if (updated > 0) Save();
            return new TicketBulkResult(updated, unchanged, skipped, skippedReason, null);
        }
    }
    // Limits on what the staff portal accepts, checked by its pages and given to its boxes as maxlength. Generous for a
    // fault report, but they stop one post filling the database (or every technician's screen) with megabytes of text.
    public const int MaxTicketTitleLength = 200;
    public const int MaxTicketTextLength = 5000;

    // A reply from the person who raised the ticket. If it had been closed it is reopened, because otherwise "it is
    // still not working" lands on a closed ticket that nobody is looking at.
    // Deliberately separate from AddTicketComment: a technician adding a note to a ticket they have just closed should
    // not bounce it straight back open, so only this path reopens.
    // Only within the reopen window (Settings → Ticket queues & closing): after that a closed ticket takes no more replies, and the
    // portal offers to report the problem again as a new ticket instead - see RequesterCanReply.
    public (bool Ok, bool Reopened) AddRequesterComment(int number, string text)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return (false, false);
            if (!RequesterCanReplyCore(_data.Tickets[index], DateTime.UtcNow)) return (false, false);

            var actor = CurrentActor();
            var ticket = _data.Tickets[index];
            var comments = ticket.Comments.ToList();
            comments.Add(new TicketComment(text.Trim(), DateTime.UtcNow) { By = actor, FromRequester = true });
            ticket = ticket with { Comments = comments };

            var reopened = false;
            // Nothing to reopen to if every status has been renamed away from "Closed"; the comment is still kept.
            if (TicketInsights.IsClosed(ticket) && _data.Statuses.FirstOrDefault(x => !string.Equals(x, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase)) is { } openStatus)
            {
                var history = ticket.History.ToList();
                history.Add(new TicketActivity("Ticket reopened", $"{actor.Name} replied after the ticket was closed, so it was reopened.", DateTime.UtcNow) { By = actor });
                ticket = ticket with { Status = openStatus, ClosedAt = null, History = history };
                // The old due date belongs to the first time round - left alone it would show as overdue the moment the
                // ticket reopens, which is both wrong and noisy. A manually typed due date is the user's, so it stays.
                if (!ticket.DueDateOverridden && ticket.SlaId is not null)
                    ticket = ticket with { DueDate = CalculateDueDate(ticket.SlaId, DateTime.UtcNow) };
                var reopenedTicket = WithSlaClock(ticket, DateTime.UtcNow);
                if (SlaClockActivity(ticket, reopenedTicket) is { } clockLine) reopenedTicket.History.Add(clockLine with { By = actor });
                ticket = reopenedTicket;
                reopened = true;
            }

            _data.Tickets[index] = ticket;
            Save();
            return (true, reopened);
        }
    }

    public bool AddTicketComment(int number, string text, bool isInternal = false)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return false;
            var comments = _data.Tickets[index].Comments.ToList();
            comments.Add(new TicketComment(text.Trim(), DateTime.UtcNow, isInternal) { By = CurrentActor() });
            _data.Tickets[index] = _data.Tickets[index] with { Comments = comments };
            Save();
            return true;
        }
    }
    public Guid? SlaFor(string priority, string category) =>
        _data.Slas.FirstOrDefault(x => x.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))?.Id
        ?? _data.Slas.FirstOrDefault(x => x.Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase))?.Id;

            public string AddTicketAttributeDefinition(string name, IEnumerable<string>? categories, string fieldType, string? choices)
            {
                lock (_sync)
                {
                    name = (name ?? "").Trim();
                    fieldType = NormalizeAttributeType(fieldType); choices = NormalizeChoices(choices);
                    if (string.IsNullOrWhiteSpace(name) || !IsAttributeType(fieldType)) return "Enter a name and valid field type.";
                    var scope = ResolveScope(categories, _data.Categories);
                    if (scope is null) return "Select valid categories.";
                    if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
                    if (_data.TicketAttributeDefinitions.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.Categories, scope))) return DuplicateAttributeMessage(scope, "categories");
                    _data.TicketAttributeDefinitions.Add(new(Guid.NewGuid(), name, fieldType, choices) { Categories = scope }); Save(); return "Ticket attribute added.";
                }
            }
            public string UpdateTicketAttributeDefinition(Guid id, string name, IEnumerable<string>? categories, string fieldType, string? choices)
            {
                lock (_sync)
                {
                    var index = _data.TicketAttributeDefinitions.FindIndex(x => x.Id == id);
                    if (index < 0) return "Ticket attribute was not found.";
                    name = (name ?? "").Trim(); fieldType = NormalizeAttributeType(fieldType); choices = NormalizeChoices(choices);
                    if (string.IsNullOrWhiteSpace(name) || !IsAttributeType(fieldType)) return "Enter a name and valid field type.";
                    var scope = ResolveScope(categories, _data.Categories);
                    if (scope is null) return "Select valid categories.";
                    if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
                    if (_data.TicketAttributeDefinitions.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.Categories, scope))) return DuplicateAttributeMessage(scope, "categories");
                    _data.TicketAttributeDefinitions[index] = new(id, name, fieldType, choices) { Categories = scope }; Save(); return "Ticket attribute updated.";
                }
            }
            public string SetTicketAttributeCategories(Guid id, IEnumerable<string>? categories)
            {
                lock (_sync)
                {
                    var index = _data.TicketAttributeDefinitions.FindIndex(x => x.Id == id);
                    if (index < 0) return "Ticket attribute was not found.";
                    var scope = ResolveScope(categories, _data.Categories);
                    if (scope is null) return "Select valid categories.";
                    var definition = _data.TicketAttributeDefinitions[index];
                    if (_data.TicketAttributeDefinitions.Any(x => x.Id != id && string.Equals(x.Name, definition.Name, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(x.Categories, scope))) return DuplicateAttributeMessage(scope, "categories");
                    _data.TicketAttributeDefinitions[index] = definition with { Categories = scope }; Save(); return "Ticket attribute updated.";
                }
            }
            public string DeleteTicketAttributeDefinition(Guid id)
            {
                lock (_sync) { if (!_data.TicketAttributeDefinitions.RemoveAll(x => x.Id == id).Equals(1)) return "Ticket attribute was not found."; _data.TicketAttributeValues.RemoveAll(x => x.AttributeDefinitionId == id); Save(); return "Ticket attribute deleted."; }
            }
            public bool UpdateTicketAttributeValues(int number, string category, IDictionary<Guid, string>? values)
            {
                lock (_sync)
                {
                    var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
                    if (ticketIndex < 0) return false;
                    var previousValues = _data.TicketAttributeValues.Where(x => x.TicketNumber == number).ToDictionary(x => x.AttributeDefinitionId, x => x.Value);
                    _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == number);
                    var changes = new List<string>();
                    foreach (var definition in GetTicketAttributes(category))
                    {
                        string? raw = null; values?.TryGetValue(definition.Id, out raw); var value = raw?.Trim() ?? "";
                        if (definition.FieldType == "checkbox") value = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                        if (definition.FieldType == "dropdown" && value.Length > 0 && !GetChoices(definition).Contains(value, StringComparer.Ordinal)) continue;
                        if (value.Length > 0) _data.TicketAttributeValues.Add(new(number, definition.Id, value));

                        var previousValue = previousValues.TryGetValue(definition.Id, out var existing) ? existing : "";
                        if (!string.Equals(previousValue, value, StringComparison.Ordinal))
                            changes.Add($"{definition.Name}: {(previousValue.Length > 0 ? previousValue : "(empty)")} -> {(value.Length > 0 ? value : "(empty)")}");
                    }
                    if (changes.Count > 0)
                    {
                        var history = _data.Tickets[ticketIndex].History.ToList();
                        history.Add(new TicketActivity("Custom attributes changed", string.Join("; ", changes), DateTime.UtcNow) { By = CurrentActor() });
                        _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };
                    }
                    Save(); return true;
                }
            }
    public bool UpdateTicket(TicketRecord item)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == item.Number);
            if (index < 0) return false;

            var previous = _data.Tickets[index];
            item = WithSlaClock(item, DateTime.UtcNow);
            var history = previous.History.ToList();
            var from = history.Count;
            AddTicketActivities(history, previous, item);
            StampActor(history, from);
            _data.Tickets[index] = item with { History = history };

            var previousAssetIds = previous.AssetIds.ToHashSet();
            var updatedAssetIds = item.AssetIds.ToHashSet();
            if (!previousAssetIds.SetEquals(updatedAssetIds))
            {
                var now = DateTime.UtcNow;
                foreach (var addedAssetId in updatedAssetIds.Except(previousAssetIds))
                {
                    var assetIndex = _data.Assets.FindIndex(x => x.Id == addedAssetId);
                    if (assetIndex < 0) continue;
                    var assetHistory = _data.Assets[assetIndex].History.ToList();
                    assetHistory.Add(new("Ticket linked", $"Linked to ticket #{item.Number} - {item.Title}", now) { By = CurrentActor() });
                    _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
                }
                foreach (var removedAssetId in previousAssetIds.Except(updatedAssetIds))
                {
                    var assetIndex = _data.Assets.FindIndex(x => x.Id == removedAssetId);
                    if (assetIndex < 0) continue;
                    var assetHistory = _data.Assets[assetIndex].History.ToList();
                    assetHistory.Add(new("Ticket unlinked", $"Unlinked from ticket #{item.Number} - {item.Title}", now) { By = CurrentActor() });
                    _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
                }
            }

            Save();
            return true;
        }
    }

    public void SavePrintTemplate(Stream source)
    {
        lock (_sync)
        {
            using (var destination = File.Create(_templatePath))
                source.CopyTo(destination);
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Settings", null, null, "Ticket print template", "Uploaded", "The ticket print template was replaced."));
            Save();
        }
    }

    public string RenderPrintTemplate(TicketRecord ticket, UserRecord? requester, TechnicianRecord? technician, IReadOnlyList<AssetRecord> assets)
    {
        if (!File.Exists(_templatePath)) return string.Empty;
        using var document = WordprocessingDocument.Open(_templatePath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        var values = new Dictionary<string, string?>
        {
            ["{{Job.Number}}"] = ticket.Number.ToString(),
            ["{{Job.Title}}"] = ticket.Title,
            ["{{Job.Description}}"] = ticket.Description,
            ["{{Job.Status}}"] = ticket.Status,
            ["{{Job.Priority}}"] = ticket.Priority,
            ["{{Job.Category}}"] = ticket.Category,
            ["{{Job.Created}}"] = ticket.CreatedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm"),
            ["{{Job.Closed}}"] = ticket.ClosedAt?.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? "Not closed",
            // Internal notes never go on a printout.
            ["{{Job.Comments}}"] = ticket.Comments.Count(x => !x.IsInternal) == 0 ? "No comments" : string.Join("\n", ticket.Comments.Where(x => !x.IsInternal).OrderBy(x => x.CreatedAt).Select(x => $"{x.CreatedAt.ToLocalTime():dd MMM yyyy, HH:mm}: {x.Text}")),
            ["{{Requester.Name}}"] = requester?.Name ?? "Unknown",
            ["{{Requester.Email}}"] = requester?.Email ?? "",
            ["{{Requester.Department}}"] = requester?.Department ?? "",
            ["{{Requester.Location}}"] = requester?.Location ?? "",
            ["{{Technician.Name}}"] = technician?.Name ?? "Unassigned",
            ["{{Technician.Email}}"] = technician?.Email ?? "",
            ["{{Technician.Team}}"] = technician?.Team ?? "",
            ["{{Asset.Tag}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.AssetTag)) : "No asset linked",
            ["{{Asset.Type}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Type)) : "",
            ["{{Asset.Model}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Model)) : "",
            ["{{Asset.Serial}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.SerialNumber)) : "",
            ["{{Asset.Location}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Location)) : ""
        };
        // Each paragraph's text with the placeholders filled in, HTML-encoded, and line breaks kept.
        string Fill(string text)
        {
            foreach (var value in values) text = text.Replace(value.Key, value.Value ?? "", StringComparison.OrdinalIgnoreCase);
            return WebUtility.HtmlEncode(text).Replace("\n", "<br />");
        }
        var html = new StringBuilder();
        foreach (var element in body.Elements())
        {
            // A table keeps its rows and cells (and merged cells), where it used to come out as one run-on line.
            if (element is Table table)
            {
                html.Append("<table class=\"template-table\">");
                foreach (var row in table.Elements<TableRow>())
                {
                    html.Append("<tr>");
                    foreach (var cell in row.Elements<TableCell>())
                    {
                        var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                        html.Append(span > 1 ? $"<td colspan=\"{span}\">" : "<td>")
                            .Append(Fill(string.Join("\n", cell.Elements<Paragraph>().Select(x => x.InnerText))))
                            .Append("</td>");
                    }
                    html.Append("</tr>");
                }
                html.Append("</table>");
                continue;
            }
            var encoded = Fill(element.InnerText);
            if (!string.IsNullOrWhiteSpace(encoded)) html.Append($"<p>{encoded}</p>");
        }
        return html.ToString();
    }

    private void AddTicketActivities(List<TicketActivity> history, TicketRecord previous, TicketRecord updated)
    {
        var now = DateTime.UtcNow;
        // The SLA clock stopping or starting again explains a due date moving, so it says so rather than the generic line.
        var slaClockLine = SlaClockActivity(previous, updated);
        if (slaClockLine is not null) history.Add(slaClockLine);
        if (previous.Title != updated.Title) history.Add(new("Title changed", $"{previous.Title} -> {updated.Title}", now));
        if (previous.Description != updated.Description) history.Add(new("Description changed", "The ticket description was updated.", now));
        if (previous.Status != updated.Status) history.Add(new("Status changed", $"{previous.Status} -> {updated.Status}", now));
        if (previous.Priority != updated.Priority) history.Add(new("Priority changed", $"{previous.Priority} -> {updated.Priority}", now));
        if (previous.Category != updated.Category) history.Add(new("Category changed", $"{previous.Category} -> {updated.Category}", now));
        if (previous.Type != updated.Type) history.Add(new("Type changed", $"{previous.Type} -> {updated.Type}", now));
        if (previous.RequesterId != updated.RequesterId) history.Add(new("Requester changed", "The ticket requester was updated.", now));
        if (previous.TechnicianId != updated.TechnicianId) history.Add(new("Technician changed", updated.TechnicianId.HasValue ? "A technician was assigned." : "The technician assignment was removed.", now));
        if (previous.TeamName != updated.TeamName) history.Add(new("Team changed", updated.TeamName is null ? "The team assignment was removed." : $"Assigned to team {updated.TeamName}.", now));
        if (!previous.AssetIds.ToHashSet().SetEquals(updated.AssetIds)) history.Add(new("Assets changed", updated.AssetIds.Count > 0 ? $"Linked assets updated ({updated.AssetIds.Count} linked)." : "All linked assets were removed.", now));
        if (previous.ClosedAt != updated.ClosedAt && previous.Status == updated.Status) history.Add(new("Closure changed", updated.ClosedAt.HasValue ? "The ticket was closed." : "The ticket was reopened.", now));
        if (previous.SlaId != updated.SlaId) history.Add(new("SLA changed", updated.SlaId.HasValue ? "An SLA was assigned." : "The SLA was removed.", now));
        if ((previous.DueDate != updated.DueDate || previous.DueDateOverridden != updated.DueDateOverridden) && slaClockLine?.Action != SlaRestartedAction) history.Add(new("Due date changed", updated.DueDate.HasValue ? (updated.DueDateOverridden ? "The due date was manually overridden." : "The due date was recalculated from the SLA.") : "The due date was removed.", now));
    }
}
