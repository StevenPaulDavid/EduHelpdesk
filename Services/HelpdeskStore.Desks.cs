using System.Security.Claims;
using System.Text.RegularExpressions;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Desks: the parts of the school the helpdesk serves - IT, Site, Cleaning - each with its own categories, teams and
// statuses, so the site team never wades through "Projector has no picture" and IT never sees "Blocked toilet".
// A ticket is on the desk its category belongs to. Nothing is stored on the ticket itself: moving a ticket to another
// desk is giving it one of that desk's categories, and moving a category to another desk in Settings takes its tickets
// with it. There is always at least one desk; an upgrading database gets "IT" holding everything it already had.
// (Called desks, not departments, because Department is already the requester's department - Teaching, Science...)
public sealed partial class HelpdeskStore
{
    public const int MaxDeskNameLength = 40;
    public const int MaxDeskPrefixLength = 6;
    public const string DefaultDeskName = "IT";
    private static readonly Regex PrefixPattern = new("^[A-Z0-9]{1,6}$", RegexOptions.Compiled);

    public IReadOnlyList<Desk> Desks { get { lock (_sync) return _data.Desks.ToList(); } }

    // With one desk everything about desks stays out of sight: the portal, the lists and the forms look as they always did.
    public bool HasSeveralDesks { get { lock (_sync) return _data.Desks.Count > 1; } }

    public Desk? FindDesk(Guid id) { lock (_sync) return _data.Desks.FirstOrDefault(x => x.Id == id); }

