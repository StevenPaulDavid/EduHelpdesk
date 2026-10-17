using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Recurring tickets (Settings → Recurring tickets): tickets logged automatically on a schedule. RecurringTicketScheduler
// calls RunRecurringTickets every minute; a date is skipped while the ticket the schedule last logged is still open, so a
// check nobody has got to yet doesn't pile up copies of itself.
public sealed partial class HelpdeskStore
{
    public sealed record RecurringTicketInput(
        string? Name, bool IsActive, string? Title, string? Description, string? Type, string? Category, string? Priority,
        Guid RequesterId, Guid? TechnicianId, string? TeamName, string? Location, string? AssetTags,
        string? Frequency, int Interval, DateOnly? StartDate, DateOnly? EndDate, TimeOnly? At, IReadOnlyList<DayOfWeek>? Days,
        int MonthDay, int WeekOfMonth, DayOfWeek WeekDay, int Month, bool MoveToSchoolDay);

    public IReadOnlyList<RecurringTicket> RecurringTickets
    {
        get { lock (_sync) return _data.RecurringTickets.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    public RecurringTicket? GetRecurringTicket(Guid id)
    {
        lock (_sync) return _data.RecurringTickets.FirstOrDefault(x => x.Id == id);
    }

    // When the schedule next logs a ticket, in school time. Null when it is paused or has ended.
    public DateTime? NextRecurringDue(RecurringTicket item)
    {
        lock (_sync) return item.IsActive ? Recurrence.NextAfter(item.Rule, item.CountedFrom, _data.SchoolDays) : null;
    }

    // Adds a schedule, or saves changes to one when an id is given. localNow is school time.
    public (bool Ok, string Message) SaveRecurringTicket(Guid? id, RecurringTicketInput input, DateTime localNow)
    {
        lock (_sync)
        {
            var old = id is { } existing ? _data.RecurringTickets.FirstOrDefault(x => x.Id == existing) : null;
            if (id is not null && old is null) return (false, "That recurring ticket was not found.");
            var (item, error) = BuildRecurringTicket(old?.Id ?? Guid.NewGuid(), input);
            if (item is null) return (false, error!);
            // A new timing, or switching it back on, counts from now: nothing in the past is logged after the event.
            var recount = old is null || !Recurrence.SameTiming(old.Rule, item.Rule) || (item.IsActive && !old.IsActive);
            item = item with
            {
                CountedFrom = recount ? localNow : old!.CountedFrom,
                LastTicketNumber = old?.LastTicketNumber, LastRunAt = old?.LastRunAt, LastResult = old?.LastResult ?? ""
            };
            if (old is null) _data.RecurringTickets.Add(item);
            else _data.RecurringTickets[_data.RecurringTickets.IndexOf(old)] = item;
            Save();
            return (true, old is null ? "Recurring ticket added." : "Recurring ticket saved.");
        }
    }

    public (bool Ok, string Message) DeleteRecurringTicket(Guid id)
    {
        lock (_sync)
        {
            if (_data.RecurringTickets.RemoveAll(x => x.Id == id) == 0) return (false, "That recurring ticket was not found.");
            Save();
            return (true, "Recurring ticket deleted. Tickets it already logged are not affected.");
        }
    }

    // "Log one now": a ticket straight away, whatever the schedule says and even if the last one is still open. The
    // schedule carries on as before, so the next date sees this ticket as its last one.
    public (bool Ok, string Message, int? Number) LogRecurringTicketNow(Guid id)
    {
        lock (_sync)
        {
            var index = _data.RecurringTickets.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "That recurring ticket was not found.", null);
            var item = _data.RecurringTickets[index];
            var (number, result) = LogRecurringTicket(item, "by hand");
            if (number is null) return (false, result, null);
            _data.RecurringTickets[index] = item with { LastTicketNumber = number, LastRunAt = DateTime.UtcNow, LastResult = result };
            Save();
            return (true, $"Ticket {RefCore(number.Value)} logged.", number);
        }
    }

    // Logs every schedule that has come due by localNow (school time), and returns how many tickets it logged. A date
    // missed while the helpdesk was off is caught up with one ticket on the next run, however many it missed.
    public int RunRecurringTickets(DateTime localNow)
    {
        lock (_sync)
        {
            var logged = 0;
            var changed = false;
            for (var i = 0; i < _data.RecurringTickets.Count; i++)
            {
                var item = _data.RecurringTickets[i];
                if (!item.IsActive || Recurrence.NextAfter(item.Rule, item.CountedFrom, _data.SchoolDays) is not { } due || due > localNow) continue;
                int? number = null;
                string result;
                if (item.LastTicketNumber is { } last && _data.Tickets.FirstOrDefault(x => x.Number == last) is { } previous && !TicketInsights.IsClosed(previous))
                    result = $"Skipped {due:d MMM yyyy}: #{last} from last time is still open.";
                else
                    (number, result) = LogRecurringTicket(item, "on schedule");
                _data.RecurringTickets[i] = item with { CountedFrom = localNow, LastTicketNumber = number ?? item.LastTicketNumber, LastRunAt = DateTime.UtcNow, LastResult = result };
                changed = true;
                if (number is not null) logged++;
            }
            if (changed) Save();
            return logged;
        }
    }

    // Logs the schedule's ticket without saving. Anything renamed or removed since the schedule was saved is left off
    // (a technician, team, room or asset); what a ticket can't be without - its requester, category and priority - stops it.
    private (int? Number, string Result) LogRecurringTicket(RecurringTicket item, string how)
    {
        if (!_data.Users.Any(x => x.Id == item.RequesterId)) return (null, "Not logged: the requester is no longer in People. Choose another one.");
        var category = _data.Categories.FirstOrDefault(x => string.Equals(x, item.Category, StringComparison.OrdinalIgnoreCase));
        if (category is null) return (null, $"Not logged: the category {item.Category} no longer exists. Choose another one.");
        var priority = _data.Priorities.FirstOrDefault(x => string.Equals(x, item.Priority, StringComparison.OrdinalIgnoreCase));
        if (priority is null) return (null, $"Not logged: the priority {item.Priority} no longer exists. Choose another one.");
        var team = _data.TechnicianTeams.FirstOrDefault(x => string.Equals(x, item.TeamName, StringComparison.OrdinalIgnoreCase));
        var technician = _data.Technicians.FirstOrDefault(x => x.Id == item.TechnicianId && x.IsActive && TechnicianInTeam(x, team));
        var location = _data.Locations.FirstOrDefault(x => string.Equals(x, item.Location, StringComparison.OrdinalIgnoreCase));
        var assets = item.AssetIds.Where(id => _data.Assets.Any(a => a.Id == id && !IsDisposed(a))).Distinct().ToList();
        var now = DateTime.UtcNow;
        var sla = SlaFor(priority, category);
        var number = AddTicketCore(new TicketRecord(0, item.Title.Length > 0 ? item.Title : item.Name, item.Description, item.RequesterId, assets,
            technician?.Id, priority, StartingStatusCore(DeskForCategoryCore(category).Id), category, now, null, sla, CalculateDueDate(sla, now, category),
            TeamName: team, Location: location) { Type = item.Type }, $"Logged {how} by the recurring ticket \"{item.Name}\".");
        return (number, $"Logged {RefCore(number)} {how}.");
    }

    private (RecurringTicket? Item, string? Error) BuildRecurringTicket(Guid id, RecurringTicketInput input)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return (null, "Enter a name for the recurring ticket.");
        if (name.Length > 100) return (null, "The name is too long (100 characters at most).");
        if (_data.RecurringTickets.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return (null, "A recurring ticket with that name already exists.");
        var title = input.Title?.Trim() ?? "";
        if (title.Length > 200) return (null, "The ticket title is too long (200 characters at most).");
        var category = _data.Categories.FirstOrDefault(x => string.Equals(x, input.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null) return (null, "Select a valid category.");
        var priority = _data.Priorities.FirstOrDefault(x => string.Equals(x, input.Priority?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (priority is null) return (null, "Select a valid priority.");
        if (!_data.Users.Any(x => x.Id == input.RequesterId)) return (null, "Select a requester.");
        string? team = null;
        if (!string.IsNullOrWhiteSpace(input.TeamName))
        {
            team = _data.TechnicianTeams.FirstOrDefault(x => string.Equals(x, input.TeamName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (team is null) return (null, "Select a valid team.");
        }
        if (input.TechnicianId is { } technicianId)
        {
            if (_data.Technicians.FirstOrDefault(x => x.Id == technicianId) is not { } technician) return (null, "Select a valid technician.");
            if (!TechnicianInTeam(technician, team)) return (null, $"Select a technician from the {team} team.");
        }
        string? location = null;
        if (!string.IsNullOrWhiteSpace(input.Location))
        {
            location = _data.Locations.FirstOrDefault(x => string.Equals(x, input.Location.Trim(), StringComparison.OrdinalIgnoreCase));
            if (location is null) return (null, "Select a valid location.");
        }
        var assetIds = new List<Guid>();
        foreach (var tag in (input.AssetTags ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (_data.Assets.FirstOrDefault(x => string.Equals(x.AssetTag, tag, StringComparison.OrdinalIgnoreCase)) is not { } asset) return (null, $"No asset has the tag {tag}.");
            if (IsDisposed(asset)) return (null, $"{asset.AssetTag} has been disposed of, so it can't go on new tickets.");
            if (!assetIds.Contains(asset.Id)) assetIds.Add(asset.Id);
        }

        var frequency = Recurrence.Normalize(input.Frequency);
        var max = Recurrence.MaxInterval(frequency);
        if (input.Interval < 1 || input.Interval > max) return (null, $"Repeat every 1 to {max} {Recurrence.Frequencies.First(x => x.Value == frequency).Unit}s.");
        if (input.StartDate is not { } start) return (null, "Enter the date it starts.");
        if (input.EndDate is { } endDate && endDate < start) return (null, "The end date is before the start date.");
        if (input.At is not { } at) return (null, "Enter the time of day to log it.");
        var days = (input.Days ?? []).Distinct().OrderBy(x => ((int)x + 6) % 7).ToList();
        if (frequency == Recurrence.Weekly && days.Count == 0) return (null, "Tick at least one day of the week.");
        var weekOfMonth = frequency == Recurrence.Monthly ? input.WeekOfMonth : 0;
        if (weekOfMonth is < 0 or > Recurrence.LastWeek) return (null, "Select which week of the month.");
        if (frequency is Recurrence.Monthly or Recurrence.Yearly && weekOfMonth == 0 && input.MonthDay is < 1 or > 31) return (null, "Enter a day of the month from 1 to 31.");
        if (frequency == Recurrence.Yearly && input.Month is < 1 or > 12) return (null, "Select a month.");
        if (frequency == Recurrence.Yearly && input.MonthDay > DateTime.DaysInMonth(2024, input.Month)) return (null, "That month doesn't have that many days.");
        var rule = new RecurrenceRule(frequency, input.Interval, start, input.EndDate, new TimeOnly(at.Hour, at.Minute),
            frequency == Recurrence.Weekly ? days : [],
            frequency is Recurrence.Monthly or Recurrence.Yearly && weekOfMonth == 0 ? input.MonthDay : 1,
            weekOfMonth, Enum.IsDefined(input.WeekDay) ? input.WeekDay : DayOfWeek.Monday,
            frequency == Recurrence.Yearly ? input.Month : 1, input.MoveToSchoolDay);

        return (new RecurringTicket(id, name, input.IsActive, rule, title, input.Description?.Trim() ?? "", TicketTypes.Normalize(input.Type),
            category, priority, input.RequesterId, input.TechnicianId, team, location, assetIds), null);
    }

    // A category, priority, team or room renamed in Settings: the schedules follow it, as tickets and templates do.
    private void RenameInRecurringTickets(string kind, string oldValue, string newValue)
    {
        bool Was(string? value) => string.Equals(value, oldValue, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < _data.RecurringTickets.Count; i++)
        {
            var item = _data.RecurringTickets[i];
            _data.RecurringTickets[i] = kind switch
            {
                "Category" when Was(item.Category) => item with { Category = newValue },
                "Priority" when Was(item.Priority) => item with { Priority = newValue },
                "Team" when Was(item.TeamName) => item with { TeamName = newValue },
                "Location" when Was(item.Location) => item with { Location = newValue },
                _ => item
            };
        }
    }

    // ---- Storage ----

    private static void EnsureRecurringTicketSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // No foreign keys: the requester, technician and assets are checked when a ticket is logged, so one deleted in
        // the meantime can't break a save.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RecurringTickets (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, IsActive INTEGER NOT NULL,
                Frequency TEXT NOT NULL, Interval INTEGER NOT NULL, StartDate TEXT NOT NULL, EndDate TEXT NULL, At TEXT NOT NULL,
                Days TEXT NOT NULL, MonthDay INTEGER NOT NULL, WeekOfMonth INTEGER NOT NULL, WeekDay INTEGER NOT NULL, Month INTEGER NOT NULL,
                MoveToSchoolDay INTEGER NOT NULL, Title TEXT NOT NULL, Description TEXT NOT NULL, TicketType TEXT NOT NULL,
                Category TEXT NOT NULL, Priority TEXT NOT NULL, RequesterId TEXT NOT NULL, TechnicianId TEXT NULL, TeamName TEXT NULL,
                Location TEXT NULL, AssetIds TEXT NOT NULL, CountedFrom TEXT NOT NULL, LastTicketNumber INTEGER NULL, LastRunAt TEXT NULL,
                LastResult TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    // CountedFrom is school (local) time, written without an offset; everything else follows the other tables.
    private const string LocalTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

    private static void ReadRecurringTickets(SqliteConnection connection, StoreData data)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IsActive, Frequency, Interval, StartDate, EndDate, At, Days, MonthDay, WeekOfMonth, WeekDay, Month, MoveToSchoolDay,
                Title, Description, TicketType, Category, Priority, RequesterId, TechnicianId, TeamName, Location, AssetIds, CountedFrom, LastTicketNumber, LastRunAt, LastResult
            FROM RecurringTickets ORDER BY rowid;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var days = reader.GetString(8).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var d) && d is >= 0 and <= 6 ? (DayOfWeek?)d : null).OfType<DayOfWeek>().ToList();
            var rule = new RecurrenceRule(Recurrence.Normalize(reader.GetString(3)), reader.GetInt32(4),
                NullableDateOnly(reader, 5) ?? DateOnly.FromDateTime(DateTime.Today), NullableDateOnly(reader, 6),
                TimeOnly.TryParseExact(reader.GetString(7), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : new TimeOnly(7, 0),
                days, reader.GetInt32(9), reader.GetInt32(10), (DayOfWeek)Math.Clamp(reader.GetInt32(11), 0, 6), reader.GetInt32(12), reader.GetInt32(13) != 0);
            var assetIds = reader.GetString(23).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null).OfType<Guid>().ToList();
            data.RecurringTickets.Add(new RecurringTicket(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2) != 0, rule,
                reader.GetString(14), reader.GetString(15), TicketTypes.Normalize(reader.GetString(16)), reader.GetString(17), reader.GetString(18),
                Guid.Parse(reader.GetString(19)), NullableGuid(reader, 20), NullableString(reader, 21), NullableString(reader, 22), assetIds)
            {
                CountedFrom = DateTime.TryParseExact(reader.GetString(24), LocalTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var counted) ? counted : DateTime.Now,
                LastTicketNumber = reader.IsDBNull(25) ? null : reader.GetInt32(25),
                LastRunAt = reader.IsDBNull(26) ? null : Date(reader, 26),
                LastResult = reader.GetString(27)
            });
        }
    }

    private static void WriteRecurringTickets(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        foreach (var x in data.RecurringTickets)
            // One line, as the save's row capture reads the statement (HelpdeskStore.SaveChanges).
            Execute(connection, transaction, "INSERT INTO RecurringTickets (Id, Name, IsActive, Frequency, Interval, StartDate, EndDate, At, Days, MonthDay, WeekOfMonth, WeekDay, Month, MoveToSchoolDay, Title, Description, TicketType, Category, Priority, RequesterId, TechnicianId, TeamName, Location, AssetIds, CountedFrom, LastTicketNumber, LastRunAt, LastResult) VALUES ($id,$name,$active,$frequency,$interval,$start,$end,$at,$days,$monthday,$week,$weekday,$month,$move,$title,$description,$type,$category,$priority,$requester,$technician,$team,$location,$assets,$counted,$last,$ran,$result);",
                ("$id", x.Id.ToString()), ("$name", x.Name), ("$active", x.IsActive ? 1 : 0), ("$frequency", x.Rule.Frequency), ("$interval", x.Rule.Interval),
                ("$start", IsoDay(x.Rule.StartDate)), ("$end", IsoDay(x.Rule.EndDate)), ("$at", x.Rule.At.ToString("HH:mm", CultureInfo.InvariantCulture)),
                ("$days", string.Join(",", x.Rule.Days.Select(d => (int)d))), ("$monthday", x.Rule.MonthDay), ("$week", x.Rule.WeekOfMonth), ("$weekday", (int)x.Rule.WeekDay),
                ("$month", x.Rule.Month), ("$move", x.Rule.MoveToSchoolDay ? 1 : 0), ("$title", x.Title), ("$description", x.Description), ("$type", x.Type),
                ("$category", x.Category), ("$priority", x.Priority), ("$requester", x.RequesterId.ToString()), ("$technician", x.TechnicianId?.ToString()),
                ("$team", x.TeamName), ("$location", x.Location), ("$assets", string.Join(",", x.AssetIds)),
                ("$counted", x.CountedFrom.ToString(LocalTimeFormat, CultureInfo.InvariantCulture)), ("$last", x.LastTicketNumber),
                ("$ran", x.LastRunAt is { } ran ? Iso(ran) : null), ("$result", x.LastResult));
    }
}
