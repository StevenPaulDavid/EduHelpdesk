using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The two desks every helpdesk has: IT, and Facilities - the site team's repairs, premises checks and cleaning. Each
// can be renamed (Site, Estates...) or switched off: an IT-only school turns Facilities off, a premises-only one IT.
// Neither can be deleted, and one desk always stays on. A school can still add desks of its own.
//
// Switching a desk off sets it aside whole (OffDesk): its categories, teams, own statuses, the asset types placed on it
// and the people on it leave the lists, so nothing else in the helpdesk sees them, and come back when it is switched
// on. A desk with tickets can't be switched off - they would lose their desk and their numbers - so it is a choice
// made when setting up, or for a desk that never took off.
//
// A new install starts with both on. An install from before Facilities was built in is asked once, in Settings →
// Desks, whether one of its own desks is the Facilities desk, whether to add one, or not now (FacilitiesChoicePending).
public sealed partial class HelpdeskStore
{
    public const string FacilitiesDeskName = "Facilities";
    // A new Facilities desk's categories, each with a portal button, and its team. All of them can be renamed or removed.
    private static readonly (string Category, string Item, string Helper)[] FacilitiesStarter =
    [
        ("Heating & hot water", "Room too cold or too hot", "Radiators, heaters or no hot water"),
        ("Plumbing & leaks", "Leak, flood or blocked toilet", "Water where it shouldn't be, or a toilet or sink that won't drain"),
        ("Electrical & lighting", "Lights or sockets not working", "Bulbs out, flickering lights or a dead socket"),
        ("Building (doors, windows, roof)", "Door, window or lock broken", "Won't open, won't close or won't lock"),
        ("Grounds & outside", "Problem outside", "Paths, fences, gates, grounds or the car park"),
        ("Cleaning", "Something needs cleaning", "A spill, a mess or a room that was missed"),
        ("Furniture & moves", "Furniture broken or needs moving", "Desks, chairs and cupboards"),
        ("Security & safety", "Something isn't safe", "A hazard, a trip risk or a security worry"),
    ];
    public const string FacilitiesTeam = "Site team";
    // What the Facilities desk leaves out of the top bar to start with; the IT desk leaves out the Facilities menu.
    private static readonly string[] FacilitiesHiddenAreas = [DeskAreas.Kits, DeskAreas.Loans, DeskAreas.Projects, DeskAreas.People, DeskAreas.Compliance];

    // Every desk, on and off: the on ones in their order, then the off ones.
    public IReadOnlyList<Desk> AllDesks { get { lock (_sync) return _data.Desks.Concat(_data.OffDesks.Select(x => x.Desk)).ToList(); } }
    public bool IsDeskOn(Guid id) { lock (_sync) return _data.Desks.Any(x => x.Id == id); }
    // The built-in desk of this kind, while it is on.
    public Desk? BuiltInDesk(string kind) { lock (_sync) return _data.Desks.FirstOrDefault(x => x.Kind == kind); }
    // An install from before Facilities was built in, not yet asked.
    public bool FacilitiesChoicePending { get { lock (_sync) return !_data.Desks.Any(x => x.Kind == DeskKinds.Facilities) && !_data.OffDesks.Any(x => x.Desk.Kind == DeskKinds.Facilities); } }

    // ---- Switching off and on ----

