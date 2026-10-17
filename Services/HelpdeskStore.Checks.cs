using System.Globalization;
using System.Text.Json;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Premises checks (stage 4 of desks): a recurring ticket can carry a checklist - the weekly fire alarm test, the monthly
// water temperatures, one line for every extinguisher - and each ticket it logs gets its own copy to fill in. A reading
// outside its limits is a fail. A check can't be closed until every line is answered, and closing one with fails raises
// one follow-up job listing them. What was recorded is the premises compliance log (Reports → Premises checks).
public sealed partial class HelpdeskStore
{
    public const int MaxCheckLabelLength = 120;
    public const int MaxCheckNoteLength = 500;
    public const string CheckPass = "pass";
    public const string CheckFail = "fail";

    // ---- The checklist on a schedule ----

    public sealed record CheckItemInput(string? Kind, string? Label, string? Min, string? Max, string? Unit, string? AssetType);

    // A new line (itemId null) or a change to one.
    public (bool Ok, string Message) SaveCheckItem(Guid scheduleId, Guid? itemId, CheckItemInput input)
    {
        lock (_sync)
        {
            var index = _data.RecurringTickets.FindIndex(x => x.Id == scheduleId);
            if (index < 0) return (false, "That recurring ticket was not found.");
            var schedule = _data.RecurringTickets[index];
            var kind = CheckKinds.Normalize(input.Kind);
            var label = (input.Label ?? "").Trim();
            var assetType = "";
            if (kind == CheckKinds.Assets)
            {
                assetType = _data.AssetTypes.FirstOrDefault(x => string.Equals(x, input.AssetType?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
                if (assetType.Length == 0) return (false, "Choose the asset type whose assets are each checked.");
                if (label.Length == 0) label = assetType;
            }
            if (label.Length == 0) return (false, "Say what is checked, such as Hot water at the hall kitchen tap.");
            if (label.Length > MaxCheckLabelLength) return (false, $"Keep it to {MaxCheckLabelLength} characters.");
            decimal? min = null, max = null;
            var unit = "";
            if (kind == CheckKinds.Reading)
            {
                if (!TryReading(input.Min, out min) || !TryReading(input.Max, out max)) return (false, "Limits are numbers, such as 50 or 20.5.");
                if (min is null && max is null) return (false, "Give a reading a lowest or highest allowed value, or both.");
                if (min > max) return (false, "The lowest allowed value is above the highest.");
                unit = (input.Unit ?? "").Trim();
                if (unit.Length > 20) return (false, "Keep the unit short, such as °C.");
            }
            var list = schedule.Checklist.ToList();
            var item = new CheckItem(itemId ?? Guid.NewGuid(), kind, label, min, max, unit, assetType);
            var at = itemId is { } id ? list.FindIndex(x => x.Id == id) : -1;
            if (itemId is not null && at < 0) return (false, "That checklist line was not found.");
            if (at >= 0) list[at] = item; else list.Add(item);
            _data.RecurringTickets[index] = schedule with { Checklist = list };
            Save();
            return (true, at >= 0 ? "Checklist line saved." : "Checklist line added. Tickets already logged keep the checklist they were given.");
        }
    }

    public (bool Ok, string Message) DeleteCheckItem(Guid scheduleId, Guid itemId)
    {
        lock (_sync)
        {
            var index = _data.RecurringTickets.FindIndex(x => x.Id == scheduleId);
            if (index < 0) return (false, "That recurring ticket was not found.");
            var list = _data.RecurringTickets[index].Checklist.ToList();
            if (list.RemoveAll(x => x.Id == itemId) == 0) return (false, "That checklist line was not found.");
            _data.RecurringTickets[index] = _data.RecurringTickets[index] with { Checklist = list };
            Save();
            return (true, "Checklist line removed.");
        }
    }

    public string? MoveCheckItem(Guid scheduleId, Guid itemId, int direction)
    {
        lock (_sync)
        {
            var index = _data.RecurringTickets.FindIndex(x => x.Id == scheduleId);
            if (index < 0) return "That recurring ticket was not found.";
            var list = _data.RecurringTickets[index].Checklist.ToList();
            var at = list.FindIndex(x => x.Id == itemId);
            if (at < 0) return "That checklist line was not found.";
            var other = at + (direction < 0 ? -1 : 1);
            if (other < 0 || other >= list.Count) return null;
            (list[at], list[other]) = (list[other], list[at]);
            _data.RecurringTickets[index] = _data.RecurringTickets[index] with { Checklist = list };
            Save();
            return null;
        }
    }

    private static bool TryReading(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return false;
        value = parsed;
        return true;
    }

    // "50 °C or more", "below 20 °C", "between 2 and 8 bar" - how a reading's limits read on the ticket and the log.
    public static string DescribeLimits(decimal? min, decimal? max, string unit)
    {
        string N(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture) + (unit.Length > 0 ? $" {unit}" : "");
        return (min, max) switch
        {
            ({ } low, { } high) => $"{N(low)} to {N(high)}",
            ({ } low, null) => $"{N(low)} or more",
            (null, { } high) => $"{N(high)} or less",
            _ => ""
        };
    }

    // ---- The check on a ticket ----

    // Called with the lock held, as a recurring ticket logs one. Each asset line becomes one line per asset of that type
    // still on the estate, by room then tag, as someone would walk round them.
    private void AddChecklistCore(int number, RecurringTicket schedule)
    {
        // With premises checks switched off (Services/Features.cs) the schedule logs a plain ticket.
        if (_data.FeaturesOff.Contains(Features.Checks)) return;
        var position = 0;
        foreach (var item in schedule.Checklist)
        {
            if (item.Kind == CheckKinds.Assets)
            {
                foreach (var asset in _data.Assets.Where(a => string.Equals(a.Type, item.AssetType, StringComparison.OrdinalIgnoreCase) && !IsDisposed(a))
                    .OrderBy(a => a.Location, NaturalComparer.Instance).ThenBy(a => a.AssetTag, NaturalComparer.Instance))
                    _data.CheckLines.Add(new CheckLine(Guid.NewGuid(), number, position++, CheckKinds.PassFail,
                        $"{item.Label}: {asset.AssetTag}{(string.IsNullOrWhiteSpace(asset.Location) ? "" : $" ({asset.Location})")}", null, null, "", asset.Id, schedule.Id, schedule.Name));
                continue;
            }
            _data.CheckLines.Add(new CheckLine(Guid.NewGuid(), number, position++, item.Kind, item.Label, item.Min, item.Max, item.Unit, null, schedule.Id, schedule.Name));
        }
    }

    public IReadOnlyList<CheckLine> ChecklistFor(int number)
    {
        lock (_sync) return _data.CheckLines.Where(x => x.TicketNumber == number).OrderBy(x => x.Position).ToList();
    }

    public IReadOnlyList<CheckLine> CheckLines { get { lock (_sync) return _data.CheckLines.ToList(); } }

    // How many lines still need an answer; 0 for a ticket with no checklist.
    public int UnansweredChecks(int number) { lock (_sync) return UnansweredChecksCore(number); }
    // None while premises checks are switched off: the checklist is out of sight, so it can't hold a ticket open.
    private int UnansweredChecksCore(int number) => _data.FeaturesOff.Contains(Features.Checks) ? 0 : _data.CheckLines.Count(x => x.TicketNumber == number && !x.Answered);

    public sealed record CheckAnswer(string? Result, string? Reading, string? Note);

    // Saves what was filled in, line by line. A reading's pass or fail comes from its limits, not from what was ticked;
    // a line left blank stays unanswered.
    public (bool Ok, string Message) SaveChecklist(int number, IReadOnlyDictionary<Guid, CheckAnswer> answers)
    {
        lock (_sync)
        {
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
            if (ticketIndex < 0) return (false, "Ticket was not found.");
            if (TicketInsights.IsClosed(_data.Tickets[ticketIndex])) return (false, "This check is closed. Reopen it to change what was recorded.");
            var now = DateTime.UtcNow;
            var actor = CurrentActor();
            var changed = 0;
            for (var i = 0; i < _data.CheckLines.Count; i++)
            {
                var line = _data.CheckLines[i];
                if (line.TicketNumber != number || !answers.TryGetValue(line.Id, out var answer)) continue;
                var note = (answer.Note ?? "").Trim();
                if (note.Length > MaxCheckNoteLength) return (false, $"Keep each note under {MaxCheckNoteLength} characters.");
                string result;
                decimal? reading = null;
                switch (line.Kind)
                {
                    case CheckKinds.Reading:
                        if (!TryReading(answer.Reading, out reading)) return (false, $"\"{answer.Reading}\" for {line.Label} isn't a number.");
                        result = reading is not { } value ? "" : (line.Min is { } low && value < low) || (line.Max is { } high && value > high) ? CheckFail : CheckPass;
                        break;
                    case CheckKinds.Note:
                        result = "";
                        break;
                    default:
                        result = answer.Result is CheckPass or CheckFail ? answer.Result : "";
                        break;
                }
                if (line.Result == result && line.Reading == reading && line.Note == note) continue;
                _data.CheckLines[i] = line with { Result = result, Reading = reading, Note = note, AnsweredAt = now, By = actor };
                changed++;
            }
            if (changed == 0) return (true, "Nothing changed.");
            var lines = _data.CheckLines.Where(x => x.TicketNumber == number).ToList();
            var unanswered = lines.Count(x => !x.Answered);
            var failed = lines.Count(x => x.Failed);
            var history = _data.Tickets[ticketIndex].History.ToList();
            history.Add(new TicketActivity("Checklist updated", $"{lines.Count - unanswered} of {lines.Count} lines done{(failed > 0 ? $", {failed} failed" : "")}.", now) { By = actor });
            _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };
            Save();
            return (true, unanswered > 0 ? $"Saved. {unanswered} line{(unanswered == 1 ? "" : "s")} still to do." : failed > 0 ? $"Saved. All done, {failed} failed - closing the check raises a follow-up job for {(failed == 1 ? "it" : "them")}." : "Saved. All done and passed.");
        }
    }

    // Why a ticket can't be closed yet, or null. Asked by every way of closing one.
    public string? CloseBlockedByChecklist(int number)
    {
        var left = UnansweredChecks(number);
        return left == 0 ? null : $"{left} checklist line{(left == 1 ? " is" : "s are")} still to do. Fill in the checklist before closing - a fail counts as done.";
    }

    // Called with the lock held, when a ticket has just been closed: a check with fails raises one follow-up job listing
    // them, on the same desk, once - closing it again after a reopen doesn't raise another.
    private void FollowUpFailedChecksCore(int number)
    {
        var failed = _data.CheckLines.Where(x => x.TicketNumber == number && x.Failed).OrderBy(x => x.Position).ToList();
        if (failed.Count == 0) return;
        if (_data.TicketLinks.Any(x => x.TicketNumber == number && x.Kind == "follow-up")) return;
        var original = _data.Tickets.FirstOrDefault(x => x.Number == number);
        if (original is null) return;
        var now = DateTime.UtcNow;
        var lines = failed.Select(x => "- " + x.Label + (x.Reading is { } value ? $": {value.ToString("0.##", CultureInfo.InvariantCulture)}{(x.Unit.Length > 0 ? " " + x.Unit : "")} (should be {DescribeLimits(x.Min, x.Max, x.Unit)})" : "") + (x.Note.Length > 0 ? $" - {x.Note}" : ""));
        var description = $"{failed.Count} line{(failed.Count == 1 ? "" : "s")} of {failed[0].ScheduleName} failed on {RefCore(original)}:\n{string.Join("\n", lines)}";
        var sla = SlaFor(original.Priority, original.Category);
        var followUp = AddTicketCore(new TicketRecord(0, $"Failed check: {failed[0].ScheduleName}", description, original.RequesterId, original.AssetIds.ToList(), null,
            original.Priority, StartingStatusCore(DeskForCategoryCore(original.Category).Id), original.Category, now, null, sla, CalculateDueDate(sla, now, original.Category),
            false, false, original.TeamName, original.Location) { Type = TicketTypes.Incident }, $"Raised when {RefCore(original)} was closed with failed checks.");
        _data.TicketLinks.Add(new TicketLink(number, followUp, "follow-up"));
        AddLinkHistory(_data.Tickets.FindIndex(x => x.Number == number), "Follow-up created", $"{RefCore(followUp)} was raised for the failed checks.");
    }

    // ---- Storage ----

    private static void EnsureCheckSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CheckLines (Id TEXT PRIMARY KEY, TicketNumber INTEGER NOT NULL, Position INTEGER NOT NULL, Kind TEXT NOT NULL, Label TEXT NOT NULL,
                MinValue TEXT NULL, MaxValue TEXT NULL, Unit TEXT NOT NULL DEFAULT '', AssetId TEXT NULL, ScheduleId TEXT NOT NULL, ScheduleName TEXT NOT NULL,
                Result TEXT NOT NULL DEFAULT '', Reading TEXT NULL, Note TEXT NOT NULL DEFAULT '', AnsweredAt TEXT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS AssetTypeDesks (AssetType TEXT PRIMARY KEY, DeskId TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        // The checklist a recurring ticket carries, as JSON on its row.
        try
        {
            command.CommandText = "ALTER TABLE RecurringTickets ADD COLUMN Checklist TEXT NOT NULL DEFAULT '';";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { }
    }

    private static decimal? NullableDecimal(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : decimal.Parse(reader.GetString(column), CultureInfo.InvariantCulture);

    private static void ReadChecks(SqliteConnection connection, StoreData data)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, TicketNumber, Position, Kind, Label, MinValue, MaxValue, Unit, AssetId, ScheduleId, ScheduleName, Result, Reading, Note, AnsweredAt, Actor, ActorId FROM CheckLines ORDER BY TicketNumber, Position;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.CheckLines.Add(new CheckLine(Guid.Parse(reader.GetString(0)), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                    NullableDecimal(reader, 5), NullableDecimal(reader, 6), reader.GetString(7), NullableGuid(reader, 8), Guid.Parse(reader.GetString(9)), reader.GetString(10))
                {
                    Result = reader.GetString(11),
                    Reading = NullableDecimal(reader, 12),
                    Note = reader.GetString(13),
                    AnsweredAt = reader.IsDBNull(14) ? null : Date(reader, 14),
                    By = ReadActor(reader, 15, 16)
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AssetType, DeskId FROM AssetTypeDesks;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (Guid.TryParse(reader.GetString(1), out var desk)) data.AssetTypeDesks[reader.GetString(0)] = desk;
        }
        // Each schedule's checklist, stored as JSON alongside it.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Checklist FROM RecurringTickets;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(1) || reader.GetString(1).Length == 0) continue;
                var index = data.RecurringTickets.FindIndex(x => x.Id == Guid.Parse(reader.GetString(0)));
                if (index < 0) continue;
                // An unreadable checklist is dropped rather than stopping the helpdesk starting.
                try { data.RecurringTickets[index] = data.RecurringTickets[index] with { Checklist = JsonSerializer.Deserialize<List<CheckItem>>(reader.GetString(1)) ?? [] }; }
                catch (JsonException) { }
            }
        }
    }

    public static string ChecklistJson(RecurringTicket item) => item.Checklist.Count == 0 ? "" : JsonSerializer.Serialize(item.Checklist);

    // After the Tickets loop, which CheckLines has a foreign key to.
    private static void WriteChecks(SqliteConnection connection, SqliteTransaction transaction, StoreData data, HashSet<int> ticketNumbers)
    {
        foreach (var line in data.CheckLines.Where(x => ticketNumbers.Contains(x.TicketNumber)).OrderBy(x => x.TicketNumber).ThenBy(x => x.Position))
            Execute(connection, transaction, "INSERT INTO CheckLines (Id, TicketNumber, Position, Kind, Label, MinValue, MaxValue, Unit, AssetId, ScheduleId, ScheduleName, Result, Reading, Note, AnsweredAt, Actor, ActorId) VALUES ($id,$ticket,$position,$kind,$label,$min,$max,$unit,$asset,$schedule,$name,$result,$reading,$note,$answered,$actor,$actorid);",
                ("$id", line.Id.ToString()), ("$ticket", line.TicketNumber), ("$position", line.Position), ("$kind", line.Kind), ("$label", line.Label),
                ("$min", line.Min?.ToString(CultureInfo.InvariantCulture)), ("$max", line.Max?.ToString(CultureInfo.InvariantCulture)), ("$unit", line.Unit), ("$asset", line.AssetId?.ToString()),
                ("$schedule", line.ScheduleId.ToString()), ("$name", line.ScheduleName), ("$result", line.Result), ("$reading", line.Reading?.ToString(CultureInfo.InvariantCulture)),
                ("$note", line.Note), ("$answered", line.AnsweredAt is { } answered ? Iso(answered) : null), ("$actor", line.By?.Name), ("$actorid", line.By?.Id?.ToString()));
        var known = data.Desks.Select(x => x.Id).ToHashSet();
        foreach (var (type, desk) in data.AssetTypeDesks.Where(x => known.Contains(x.Value) && data.AssetTypes.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, "INSERT INTO AssetTypeDesks (AssetType, DeskId) VALUES ($type,$desk);", ("$type", type), ("$desk", desk.ToString()));
    }
}
