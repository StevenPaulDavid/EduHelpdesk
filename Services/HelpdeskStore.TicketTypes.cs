using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The school's own ticket types (Settings → Ticket types), alongside the three built in (TicketTypes.All). Each belongs
// to one desk, so the Site desk can have "Reactive" and "Planned" without IT seeing them. They are labels: for
// filters, reports and templates. Only the built-in Disposal does anything (HelpdeskStore.Disposal).
public sealed partial class HelpdeskStore
{
    public sealed record CustomTicketType(string Name, Guid DeskId);

    private const int MaxTicketTypeLength = 40;

    // Every type, the built-in ones first, then the school's in their order. For filters, reports and templates.
    public IReadOnlyList<string> AllTicketTypes { get { lock (_sync) return AllTicketTypesCore(); } }
    private List<string> AllTicketTypesCore() => [.. TicketTypes.All, .. _data.TicketTypes.Select(x => x.Name)];

    public IReadOnlyList<CustomTicketType> CustomTicketTypes { get { lock (_sync) return _data.TicketTypes.ToList(); } }

    // The types a ticket on this desk can have: the built-in ones and that desk's own.
    public IReadOnlyList<string> TicketTypesFor(Guid deskId)
    {
        lock (_sync) return [.. TicketTypes.All, .. _data.TicketTypes.Where(x => x.DeskId == deskId).Select(x => x.Name)];
    }