    public (bool Ok, string Message) SwitchDeskOff(Guid id)
    {
        lock (_sync)
        {
            var desk = _data.Desks.FirstOrDefault(x => x.Id == id);
            if (desk is null) return (false, "That desk was not found.");
            if (!desk.IsBuiltIn) return (false, $"Only the built-in desks switch off. Delete the {desk.Name} desk instead.");
            if (_data.Desks.Count == 1) return (false, $"The {desk.Name} desk is the only one on. Switch another on first.");
            var categories = _data.Categories.Where(x => DeskForCategoryCore(x).Id == id).ToList();
            var tickets = _data.Tickets.Count(t => categories.Contains(t.Category, StringComparer.OrdinalIgnoreCase));
            if (tickets > 0)
                return (false, $"The {desk.Name} desk has {Plural(tickets, "ticket", "tickets")}, which would lose their desk and their numbers, so it stays on. To keep it out of the way, untick \"Offer this desk in the staff portal\" and leave it out of people's desks.");
            bool Uses(string? category) => categories.Contains(category ?? "", StringComparer.OrdinalIgnoreCase);
            if (_data.TicketTemplates.FirstOrDefault(x => Uses(x.Category)) is { } template)
                return (false, $"The ticket template \"{template.Name}\" uses a {desk.Name} category. Delete it or give it another category first.");
            if (_data.RecurringTickets.FirstOrDefault(x => Uses(x.Category)) is { } schedule)
                return (false, $"The recurring ticket \"{schedule.Name}\" uses a {desk.Name} category. Delete it or give it another category first.");
            if (_data.TicketAttributeDefinitions.FirstOrDefault(x => x.Categories.Any(Uses)) is { } attribute)
                return (false, $"The custom field \"{attribute.Name}\" is asked on a {desk.Name} category. Take that category off it first.");
            var teams = _data.TechnicianTeams.Where(x => DeskForTeamCore(x)?.Id == id).ToList();
            if (_data.Technicians.FirstOrDefault(x => teams.Contains(x.Team, StringComparer.OrdinalIgnoreCase)) is { } member)
                return (false, $"{member.Name} is in the {member.Team} team, on the {desk.Name} desk. Put them in a team on another desk first.");

            var statuses = _data.Statuses.Where(x => DeskForStatusCore(x)?.Id == id).ToList();
            var assetTypes = _data.AssetTypeDesks.Where(x => x.Value == id).Select(x => x.Key).ToList();
            var people = _data.Technicians.Where(x => x.DeskIds.Contains(id)).Select(x => x.Id).ToList();
            _data.Desks.Remove(desk);
            _data.OffDesks.Add(new OffDesk(desk, categories, teams, statuses, assetTypes, people));
            _data.Categories.RemoveAll(x => categories.Contains(x, StringComparer.OrdinalIgnoreCase));
            _data.TechnicianTeams.RemoveAll(x => teams.Contains(x, StringComparer.OrdinalIgnoreCase));
            _data.Statuses.RemoveAll(x => statuses.Contains(x, StringComparer.OrdinalIgnoreCase));
            foreach (var category in categories) _data.CategoryDesks.Remove(category);
            foreach (var team in teams) _data.TeamDesks.Remove(team);
            foreach (var status in statuses) _data.StatusDesks.Remove(status);
            // The asset types and the people go to the first desk still on, and back again when it is switched on.
            EnsureDesks();
            Save();
            return (true, $"The {desk.Name} desk is switched off. Its {Plural(categories.Count, "category", "categories")} and {Plural(teams.Count, "team", "teams")} are set aside, and come back if you switch it on again.");
        }
    }