    // By id or by name, as the ticket list's and the portal's addresses carry it.
    public Desk? FindDesk(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        lock (_sync)
            return Guid.TryParse(key, out var id)
                ? _data.Desks.FirstOrDefault(x => x.Id == id)
                : _data.Desks.FirstOrDefault(x => string.Equals(x.Name, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public Desk DeskForCategory(string? category) { lock (_sync) return DeskForCategoryCore(category); }
    public Desk DeskOf(TicketRecord ticket) => DeskForCategory(ticket.Category);

    // A category nobody has placed (one added by an import, say) sits on the first desk until it is placed.
    private Desk DeskForCategoryCore(string? category) =>
        category is not null && _data.CategoryDesks.TryGetValue(category.Trim(), out var id) && _data.Desks.FirstOrDefault(x => x.Id == id) is { } desk
            ? desk
            : _data.Desks[0];

    // Null for no team.
    public Desk? DeskForTeam(string? team) { lock (_sync) return DeskForTeamCore(team); }
    private Desk? DeskForTeamCore(string? team) =>
        string.IsNullOrWhiteSpace(team) ? null
        : _data.TeamDesks.TryGetValue(team.Trim(), out var id) && _data.Desks.FirstOrDefault(x => x.Id == id) is { } desk ? desk
        : _data.Desks[0];

    // Null for a status every desk uses.
    public Desk? DeskForStatus(string? status) { lock (_sync) return DeskForStatusCore(status); }
    private Desk? DeskForStatusCore(string? status) =>
        status is not null && _data.StatusDesks.TryGetValue(status.Trim(), out var id) ? _data.Desks.FirstOrDefault(x => x.Id == id) : null;

    // A fast lookup for the ticket list, which asks once per ticket: taken once, without the lock each time.
    public Func<string?, Desk> DeskLookup()
    {
        lock (_sync)
        {
            var first = _data.Desks[0];
            var byId = _data.Desks.ToDictionary(x => x.Id);
            var byCategory = _data.CategoryDesks.Where(x => byId.ContainsKey(x.Value)).ToDictionary(x => x.Key, x => byId[x.Value], StringComparer.OrdinalIgnoreCase);
            return category => category is not null && byCategory.TryGetValue(category.Trim(), out var desk) ? desk : first;
        }
    }

    // In the order of the Categories list, which is the order of every category dropdown.
    public IReadOnlyList<string> CategoriesFor(Guid deskId)
    {
        lock (_sync) return _data.Categories.Where(x => DeskForCategoryCore(x).Id == deskId).ToList();
    }

    public IReadOnlyList<string> TeamsFor(Guid deskId)
    {
        lock (_sync) return _data.TechnicianTeams.Where(x => DeskForTeamCore(x)?.Id == deskId).OrderBy(x => x).ToList();
    }

    // Every shared status, and this desk's own, in list order.
    public IReadOnlyList<string> StatusesFor(Guid deskId)
    {
        lock (_sync) return StatusesForCore(deskId);
    }
    private List<string> StatusesForCore(Guid deskId) => _data.Statuses.Where(x => DeskForStatusCore(x) is not { } desk || desk.Id == deskId).ToList();
    public bool StatusOnDesk(string? status, Guid deskId) { lock (_sync) return DeskForStatusCore(status) is not { } desk || desk.Id == deskId; }

    // Where a new ticket in this category starts: the first status its desk uses (Open, unless the list was reordered).
    public string StartingStatusFor(string? category)
    {
        lock (_sync) return StartingStatusCore(DeskForCategoryCore(category).Id);
    }
    private string StartingStatusCore(Guid deskId) =>
        StatusesForCore(deskId).FirstOrDefault(x => !IsBuiltInStatus(x)) ?? _data.Statuses.FirstOrDefault() ?? "Open";

    public static bool TechnicianOnDesk(TechnicianRecord technician, Guid deskId) => technician.DeskIds.Contains(deskId);

    public IReadOnlyList<TechnicianRecord> TechniciansOnDesk(Guid deskId)
    {
        lock (_sync) return _data.Technicians.Where(x => TechnicianOnDesk(x, deskId)).ToList();
    }

    // The desks of the signed-in account, in desk order. Empty for someone who isn't a technician account.
    public IReadOnlyList<Desk> DesksOf(ClaimsPrincipal user)
    {
        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)) return [];
        lock (_sync)
            return _data.Technicians.FirstOrDefault(x => x.Id == id) is { } technician
                ? _data.Desks.Where(x => technician.DeskIds.Contains(x.Id)).ToList()
                : [];
    }

    // The number as people see it: SITE-1043, or #1043 on a desk with no prefix.
    public static string RefFor(Desk? desk, int number) => desk is { Prefix.Length: > 0 } ? $"{desk.Prefix}-{number}" : $"#{number}";
    public string Ref(TicketRecord ticket) => RefFor(DeskOf(ticket), ticket.Number);
    public string Ref(int number)
    {
        lock (_sync) return _data.Tickets.FirstOrDefault(x => x.Number == number) is { } ticket ? RefFor(DeskForCategoryCore(ticket.Category), number) : $"#{number}";
    }

    // The desks staff can report to in the portal: offered there, and with at least one category. With only one, the
    // portal skips the desk step and looks as it did before desks.
    public IReadOnlyList<Desk> PortalDesks()
    {
        lock (_sync)
            return _data.Desks.Where(d => d.ShowInPortal && _data.Categories.Any(c => DeskForCategoryCore(c).Id == d.Id)).ToList();
    }

    // ---- Keeping a ticket consistent with its desk ----

    // After a ticket has moved desk: a team, technician or status that belongs elsewhere is taken off rather than left
    // pointing at the old desk. A closed ticket only changes desk; who worked it stays on the record. Called with the
    // lock held. Returns what was taken off, for the message.
    private (TicketRecord Ticket, List<string> Dropped) FitToDeskCore(TicketRecord ticket)
    {
        var dropped = new List<string>();
        if (TicketInsights.IsClosed(ticket)) return (ticket, dropped);
        var desk = DeskForCategoryCore(ticket.Category);
        if (ticket.TeamName is { } team && DeskForTeamCore(team)?.Id != desk.Id)
        {
            ticket = ticket with { TeamName = null };
            dropped.Add("team");
        }
        if (ticket.TechnicianId is { } technicianId && _data.Technicians.FirstOrDefault(x => x.Id == technicianId) is { } technician && !TechnicianOnDesk(technician, desk.Id))
        {
            ticket = ticket with { TechnicianId = null };
            dropped.Add("technician");
        }
        if (DeskForStatusCore(ticket.Status) is { } statusDesk && statusDesk.Id != desk.Id)
        {
            ticket = ticket with { Status = StartingStatusCore(desk.Id) };
            dropped.Add("status");
        }
        return (ticket, dropped);
    }

    // What the page says after a move: "Moved to the Site desk. The technician was unassigned because they aren't on it."
    public static string DescribeMove(Desk desk, IReadOnlyCollection<string> dropped)
    {
        var text = $"Moved to the {desk.Name} desk.";
        if (dropped.Count == 0) return text;
        var parts = new List<string>();
        if (dropped.Contains("technician")) parts.Add("the technician was unassigned");
        if (dropped.Contains("team")) parts.Add("the team was taken off");
        if (dropped.Contains("status")) parts.Add("the status went back to the start");
        var joined = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        return $"{text} {char.ToUpperInvariant(joined[0])}{joined[1..]}, because {(parts.Count == 1 ? "it belongs" : "they belong")} to another desk.";
    }

    // Changing a ticket's category on its page, which moves it when the category is on another desk. Saves.
    public (bool Ok, string Message) ChangeTicketCategory(int number, string? category)
    {
        lock (_sync)
        {
            var ticket = _data.Tickets.FirstOrDefault(x => x.Number == number);
            if (ticket is null) return (false, "Ticket was not found.");
            var chosen = _data.Categories.FirstOrDefault(x => string.Equals(x, category?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosen is null) return (false, "Select a valid category.");
            var before = DeskForCategoryCore(ticket.Category);
            var (changed, dropped) = WithCategoryCore(ticket, chosen);
            UpdateTicket(changed);
            var after = DeskForCategoryCore(chosen);
            return (true, before.Id == after.Id ? "Job updated." : DescribeMove(after, dropped));
        }
    }

    // ---- Managing desks (Settings → Desks) ----

    public (bool Ok, string Message) AddDesk(string? name, string? prefix, string? helperLine)
    {
        lock (_sync)
        {
            var (cleanName, cleanPrefix, helper, error) = CheckDesk(null, name, prefix, helperLine);
            if (error is not null) return (false, error);
            var desk = new Desk(Guid.NewGuid(), cleanName, cleanPrefix, helper);
            _data.Desks.Add(desk);
            Save();
            return (true, $"{desk.Name} desk added. Give it categories in Settings → Categories, and add people to it on their staff account.");
        }
    }

    public (bool Ok, string Message) UpdateDesk(Guid id, string? name, string? prefix, string? helperLine, bool showInPortal)
    {
        lock (_sync)
        {
            var index = _data.Desks.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "That desk was not found.");
            var (cleanName, cleanPrefix, helper, error) = CheckDesk(id, name, prefix, helperLine);
            if (error is not null) return (false, error);
            _data.Desks[index] = _data.Desks[index] with { Name = cleanName, Prefix = cleanPrefix, HelperLine = helper, ShowInPortal = showInPortal };
            Save();
            return (true, $"{cleanName} desk saved.");
        }
    }

    public string SetDeskLook(Guid id, string? icon, string? color)
    {
        lock (_sync)
        {
            var index = _data.Desks.FindIndex(x => x.Id == id);
            if (index < 0) return "That desk was not found.";
            var chosenIcon = PortalLook.FindIcon(icon);
            if (chosenIcon is null) return "Choose one of the icons.";
            var chosenColor = PortalLook.FindColor(color);
            if (chosenColor is null) return "Choose one of the colours.";
            _data.Desks[index] = _data.Desks[index] with { Icon = chosenIcon.Key, Color = chosenColor.Key };
            Save();
            return $"{_data.Desks[index].Name} tile saved.";
        }
    }

    // The look of a desk's tile: what was chosen, or the built-in one for its name.
    public CategoryStyle StyleForDesk(Desk desk)
    {
        var fallback = PortalLook.DefaultFor(desk.Name);
        return new CategoryStyle(PortalLook.FindIcon(desk.Icon)?.Key ?? fallback.Icon, PortalLook.FindColor(desk.Color)?.Key ?? fallback.Color);
    }

    // One place up (-1) or down. The first desk is also where an unplaced category sits.
    public string? MoveDesk(Guid id, int direction)
    {
        lock (_sync)
        {
            var index = _data.Desks.FindIndex(x => x.Id == id);
            if (index < 0) return "That desk was not found.";
            var other = index + (direction < 0 ? -1 : 1);
            if (other < 0 || other >= _data.Desks.Count) return null;
            (_data.Desks[index], _data.Desks[other]) = (_data.Desks[other], _data.Desks[index]);
            Save();
            return null;
        }
    }

    // Only an empty desk goes: its categories, teams and own statuses have to be given to another desk first, and
    // nobody may be left on no desk at all.
    public (bool Ok, string Message) DeleteDesk(Guid id)
    {
        lock (_sync)
        {
            var desk = _data.Desks.FirstOrDefault(x => x.Id == id);
            if (desk is null) return (false, "That desk was not found.");
            if (_data.Desks.Count == 1) return (false, "There has to be at least one desk.");
            var categories = _data.Categories.Count(x => DeskForCategoryCore(x).Id == id);
            if (categories > 0) return (false, $"The {desk.Name} desk still has {Plural(categories, "category", "categories")}. Move them to another desk in Settings → Categories first.");
            var teams = _data.TechnicianTeams.Count(x => DeskForTeamCore(x)?.Id == id);
            if (teams > 0) return (false, $"The {desk.Name} desk still has {Plural(teams, "team", "teams")}. Move them to another desk in Settings → Teams first.");
            var statuses = _data.Statuses.Count(x => DeskForStatusCore(x)?.Id == id);
            if (statuses > 0) return (false, $"The {desk.Name} desk still has {Plural(statuses, "status", "statuses")} of its own. Share them or move them in Settings → Statuses first.");
            if (_data.Technicians.FirstOrDefault(x => x.DeskIds.Count == 1 && x.DeskIds[0] == id) is { } only)
                return (false, $"{only.Name} is only on the {desk.Name} desk. Put them on another desk first.");
            _data.Desks.Remove(desk);
            for (var i = 0; i < _data.Technicians.Count; i++)
                if (_data.Technicians[i].DeskIds.Contains(id))
                    _data.Technicians[i] = _data.Technicians[i] with { DeskIds = _data.Technicians[i].DeskIds.Where(x => x != id).ToList() };
            Save();
            return (true, $"{desk.Name} desk deleted.");
        }
    }

    private static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private (string Name, string Prefix, string Helper, string? Error) CheckDesk(Guid? id, string? name, string? prefix, string? helperLine)
    {
        var cleanName = (name ?? "").Trim();
        var cleanPrefix = (prefix ?? "").Trim().ToUpperInvariant();
        if (cleanName.Length == 0) return ("", "", "", "Give the desk a name.");
        if (cleanName.Length > MaxDeskNameLength) return ("", "", "", $"Keep the name to {MaxDeskNameLength} characters.");
        if (_data.Desks.Any(x => x.Id != id && string.Equals(x.Name, cleanName, StringComparison.OrdinalIgnoreCase))) return ("", "", "", "There's already a desk with that name.");
        if (cleanPrefix.Length > 0 && !PrefixPattern.IsMatch(cleanPrefix)) return ("", "", "", $"A prefix is up to {MaxDeskPrefixLength} letters or numbers, such as SITE - or leave it blank.");
        if (cleanPrefix.Length > 0 && _data.Desks.Any(x => x.Id != id && string.Equals(x.Prefix, cleanPrefix, StringComparison.OrdinalIgnoreCase))) return ("", "", "", "Another desk already uses that prefix.");
        if (CheckHelperLine(helperLine, out var helper) is { } helperError) return ("", "", "", helperError);
        return (cleanName, cleanPrefix, helper, null);
    }

    // ---- Placing categories, teams and statuses ----

    // Its open tickets go with it, losing a team, technician or status that belongs to the old desk.
    public (bool Ok, string Message) SetCategoryDesk(string? category, Guid deskId)
    {
        lock (_sync)
        {
            var name = _data.Categories.FirstOrDefault(x => string.Equals(x, category?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return (false, "Select a valid category.");
            var desk = _data.Desks.FirstOrDefault(x => x.Id == deskId);
            if (desk is null) return (false, "Select a valid desk.");
            if (DeskForCategoryCore(name).Id == deskId) return (true, $"{name} is already on the {desk.Name} desk.");
            _data.CategoryDesks[name] = deskId;
            var moved = RefitTickets(t => string.Equals(t.Category, name, StringComparison.OrdinalIgnoreCase), $"Its category, {name}, was moved to the {desk.Name} desk.");
            Save();
            return (true, $"{name} moved to the {desk.Name} desk{(moved > 0 ? $", with {Plural(moved, "open ticket", "open tickets")}" : "")}.");
        }
    }

    // Everyone in the team joins the team's desk, since an account is always on its team's desk.
    public (bool Ok, string Message) SetTeamDesk(string? team, Guid deskId)
    {
        lock (_sync)
        {
            var name = _data.TechnicianTeams.FirstOrDefault(x => string.Equals(x, team?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return (false, "Select a valid team.");
            var desk = _data.Desks.FirstOrDefault(x => x.Id == deskId);
            if (desk is null) return (false, "Select a valid desk.");
            if (DeskForTeamCore(name)?.Id == deskId) return (true, $"{name} is already on the {desk.Name} desk.");
            _data.TeamDesks[name] = deskId;
            for (var i = 0; i < _data.Technicians.Count; i++)
                if (string.Equals(_data.Technicians[i].Team, name, StringComparison.OrdinalIgnoreCase) && !_data.Technicians[i].DeskIds.Contains(deskId))
                    _data.Technicians[i] = _data.Technicians[i] with { DeskIds = [.. _data.Technicians[i].DeskIds, deskId] };
            RefitTickets(t => string.Equals(t.TeamName, name, StringComparison.OrdinalIgnoreCase), $"Its team, {name}, was moved to the {desk.Name} desk.");
            Save();
            return (true, $"{name} moved to the {desk.Name} desk.");
        }
    }

    // Null shares the status with every desk. Refused while open tickets on other desks are in it - they'd have nowhere to go.
    public (bool Ok, string Message) SetStatusDesk(string? status, Guid? deskId)
    {
        lock (_sync)
        {
            var name = _data.Statuses.FirstOrDefault(x => string.Equals(x, status?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return (false, "Status was not found.");
            if (deskId is null)
            {
                if (_data.StatusDesks.Remove(name)) Save();
                return (true, $"{name} is used on every desk.");
            }
            var desk = _data.Desks.FirstOrDefault(x => x.Id == deskId);
            if (desk is null) return (false, "Select a valid desk.");
            if (IsBuiltInStatus(name)) return (false, "\"Closed\" is how every desk finishes a ticket, so it's always shared.");
            if (!_data.Statuses.Any(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase) && !IsBuiltInStatus(x) && DeskForStatusCore(x) is null))
                return (false, "Keep at least one open status shared by every desk, so every new ticket has somewhere to start.");
            var elsewhere = _data.Tickets.Count(t => !TicketInsights.IsClosed(t) && string.Equals(t.Status, name, StringComparison.OrdinalIgnoreCase) && DeskForCategoryCore(t.Category).Id != desk.Id);
            if (elsewhere > 0) return (false, $"{Plural(elsewhere, "open ticket", "open tickets")} on other desks {(elsewhere == 1 ? "is" : "are")} {name}. Move {(elsewhere == 1 ? "it" : "them")} on first.");
            _data.StatusDesks[name] = desk.Id;
            Save();
            return (true, $"{name} is now only used on the {desk.Name} desk.");
        }
    }

    // Applies FitToDeskCore to the open tickets matching, with a history line on each that changed. Returns how many
    // open tickets matched. Called with the lock held; the caller saves.
    private int RefitTickets(Func<TicketRecord, bool> match, string reason)
    {
        var count = 0;
        for (var i = 0; i < _data.Tickets.Count; i++)
        {
            var ticket = _data.Tickets[i];
            if (TicketInsights.IsClosed(ticket) || !match(ticket)) continue;
            count++;
            var (fitted, dropped) = FitToDeskCore(ticket);
            if (dropped.Count == 0) continue;
            fitted = WithSlaClock(fitted, DateTime.UtcNow);
            var history = ticket.History.ToList();
            var from = history.Count;
            AddTicketActivities(history, ticket, fitted);
            history.Add(new TicketActivity("Desk rules applied", reason, DateTime.UtcNow));
            StampActor(history, from);
            _data.Tickets[i] = fitted with { History = history };
        }
        return count;
    }

    // ---- Accounts ----

    // The desks an account ends up on: what was asked (or, asked nothing, what it had), only desks that exist, its team's
    // desk always, and the first desk if that leaves none. Called with the lock held.
    private IReadOnlyList<Guid> DesksForAccountCore(TechnicianRecord item, TechnicianRecord? previous)
    {
        var known = _data.Desks.Select(x => x.Id).ToHashSet();
        var desks = (item.DeskIds.Count > 0 ? item.DeskIds : previous?.DeskIds ?? []).Where(known.Contains).Distinct().ToList();
        if (DeskForTeamCore(item.Team) is { } teamDesk && !desks.Contains(teamDesk.Id)) desks.Add(teamDesk.Id);
        if (desks.Count == 0) desks.Add(_data.Desks[0].Id);
        // In desk order, so the account's first desk is the one its lists open on.
        return _data.Desks.Where(x => desks.Contains(x.Id)).Select(x => x.Id).ToList();
    }

    // ---- Keeping the desks whole ----

    // Run on every load and after seeding: there is always a desk, and every category, team and account is on one that
    // exists. An upgrading database has none, so "IT" is made and given everything - which is exactly what it had before.
    private void EnsureDesks()
    {
        _data.Desks ??= [];
        _data.Desks = _data.Desks.Where(x => !string.IsNullOrWhiteSpace(x.Name)).DistinctBy(x => x.Id).ToList();
        if (_data.Desks.Count == 0) _data.Desks.Add(new Desk(Guid.NewGuid(), DefaultDeskName, DefaultDeskName));
        var known = _data.Desks.Select(x => x.Id).ToHashSet();
        var first = _data.Desks[0].Id;
        _data.CategoryDesks = Placed(_data.CategoryDesks, _data.Categories, known, first);
        _data.TeamDesks = Placed(_data.TeamDesks, _data.TechnicianTeams, known, first);
        _data.StatusDesks = new Dictionary<string, Guid>(
            (_data.StatusDesks ?? new Dictionary<string, Guid>()).Where(x => known.Contains(x.Value) && !IsBuiltInStatus(x.Key) && _data.Statuses.Contains(x.Key, StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _data.Technicians.Count; i++)
        {
            var desks = DesksForAccountCore(_data.Technicians[i], null);
            if (!desks.SequenceEqual(_data.Technicians[i].DeskIds)) _data.Technicians[i] = _data.Technicians[i] with { DeskIds = desks };
        }
    }

    // Every value in the list mapped to a desk that exists, the unplaced ones to the first desk; entries for values no
    // longer in the list are dropped.
    private static Dictionary<string, Guid> Placed(Dictionary<string, Guid>? current, List<string> values, HashSet<Guid> known, Guid first)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
            result[value] = current is not null && current.TryGetValue(value, out var id) && known.Contains(id) ? id : first;
        return result;
    }

    // Renames and deletes in the option lists keep the desk placings in step (HelpdeskStore.Options).
    private static void RenameKey(Dictionary<string, Guid> map, string oldValue, string newValue)
    {
        if (map.Remove(oldValue, out var id)) map[newValue] = id;
    }

    // ---- Storage ----

    private static void EnsureDeskSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // No foreign keys: the placings are rewritten from the lists on every save, and a stale row is ignored on load.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Desks (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Prefix TEXT NOT NULL DEFAULT '', HelperLine TEXT NOT NULL DEFAULT '',
                Icon TEXT NOT NULL DEFAULT '', Color TEXT NOT NULL DEFAULT '', ShowInPortal INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS CategoryDesks (Category TEXT PRIMARY KEY, DeskId TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TeamDesks (Team TEXT PRIMARY KEY, DeskId TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS StatusDesks (Status TEXT PRIMARY KEY, DeskId TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TechnicianDesks (TechnicianId TEXT NOT NULL, DeskId TEXT NOT NULL, PRIMARY KEY (TechnicianId, DeskId));
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadDesks(SqliteConnection connection, StoreData data)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Prefix, HelperLine, Icon, Color, ShowInPortal FROM Desks ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.Desks.Add(new Desk(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6) != 0));
        }
        void ReadMap(string table, string column, Dictionary<string, Guid> target)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {column}, DeskId FROM {table};";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (Guid.TryParse(reader.GetString(1), out var id)) target[reader.GetString(0)] = id;
        }
        ReadMap("CategoryDesks", "Category", data.CategoryDesks);
        ReadMap("TeamDesks", "Team", data.TeamDesks);
        ReadMap("StatusDesks", "Status", data.StatusDesks);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TechnicianId, DeskId FROM TechnicianDesks ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            var byTechnician = new Dictionary<Guid, List<Guid>>();
            while (reader.Read())
                if (Guid.TryParse(reader.GetString(0), out var technicianId) && Guid.TryParse(reader.GetString(1), out var deskId))
                {
                    if (!byTechnician.TryGetValue(technicianId, out var list)) byTechnician[technicianId] = list = [];
                    list.Add(deskId);
                }
            for (var i = 0; i < data.Technicians.Count; i++)
                if (byTechnician.TryGetValue(data.Technicians[i].Id, out var desks))
                    data.Technicians[i] = data.Technicians[i] with { DeskIds = desks };
        }
    }

    private static void WriteDesks(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        var known = data.Desks.Select(x => x.Id).ToHashSet();
        foreach (var desk in data.Desks)
            Execute(connection, transaction, "INSERT INTO Desks (Id, Name, Prefix, HelperLine, Icon, Color, ShowInPortal) VALUES ($id,$name,$prefix,$helper,$icon,$color,$portal);",
                ("$id", desk.Id.ToString()), ("$name", desk.Name), ("$prefix", desk.Prefix), ("$helper", desk.HelperLine), ("$icon", desk.Icon), ("$color", desk.Color), ("$portal", desk.ShowInPortal ? 1 : 0));
        // Sorted, so a save that changed nothing else writes the same rows in the same order.
        foreach (var (category, id) in data.CategoryDesks.Where(x => known.Contains(x.Value) && data.Categories.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, "INSERT INTO CategoryDesks (Category, DeskId) VALUES ($category,$desk);", ("$category", category), ("$desk", id.ToString()));
        foreach (var (team, id) in data.TeamDesks.Where(x => known.Contains(x.Value) && data.TechnicianTeams.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, "INSERT INTO TeamDesks (Team, DeskId) VALUES ($team,$desk);", ("$team", team), ("$desk", id.ToString()));
        foreach (var (status, id) in data.StatusDesks.Where(x => known.Contains(x.Value) && data.Statuses.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, "INSERT INTO StatusDesks (Status, DeskId) VALUES ($status,$desk);", ("$status", status), ("$desk", id.ToString()));
        foreach (var technician in data.Technicians)
            foreach (var id in technician.DeskIds.Where(known.Contains).Distinct())
                Execute(connection, transaction, "INSERT INTO TechnicianDesks (TechnicianId, DeskId) VALUES ($technician,$desk);", ("$technician", technician.Id.ToString()), ("$desk", id.ToString()));
    }
}