    // The desk a type belongs to, or null for a built-in (or unknown) one.
    public Desk? DeskForTicketType(string? type) { lock (_sync) return DeskForTicketTypeCore(type); }
    private Desk? DeskForTicketTypeCore(string? type) =>
        _data.TicketTypes.FirstOrDefault(x => string.Equals(x.Name, type?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } custom
            ? _data.Desks.FirstOrDefault(x => x.Id == custom.DeskId) : null;

    // The type as it is spelt on the list, or null if there is no such type.
    public string? CanonicalTicketType(string? value) { lock (_sync) return CanonicalTicketTypeCore(value); }
    private string? CanonicalTicketTypeCore(string? value) =>
        AllTicketTypesCore().FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    // Why a ticket on this category can't have this type, or null when it can: a type kept to another desk.
    public string? TicketTypeProblem(string? type, string category)
    {
        lock (_sync)
        {
            if (CanonicalTicketTypeCore(type) is not { } known) return "Select a valid type.";
            return DeskForTicketTypeCore(known) is { } typeDesk && typeDesk.Id != DeskForCategoryCore(category).Id
                ? $"{known} is a {typeDesk.Name} desk type, and {category} is on another desk." : null;
        }
    }

    // ---- Settings → Ticket types ----

    public (bool Ok, string Message) AddTicketType(string? name, Guid? deskId)
    {
        lock (_sync)
        {
            var value = (name ?? "").Trim();
            if (value.Length == 0) return (false, "Enter a name for the type.");
            if (value.Length > MaxTicketTypeLength) return (false, $"Keep it under {MaxTicketTypeLength} characters.");
            if (CanonicalTicketTypeCore(value) is { } existing) return (false, $"There's already a type called {existing}.");
            var desk = _data.Desks.FirstOrDefault(x => x.Id == deskId) ?? (_data.Desks.Count == 1 ? _data.Desks[0] : null);
            if (desk is null) return (false, "Choose which desk it's for.");
            _data.TicketTypes.Add(new CustomTicketType(value, desk.Id));
            Save();
            return (true, $"{value} added{(_data.Desks.Count > 1 ? $" to the {desk.Name} desk" : "")}.");
        }
    }

    // Renames it on every ticket, template and recurring ticket that has it.
    public (bool Ok, string Message) RenameTicketType(string? current, string? name)
    {
        lock (_sync)
        {
            var index = CustomIndex(current);
            if (index < 0) return (false, TicketTypes.All.Contains(current?.Trim() ?? "", StringComparer.OrdinalIgnoreCase) ? "The built-in types can't be renamed." : "Type was not found.");
            var old = _data.TicketTypes[index].Name;
            var value = (name ?? "").Trim();
            if (value.Length == 0) return (false, "Enter a name for the type.");
            if (value.Length > MaxTicketTypeLength) return (false, $"Keep it under {MaxTicketTypeLength} characters.");
            if (value == old) return (true, "Nothing changed.");
            if (CanonicalTicketTypeCore(value) is { } existing && !string.Equals(existing, old, StringComparison.OrdinalIgnoreCase))
                return (false, $"There's already a type called {existing}.");
            _data.TicketTypes[index] = _data.TicketTypes[index] with { Name = value };
            ReplaceTicketTypeCore(old, value);
            Save();
            return (true, $"{old} renamed to {value}.");
        }
    }

    // Moving it to another desk is refused while open tickets on other desks have it - they'd be left with a type
    // their desk doesn't have.
    public (bool Ok, string Message) SetTicketTypeDesk(string? type, Guid deskId)
    {
        lock (_sync)
        {
            var index = CustomIndex(type);
            if (index < 0) return (false, "Type was not found.");
            var desk = _data.Desks.FirstOrDefault(x => x.Id == deskId);
            if (desk is null) return (false, "Choose a desk.");
            var item = _data.TicketTypes[index];
            if (item.DeskId == desk.Id) return (true, "Nothing changed.");
            var elsewhere = _data.Tickets.Count(t => !TicketInsights.IsClosed(t) && string.Equals(t.Type, item.Name, StringComparison.OrdinalIgnoreCase) && DeskForCategoryCore(t.Category).Id != desk.Id);
            if (elsewhere > 0) return (false, $"{Plural(elsewhere, "open ticket", "open tickets")} on other desks {(elsewhere == 1 ? "is" : "are")} {item.Name}. Change {(elsewhere == 1 ? "its" : "their")} type first.");
            _data.TicketTypes[index] = item with { DeskId = desk.Id };
            Save();
            return (true, $"{item.Name} is now a {desk.Name} desk type.");
        }
    }

    // Up or down one place in the list (and so in every drop-down).
    public (bool Ok, string Message) MoveTicketType(string? type, bool up)
    {
        lock (_sync)
        {
            var index = CustomIndex(type);
            if (index < 0) return (false, "Type was not found.");
            var other = up ? index - 1 : index + 1;
            if (other < 0 || other >= _data.TicketTypes.Count) return (true, "Nothing changed.");
            (_data.TicketTypes[index], _data.TicketTypes[other]) = (_data.TicketTypes[other], _data.TicketTypes[index]);
            Save();
            return (true, "Order saved.");
        }
    }

    // How many tickets, templates and recurring tickets have it - the Delete button asks where they go if any do.
    public int TicketTypeUsage(string? type)
    {
        lock (_sync)
        {
            var name = type?.Trim();
            return _data.Tickets.Count(x => string.Equals(x.Type, name, StringComparison.OrdinalIgnoreCase))
                + _data.TicketTemplates.Count(x => string.Equals(x.Type, name, StringComparison.OrdinalIgnoreCase))
                + _data.RecurringTickets.Count(x => string.Equals(x.Type, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Takes it off the list. Anything that has it moves to "into" first - one of the built-in types or another of the
    // school's - which has to be given when anything does.
    public (bool Ok, string Message) DeleteTicketType(string? type, string? into)
    {
        lock (_sync)
        {
            var index = CustomIndex(type);
            if (index < 0) return (false, TicketTypes.All.Contains(type?.Trim() ?? "", StringComparer.OrdinalIgnoreCase) ? "The built-in types can't be removed." : "Type was not found.");
            var name = _data.TicketTypes[index].Name;
            var used = TicketTypeUsage(name);
            string? target = null;
            if (used > 0)
            {
                target = CanonicalTicketTypeCore(into);
                if (target is null || string.Equals(target, name, StringComparison.OrdinalIgnoreCase)) return (false, $"{name} is used {Plural(used, "time", "times")}. Choose the type to move them to.");
                // A Disposal ticket marks its assets; turning a label into one by deleting a type would be a surprise.
                if (target == TicketTypes.Disposal) return (false, "Choose a type other than Disposal to move them to.");
                if (DeskForTicketTypeCore(target) is { } targetDesk && targetDesk.Id != _data.TicketTypes[index].DeskId)
                    return (false, $"{target} is a {targetDesk.Name} desk type. Choose a built-in type or one on the same desk.");
            }
            _data.TicketTypes.RemoveAt(index);
            if (target is not null) ReplaceTicketTypeCore(name, target);
            Save();
            return (true, target is null ? $"{name} removed." : $"{name} removed, and {Plural(used, "use", "uses")} moved to {target}.");
        }
    }

    private int CustomIndex(string? type) => _data.TicketTypes.FindIndex(x => string.Equals(x.Name, type?.Trim(), StringComparison.OrdinalIgnoreCase));

    // Every ticket, template and recurring ticket with one type gets the other. Tickets get a history line.
    private void ReplaceTicketTypeCore(string old, string value)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < _data.Tickets.Count; i++)
        {
            var ticket = _data.Tickets[i];
            if (!string.Equals(ticket.Type, old, StringComparison.OrdinalIgnoreCase)) continue;
            var history = ticket.History.ToList();
            history.Add(new("Type changed", $"{old} -> {value} (the type list was changed in Settings).", now) { By = CurrentActor() });
            _data.Tickets[i] = ticket with { Type = value, History = history };
        }
        for (var i = 0; i < _data.TicketTemplates.Count; i++)
            if (string.Equals(_data.TicketTemplates[i].Type, old, StringComparison.OrdinalIgnoreCase))
                _data.TicketTemplates[i] = _data.TicketTemplates[i] with { Type = value };
        for (var i = 0; i < _data.RecurringTickets.Count; i++)
            if (string.Equals(_data.RecurringTickets[i].Type, old, StringComparison.OrdinalIgnoreCase))
                _data.RecurringTickets[i] = _data.RecurringTickets[i] with { Type = value };
    }

    // ---- Storage ----

    private static void EnsureTicketTypeSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // No foreign key to Desks: a desk switched off keeps its types for when it comes back.
        command.CommandText = "CREATE TABLE IF NOT EXISTS TicketTypes (Name TEXT PRIMARY KEY, DeskId TEXT NOT NULL, Position INTEGER NOT NULL);";
        command.ExecuteNonQuery();
    }

    private static void ReadTicketTypes(SqliteConnection connection, StoreData data)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, DeskId FROM TicketTypes ORDER BY Position, Name;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (Guid.TryParse(reader.GetString(1), out var desk)) data.TicketTypes.Add(new CustomTicketType(reader.GetString(0), desk));
    }

    private static void WriteTicketTypes(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        var position = 0;
        foreach (var item in data.TicketTypes)
            Execute(connection, transaction, "INSERT INTO TicketTypes (Name, DeskId, Position) VALUES ($name,$desk,$position);",
                ("$name", item.Name), ("$desk", item.DeskId.ToString()), ("$position", position++));
    }
}