    public (bool Ok, string Message) SwitchDeskOn(Guid id)
    {
        lock (_sync)
        {
            var off = _data.OffDesks.FirstOrDefault(x => x.Desk.Id == id);
            if (off is null) return (false, "That desk was not found.");
            var desk = off.Desk;
            if (_data.Desks.FirstOrDefault(x => string.Equals(x.Name, desk.Name, StringComparison.OrdinalIgnoreCase)) is { } sameName)
                return (false, $"Another desk is already called {sameName.Name}. Rename one of them first.");
            if (desk.Prefix.Length > 0 && _data.Desks.FirstOrDefault(x => string.Equals(x.Prefix, desk.Prefix, StringComparison.OrdinalIgnoreCase)) is { } samePrefix)
                return (false, $"The {samePrefix.Name} desk already uses the prefix {desk.Prefix}. Change one of them first.");
            _data.OffDesks.Remove(off);
            // IT goes back to the front, where it started; anything else to the end.
            if (desk.Kind == DeskKinds.It) _data.Desks.Insert(0, desk); else _data.Desks.Add(desk);
            // What it set aside comes back, except anything another desk has taken the name of meanwhile.
            foreach (var category in off.Categories.Where(x => !_data.Categories.Contains(x, StringComparer.OrdinalIgnoreCase)))
            {
                _data.Categories.Add(category);
                _data.CategoryDesks[category] = id;
            }
            foreach (var team in off.Teams.Where(x => !_data.TechnicianTeams.Contains(x, StringComparer.OrdinalIgnoreCase)))
            {
                _data.TechnicianTeams.Add(team);
                _data.TeamDesks[team] = id;
            }
            foreach (var status in off.Statuses.Where(x => !_data.Statuses.Contains(x, StringComparer.OrdinalIgnoreCase)))
            {
                // Before Closed, which is always last.
                var closed = _data.Statuses.FindIndex(IsBuiltInStatus);
                _data.Statuses.Insert(closed < 0 ? _data.Statuses.Count : closed, status);
                _data.StatusDesks[status] = id;
            }
            foreach (var type in off.AssetTypes.Where(x => _data.AssetTypes.Contains(x, StringComparer.OrdinalIgnoreCase))) _data.AssetTypeDesks[type] = id;
            for (var i = 0; i < _data.Technicians.Count; i++)
                if (off.Technicians.Contains(_data.Technicians[i].Id) && !_data.Technicians[i].DeskIds.Contains(id))
                    _data.Technicians[i] = _data.Technicians[i] with { DeskIds = [.. _data.Technicians[i].DeskIds, id] };
            // A Facilities desk that has never been set up (Not now, on an older install) starts with the starter set.
            var started = desk.Kind == DeskKinds.Facilities && off.Categories.Count == 0 && StartFacilitiesCore(desk);
            EnsureDesks();
            Save();
            return (true, $"The {desk.Name} desk is on again{(started ? ", with a starter set of categories and a team" : "")}. Add people to it on their technician account.");
        }
    }

    // ---- Facilities on an older install: asked once (FacilitiesChoicePending) ----

