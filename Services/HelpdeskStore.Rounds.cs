using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Cleaning rounds (stage 5 of desks): rooms grouped into rounds, each due on its own weekdays, with the supervisor ticking
// off on screen which rooms were cleaned and by whom - cleaners don't sign in, and work from a printed sheet - plus an
// optional spot-check score. Days nobody recorded anything (a holiday, the supervisor off) are kept apart from rooms
// missed on days that were recorded, since the helpdesk doesn't know term dates.
public sealed partial class HelpdeskStore
{
    public const int MaxRoundNameLength = 80;
    public const int MaxRoundNoteLength = 300;

    public IReadOnlyList<CleaningRound> CleaningRounds { get { lock (_sync) return _data.CleaningRounds.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(); } }
    public IReadOnlyList<string> Cleaners { get { lock (_sync) return _data.Cleaners.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(); } }
    public CleaningRound? GetCleaningRound(Guid id) { lock (_sync) return _data.CleaningRounds.FirstOrDefault(x => x.Id == id); }

    // The active rounds due on a day, by name.
    public IReadOnlyList<CleaningRound> RoundsDueOn(DateOnly date)
    {
        lock (_sync) return _data.CleaningRounds.Where(x => x.IsActive && x.Days.Contains(date.DayOfWeek) && date >= x.StartedOn).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<RoundVisit> VisitsFor(Guid roundId, DateOnly date)
    {
        lock (_sync) return _data.RoundVisits.Where(x => x.RoundId == roundId && x.Date == date).ToList();
    }

    public IReadOnlyList<RoundVisit> RoundVisits { get { lock (_sync) return _data.RoundVisits.ToList(); } }

    // ---- Setting rounds up (Settings → Cleaning rounds) ----

    // startsOn: the first day it counts (missed rooms, the day's list); a new round starts today unless given one.
    public (bool Ok, string Message) SaveCleaningRound(Guid? id, string? name, IEnumerable<string>? rooms, IEnumerable<DayOfWeek>? days, string? cleaner, bool active, DateOnly? startsOn = null)
    {
        lock (_sync)
        {
            var existing = id is { } given ? _data.CleaningRounds.FindIndex(x => x.Id == given) : -1;
            if (id is not null && existing < 0) return (false, "That round was not found.");
            var cleanName = (name ?? "").Trim();
            if (cleanName.Length == 0) return (false, "Give the round a name, such as Ground floor classrooms.");
            if (cleanName.Length > MaxRoundNameLength) return (false, $"Keep the name to {MaxRoundNameLength} characters.");
            if (_data.CleaningRounds.Any(x => x.Id != id && string.Equals(x.Name, cleanName, StringComparison.OrdinalIgnoreCase))) return (false, "There's already a round with that name.");
            var chosenRooms = new List<string>();
            foreach (var room in (rooms ?? []).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var known = _data.Locations.FirstOrDefault(x => string.Equals(x, room, StringComparison.OrdinalIgnoreCase));
                if (known is null) return (false, $"{room} isn't in Settings → Locations.");
                chosenRooms.Add(known);
            }
            if (chosenRooms.Count == 0) return (false, "Tick the rooms the round covers.");
            var chosenDays = (days ?? []).Distinct().OrderBy(x => ((int)x + 6) % 7).ToList();
            if (chosenDays.Count == 0) return (false, "Tick the days the round is done.");
            var usual = "";
            if (!string.IsNullOrWhiteSpace(cleaner))
            {
                usual = _data.Cleaners.FirstOrDefault(x => string.Equals(x, cleaner.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
                if (usual.Length == 0) return (false, "Choose a cleaner from Settings → Cleaners.");
            }
            chosenRooms = chosenRooms.OrderBy(x => x, NaturalComparer.Instance).ToList();
            var round = new CleaningRound(id ?? Guid.NewGuid(), cleanName, chosenRooms, chosenDays, usual, active)
            {
                StartedOn = startsOn ?? (existing >= 0 ? _data.CleaningRounds[existing].StartedOn : DateOnly.FromDateTime(DateTime.Now))
            };
            if (existing >= 0) _data.CleaningRounds[existing] = round; else _data.CleaningRounds.Add(round);
            Save();
            return (true, existing >= 0 ? $"{cleanName} saved." : $"{cleanName} added, counted from {round.StartedOn:ddd d MMM yyyy}.");
        }
    }

    // What was recorded on it goes too.
    public (bool Ok, string Message) DeleteCleaningRound(Guid id)
    {
        lock (_sync)
        {
            var round = _data.CleaningRounds.FirstOrDefault(x => x.Id == id);
            if (round is null) return (false, "That round was not found.");
            _data.CleaningRounds.Remove(round);
            _data.RoundVisits.RemoveAll(x => x.RoundId == id);
            Save();
            return (true, $"{round.Name} deleted, with what was recorded on it.");
        }
    }

    // ---- Recording a day's round ----

    public sealed record VisitInput(string Room, bool Done, string? Cleaner, string? Note, int? Score, string? ScoreNote);

    // Replaces what was recorded for these rooms on this day. A room that is neither done nor has a note or score is left
    // unrecorded, so ticking nothing and saving changes nothing.
    public (bool Ok, string Message) RecordRound(Guid roundId, DateOnly date, IEnumerable<VisitInput> rooms)
    {
        lock (_sync)
        {
            var round = _data.CleaningRounds.FirstOrDefault(x => x.Id == roundId);
            if (round is null) return (false, "That round was not found.");
            if (date > DateOnly.FromDateTime(DateTime.Now)) return (false, "That day hasn't happened yet.");
            if (date < round.StartedOn) return (false, $"{round.Name} is counted from {round.StartedOn:ddd d MMM yyyy}.");
            var now = DateTime.UtcNow;
            var actor = CurrentActor();
            var visits = new List<RoundVisit>();
            foreach (var input in rooms)
            {
                var room = round.Rooms.FirstOrDefault(x => string.Equals(x, input.Room, StringComparison.OrdinalIgnoreCase));
                if (room is null) continue;
                var note = (input.Note ?? "").Trim();
                var scoreNote = (input.ScoreNote ?? "").Trim();
                if (note.Length > MaxRoundNoteLength || scoreNote.Length > MaxRoundNoteLength) return (false, $"Keep notes under {MaxRoundNoteLength} characters.");
                if (input.Score is < 1 or > 5) return (false, "A spot-check score is 1 to 5.");
                if (!input.Done && note.Length == 0 && input.Score is null) continue;
                var cleaner = "";
                if (!string.IsNullOrWhiteSpace(input.Cleaner))
                {
                    cleaner = _data.Cleaners.FirstOrDefault(x => string.Equals(x, input.Cleaner.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
                    if (cleaner.Length == 0) return (false, $"Choose a cleaner from the list for {room}.");
                }
                var previous = _data.RoundVisits.FirstOrDefault(x => x.RoundId == roundId && x.Date == date && string.Equals(x.Room, room, StringComparison.OrdinalIgnoreCase));
                var visit = new RoundVisit(previous?.Id ?? Guid.NewGuid(), roundId, date, room, input.Done, input.Done ? cleaner : "", note) { Score = input.Score, ScoreNote = scoreNote };
                // Unchanged rooms keep who recorded them and when.
                visits.Add(previous is not null && previous with { RecordedAt = default, By = null } == visit ? previous : visit with { RecordedAt = now, By = actor });
            }
            var before = _data.RoundVisits.Where(x => x.RoundId == roundId && x.Date == date).ToList();
            if (before.Count == visits.Count && before.All(visits.Contains)) return (true, "Nothing changed.");
            _data.RoundVisits.RemoveAll(x => x.RoundId == roundId && x.Date == date);
            _data.RoundVisits.AddRange(visits.OrderBy(x => x.Room, NaturalComparer.Instance));
            Save();
            var done = visits.Count(x => x.Done);
            return (true, $"{round.Name}, {date:ddd d MMM}: {done} of {round.Rooms.Count} rooms done.");
        }
    }

    // ---- What was missed ----

    public sealed record RoundDay(CleaningRound Round, DateOnly Date, IReadOnlyList<RoundVisit> Visits)
    {
        public bool Recorded => Visits.Count > 0;
        public IReadOnlyList<string> Missed => Recorded ? Round.Rooms.Where(r => !Visits.Any(v => v.Done && string.Equals(v.Room, r, StringComparison.OrdinalIgnoreCase))).ToList() : [];
    }

    // Every day each round was due between from and to (both included, and never after today or before the round began).
    public IReadOnlyList<RoundDay> RoundDays(DateOnly from, DateOnly to)
    {
        lock (_sync)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (to > today) to = today;
            var visits = _data.RoundVisits.Where(x => x.Date >= from && x.Date <= to).ToLookup(x => (x.RoundId, x.Date));
            var days = new List<RoundDay>();
            foreach (var round in _data.CleaningRounds)
                for (var date = from < round.StartedOn ? round.StartedOn : from; date <= to; date = date.AddDays(1))
                    if (round.Days.Contains(date.DayOfWeek)) days.Add(new RoundDay(round, date, visits[(round.Id, date)].ToList()));
            return days.OrderBy(x => x.Date).ThenBy(x => x.Round.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    // ---- Keeping the lists in step ----

    // A room renamed in Settings → Locations: rounds and what was recorded follow it.
    private void RenameInRounds(string oldValue, string newValue)
    {
        bool Was(string value) => string.Equals(value, oldValue, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < _data.CleaningRounds.Count; i++)
            if (_data.CleaningRounds[i].Rooms.Any(Was))
                _data.CleaningRounds[i] = _data.CleaningRounds[i] with { Rooms = _data.CleaningRounds[i].Rooms.Select(x => Was(x) ? newValue : x).ToList() };
        for (var i = 0; i < _data.RoundVisits.Count; i++)
            if (Was(_data.RoundVisits[i].Room)) _data.RoundVisits[i] = _data.RoundVisits[i] with { Room = newValue };
    }

    private void RenameCleaner(string oldValue, string newValue)
    {
        bool Was(string value) => string.Equals(value, oldValue, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < _data.CleaningRounds.Count; i++)
            if (Was(_data.CleaningRounds[i].Cleaner)) _data.CleaningRounds[i] = _data.CleaningRounds[i] with { Cleaner = newValue };
        for (var i = 0; i < _data.RoundVisits.Count; i++)
            if (Was(_data.RoundVisits[i].Cleaner)) _data.RoundVisits[i] = _data.RoundVisits[i] with { Cleaner = newValue };
    }

    // ---- Storage ----

    private static void EnsureRoundSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Cleaners (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS CleaningRounds (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Rooms TEXT NOT NULL, Days TEXT NOT NULL, Cleaner TEXT NOT NULL DEFAULT '',
                IsActive INTEGER NOT NULL DEFAULT 1, StartedOn TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS RoundVisits (Id TEXT PRIMARY KEY, RoundId TEXT NOT NULL, VisitDate TEXT NOT NULL, Room TEXT NOT NULL, Done INTEGER NOT NULL,
                Cleaner TEXT NOT NULL DEFAULT '', Note TEXT NOT NULL DEFAULT '', Score INTEGER NULL, ScoreNote TEXT NOT NULL DEFAULT '', RecordedAt TEXT NOT NULL,
                Actor TEXT NULL, ActorId TEXT NULL, FOREIGN KEY (RoundId) REFERENCES CleaningRounds(Id) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadRounds(SqliteConnection connection, StoreData data)
    {
        ReadStrings(connection, "Cleaners", data.Cleaners);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Rooms, Days, Cleaner, IsActive, StartedOn FROM CleaningRounds ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.CleaningRounds.Add(new CleaningRound(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                    reader.GetString(2).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(), ParseDays(reader.GetString(3)), reader.GetString(4), reader.GetInt32(5) != 0)
                {
                    StartedOn = DateOnly.TryParseExact(reader.GetString(6), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var started) ? started : DateOnly.FromDateTime(DateTime.Today)
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, RoundId, VisitDate, Room, Done, Cleaner, Note, Score, ScoreNote, RecordedAt, Actor, ActorId FROM RoundVisits ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.RoundVisits.Add(new RoundVisit(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                    DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetString(3), reader.GetInt32(4) != 0, reader.GetString(5), reader.GetString(6))
                {
                    Score = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    ScoreNote = reader.GetString(8),
                    RecordedAt = Date(reader, 9),
                    By = ReadActor(reader, 10, 11)
                });
        }
    }

    private static void WriteRounds(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        InsertStrings(connection, transaction, "Cleaners", data.Cleaners);
        foreach (var round in data.CleaningRounds)
            Execute(connection, transaction, "INSERT INTO CleaningRounds (Id, Name, Rooms, Days, Cleaner, IsActive, StartedOn) VALUES ($id,$name,$rooms,$days,$cleaner,$active,$started);",
                ("$id", round.Id.ToString()), ("$name", round.Name), ("$rooms", string.Join('\n', round.Rooms)), ("$days", string.Join(",", round.Days.Select(x => (int)x))),
                ("$cleaner", round.Cleaner), ("$active", round.IsActive ? 1 : 0), ("$started", round.StartedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var rounds = data.CleaningRounds.Select(x => x.Id).ToHashSet();
        // By day then room, so recording today's round only adds rows at the end of the round's list.
        foreach (var visit in data.RoundVisits.Where(x => rounds.Contains(x.RoundId)).OrderBy(x => x.Date).ThenBy(x => x.Room, NaturalComparer.Instance))
            Execute(connection, transaction, "INSERT INTO RoundVisits (Id, RoundId, VisitDate, Room, Done, Cleaner, Note, Score, ScoreNote, RecordedAt, Actor, ActorId) VALUES ($id,$round,$date,$room,$done,$cleaner,$note,$score,$scorenote,$recorded,$actor,$actorid);",
                ("$id", visit.Id.ToString()), ("$round", visit.RoundId.ToString()), ("$date", visit.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$room", visit.Room),
                ("$done", visit.Done ? 1 : 0), ("$cleaner", visit.Cleaner), ("$note", visit.Note), ("$score", visit.Score), ("$scorenote", visit.ScoreNote),
                ("$recorded", Iso(visit.RecordedAt)), ("$actor", visit.By?.Name), ("$actorid", visit.By?.Id?.ToString()));
    }
}