    // One of the school's own desks - Site, say - becomes the Facilities desk, keeping its name, tickets and setup.
    public (bool Ok, string Message) MakeFacilitiesDesk(Guid id)
    {
        lock (_sync)
        {
            if (!FacilitiesChoicePendingCore()) return (false, "There's already a Facilities desk.");
            var index = _data.Desks.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "That desk was not found.");
            if (_data.Desks[index].IsBuiltIn) return (false, "Choose one of your own desks.");
            _data.Desks[index] = _data.Desks[index] with { Kind = DeskKinds.Facilities };
            HideFacilitiesFromIt();
            Save();
            return (true, $"{_data.Desks[index].Name} is now the Facilities desk: the Facilities menu shows while someone works on it.");
        }
    }

    // A new Facilities desk with the starter set.
    public (bool Ok, string Message) AddFacilitiesDesk()
    {
        lock (_sync)
        {
            if (!FacilitiesChoicePendingCore()) return (false, "There's already a Facilities desk.");
            var desk = NewFacilitiesDesk();
            if (desk is null) return (false, $"Another desk is already called {FacilitiesDeskName}. Make that one the Facilities desk, or rename it first.");
            _data.Desks.Add(desk);
            StartFacilitiesCore(desk);
            HideFacilitiesFromIt();
            EnsureDesks();
            Save();
            return (true, $"{desk.Name} desk added with a starter set of categories and the {FacilitiesTeam}. Add people to it on their technician account.");
        }
    }

    // Not now: the Facilities desk is there but off, and can be switched on in Settings → Desks whenever.
    public (bool Ok, string Message) DeclineFacilitiesDesk()
    {
        lock (_sync)
        {
            if (!FacilitiesChoicePendingCore()) return (false, "There's already a Facilities desk.");
            var desk = NewFacilitiesDesk(allowNameClash: true)!;
            _data.OffDesks.Add(new OffDesk(desk, [], [], [], [], []));
            Save();
            return (true, "Facilities stays off. Switch it on in Settings → Desks whenever you want it.");
        }
    }

    private bool FacilitiesChoicePendingCore() => !_data.Desks.Any(x => x.Kind == DeskKinds.Facilities) && !_data.OffDesks.Any(x => x.Desk.Kind == DeskKinds.Facilities);

    // ---- Setting up ----

    // A new install's Facilities desk (SeedStarterData), after IT.
    private void SeedFacilitiesDesk()
    {
        if (_data.Desks.Any(x => x.Kind == DeskKinds.Facilities)) return;
        if (NewFacilitiesDesk() is not { } desk) return;
        _data.Desks.Add(desk);
        StartFacilitiesCore(desk);
        HideFacilitiesFromIt();
    }

    private Desk? NewFacilitiesDesk(bool allowNameClash = false)
    {
        var taken = _data.Desks.Concat(_data.OffDesks.Select(x => x.Desk)).ToList();
        if (!allowNameClash && taken.Any(x => string.Equals(x.Name, FacilitiesDeskName, StringComparison.OrdinalIgnoreCase))) return null;
        // FAC, or no prefix if a desk of the school's already has it - it can be set in Settings → Desks.
        var prefix = new[] { "FAC", "FM", "SITE" }.FirstOrDefault(p => !taken.Any(x => string.Equals(x.Prefix, p, StringComparison.OrdinalIgnoreCase))) ?? "";
        return new Desk(Guid.NewGuid(), FacilitiesDeskName, prefix, "Heating, leaks, lights, doors, grounds and cleaning", "wrench", "orange")
        {
            Kind = DeskKinds.Facilities,
            HiddenAreas = FacilitiesHiddenAreas
        };
    }

    // The starter categories, their portal buttons and the team, on this desk - skipping any name the school already
    // has. True when anything was added. Called with the lock held; the caller saves.
    private bool StartFacilitiesCore(Desk desk)
    {
        var added = false;
        foreach (var (category, item, helper) in FacilitiesStarter)
        {
            if (_data.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)) continue;
            _data.Categories.Add(category);
            _data.CategoryDesks[category] = desk.Id;
            _data.ServiceItems.Add(new ServiceItem(Guid.NewGuid(), category, item, "", helper));
            added = true;
        }
        if (!_data.TechnicianTeams.Contains(FacilitiesTeam, StringComparer.OrdinalIgnoreCase))
        {
            _data.TechnicianTeams.Add(FacilitiesTeam);
            _data.TeamDesks[FacilitiesTeam] = desk.Id;
            added = true;
        }
        return added;
    }

    // Once there is a Facilities desk, the IT desk leaves the Facilities menu out of its top bar (it can be put back).
    private void HideFacilitiesFromIt()
    {
        var index = _data.Desks.FindIndex(x => x.Kind == DeskKinds.It);
        if (index >= 0 && !_data.Desks[index].HiddenAreas.Contains(DeskAreas.Facilities))
            _data.Desks[index] = _data.Desks[index] with { HiddenAreas = [.. _data.Desks[index].HiddenAreas, DeskAreas.Facilities] };
    }

    // Run from EnsureDesks: an install from before the built-in desks has its IT desk marked - the one called IT, or the
    // first. (Its cleaning rounds area reads as the Facilities menu: ParseAreas.)
    private void EnsureBuiltInDesks()
    {
        _data.OffDesks ??= [];
        if (!_data.Desks.Concat(_data.OffDesks.Select(x => x.Desk)).Any(x => x.Kind == DeskKinds.It))
        {
            var index = _data.Desks.FindIndex(x => string.Equals(x.Name, DefaultDeskName, StringComparison.OrdinalIgnoreCase) && !x.IsBuiltIn);
            if (index < 0) index = _data.Desks.FindIndex(x => !x.IsBuiltIn);
            if (index >= 0) _data.Desks[index] = _data.Desks[index] with { Kind = DeskKinds.It };
        }
    }
}
