using System.Security.Claims;
using System.Text;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class JobsModel(HelpdeskStore store) : PageModel
{
    public static readonly int[] PageSizes = [25, 50, 100, 200];

    // Short query-string names. "area", "action" and "page" are reserved by routing, so they are avoided.
    [BindProperty(SupportsGet = true, Name = "view")] public string View { get; set; } = "open";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public List<string> Status { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "priority")] public List<string> Priority { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "category")] public List<string> Category { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "type")] public List<string> Type { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "tech")] public string? Technician { get; set; }
    [BindProperty(SupportsGet = true, Name = "team")] public string? Team { get; set; }
    // Desk names (HelpdeskStore.Desks), or "all". Left out, the list opens on the top bar's desk switcher (HelpdeskStore.DeskSwitch).
    [BindProperty(SupportsGet = true, Name = "desk")] public List<string> Desk { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "requester")] public string? Requester { get; set; }
    [BindProperty(SupportsGet = true, Name = "dept")] public string? Department { get; set; }
    [BindProperty(SupportsGet = true, Name = "loc")] public string? Location { get; set; }
    [BindProperty(SupportsGet = true, Name = "asset")] public string? Asset { get; set; }
    // Logged on or between these days, from a dashboard chart (Reports/Dashboard). Not in the Filters panel; its chip
    // takes it off.
    [BindProperty(SupportsGet = true, Name = "from")] public DateOnly? LoggedFrom { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public DateOnly? LoggedTo { get; set; }
    // Planned for: today, week, late or none (TicketListQuery.PlanFilters), or one day from the planner.
    [BindProperty(SupportsGet = true, Name = "plan")] public string? Planned { get; set; }
    [BindProperty(SupportsGet = true, Name = "planday")] public DateOnly? PlannedOn { get; set; }
    // How the tickets are laid out: the list, the board (cards in columns) or the planner (cards on the days they're
    // planned for). The board's columns are by status or by technician ("tech"); the planner shows the week holding
    // "day", or its month ("span"). The queue, search and filters are the same in all three.
    [BindProperty(SupportsGet = true, Name = "show")] public string? Show { get; set; }
    [BindProperty(SupportsGet = true, Name = "by")] public string? By { get; set; }
    [BindProperty(SupportsGet = true, Name = "span")] public string? Span { get; set; }
    [BindProperty(SupportsGet = true, Name = "day")] public DateOnly? Day { get; set; }
    // The kinds of other date (PlannerKinds) the planner leaves out.
    [BindProperty(SupportsGet = true, Name = "hide")] public List<string> Hide { get; set; } = [];
    [BindProperty(SupportsGet = true, Name = "sort")] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "dir")] public string? Dir { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int Size { get; set; } = 50;

    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<string> Teams => store.TechnicianTeams;
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Types => TicketTypes.All;
    public IReadOnlyDictionary<string, string> StatusDescriptions => store.StatusDescriptions;
    // Only the desks this person can see (HelpdeskStore.Desks): with one, there's nothing to choose between.
    public IReadOnlyList<Desk> Desks => _visibleDesks ??= store.DesksVisibleTo(User);
    private IReadOnlyList<Desk>? _visibleDesks;
    public bool HasSeveralDesks => Desks.Count > 1;
    // The desks being shown, empty for all of them, and the ones the desk switcher opens it on.
    public List<Desk> ShownDesks { get; private set; } = [];
    private List<Desk> _defaultDesks = [];
    private Func<string?, Desk> _deskOf = _ => null!;
    public string Ref(TicketRecord ticket) => HelpdeskStore.RefFor(_deskOf(ticket.Category), ticket);
    public string DeskName(TicketRecord ticket) => _deskOf(ticket.Category).Name;
    private bool DesksAreDefault => ShownDesks.Select(x => x.Id).ToHashSet().SetEquals(_defaultDesks.Select(x => x.Id));
    public int DueSoonHours => store.TicketDueSoonHours;
    [TempData] public string? Message { get; set; }

    public IReadOnlyList<TicketRecord> Rows { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> UserNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, string> TechnicianNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, AssetRecord> AssetsById { get; private set; } = new Dictionary<Guid, AssetRecord>();
    public IReadOnlyDictionary<string, int> ViewCounts { get; private set; } = new Dictionary<string, int>();
    public int TotalTickets { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public DateTime Now { get; } = DateTime.UtcNow;
    public Guid? CurrentTechnicianId { get; private set; }
    public TechnicianRecord? CurrentTechnician => CurrentTechnicianId is { } id ? Technicians.FirstOrDefault(x => x.Id == id) : null;
    public bool Descending => Dir == "desc";
    // An empty list limited to some desks says so, with the way to every desk, rather than "none logged yet".
    public string DeskPhrase => HelpdeskStore.DeskPhrase(ShownDesks);
    public string? AllDesksUrl() { var route = RouteFor(1, Sort!, Dir!, View); route["desk"] = "all"; return Url.Page("/Jobs", route); }
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || Status.Count > 0 || Priority.Count > 0 || Category.Count > 0 || Type.Count > 0 || !string.IsNullOrWhiteSpace(Technician)
        || !string.IsNullOrWhiteSpace(Team) || !string.IsNullOrWhiteSpace(Requester) || !string.IsNullOrWhiteSpace(Department) || !string.IsNullOrWhiteSpace(Location) || !string.IsNullOrWhiteSpace(Asset) || LoggedFrom is not null || LoggedTo is not null || !string.IsNullOrWhiteSpace(Planned) || PlannedOn is not null;

    // The list as last looked at - view, filters, sort and page - so "Back to tickets" on a ticket returns to it rather
    // than to a fresh, unfiltered list. Only a path on this page is ever stored or followed (see JobModel.BackUrl).
    public const string BackCookie = "tickets_back";

    public void OnGet()
    {
        Response.Cookies.Append(BackCookie, Request.Path + Request.QueryString, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, IsEssential = true
        });
        Prepare();
        var context = Context();
        // Another desk's tickets aren't here at all for someone not on it.
        var all = store.TicketsFor(User);
        var matches = BuildQuery().Run(all, context);
        // The queue tabs count within the desks shown, so a site technician's "Open" is the site's open tickets.
        var scoped = ShownDesks.Count == 0 ? all : all.Where(t => ShownDesks.Any(d => d.Id == _deskOf(t.Category).Id)).ToList();
        TotalTickets = scoped.Count;
        ViewCounts = TicketListQuery.ViewCounts(scoped, context);
        MatchCount = matches.Count;
        if (IsBoard) Board = BuildBoard(matches, all, context);
        if (IsPlanner) Planner = BuildPlanner(matches);
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)Size));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Rows = matches.Skip((PageNumber - 1) * Size).Take(Size).ToList();
        UserNames = context.Users.ToDictionary(x => x.Id, x => x.Name);
        TechnicianNames = context.Technicians.ToDictionary(x => x.Id, x => x.Name);
        AssetsById = context.Assets.ToDictionary(x => x.Id);
    }

    public DateOnly Today => DateOnly.FromDateTime(Now.ToLocalTime());

    // ---- The board ----
    public bool IsBoard => Show == "board";
    public bool ByTechnician => By == "tech";
    public bool CanEdit => store.UserCan(User, Modules.Tickets, ModulePermission.Edit);
    // A column never draws more cards than this; the rest are a link to the list.
    public const int BoardColumnLimit = 60;
    // How long a closed ticket stays in the Closed column.
    public const int BoardClosedDays = 7;
    public const string UnassignedKey = "none";
    // Key is what a card dropped here is given: a status, or a technician's id ("none" for nobody).
    public sealed record BoardColumn(string Key, string Title, IReadOnlyList<TicketRecord> Cards, int Total, bool IsClosed, string? ListUrl);
    public IReadOnlyList<BoardColumn> Board { get; private set; } = [];

    private List<BoardColumn> BuildBoard(List<TicketRecord> matches, IReadOnlyList<TicketRecord> all, TicketContext context)
    {
        BoardColumn Column(string key, string title, List<TicketRecord> cards, bool closed, string? listUrl) =>
            new(key, title, cards.Take(BoardColumnLimit).ToList(), cards.Count, closed, listUrl);
        string? ListUrl(string key, object? value) { var route = RouteFor(1, Sort!, Dir!, View); route["show"] = null; route["by"] = null; route[key] = value; return Url.Page("/Jobs", route); }
        var open = matches.Where(x => !TicketInsights.IsClosed(x)).ToList();
        var columns = new List<BoardColumn>();
        if (ByTechnician)
        {
            columns.Add(Column(UnassignedKey, "Unassigned", open.Where(x => x.TechnicianId is null).ToList(), false, ListUrl("tech", "none")));
            foreach (var technician in PeopleFor(open))
                columns.Add(Column(technician.Id.ToString(), technician.Name, open.Where(x => x.TechnicianId == technician.Id).ToList(), false, ListUrl("tech", technician.Id.ToString())));
            return columns;
        }

        var shownDeskIds = ShownDesks.Select(x => x.Id).ToHashSet();
        foreach (var status in store.Statuses.Where(x => !string.Equals(x, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            var cards = open.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
            // A status kept to a desk that isn't being shown has no column, unless something is in it.
            if (cards.Count == 0 && shownDeskIds.Count > 0 && store.DeskForStatus(status) is { } desk && !shownDeskIds.Contains(desk.Id)) continue;
            columns.Add(Column(status, status, cards, false, ListUrl("status", new[] { status })));
        }
        // Closed lately, whichever queue is showing, so a card dropped there can be seen to have landed.
        var query = BuildQuery();
        query.View = "all"; query.Sort = "updated"; query.Descending = true;
        var cutoff = Now.AddDays(-BoardClosedDays);
        var closed = query.Run(all, context).Where(x => TicketInsights.IsClosed(x) && (x.ClosedAt ?? x.LastModifiedAt) >= cutoff).OrderByDescending(x => x.ClosedAt ?? x.LastModifiedAt).ToList();
        columns.Add(Column(TicketInsights.ClosedStatus, TicketInsights.ClosedStatus, closed, true, null));
        return columns;
    }

    // The people who could be given one of these tickets: active, and on a desk being shown. Anyone else who still
    // holds one gets a place too, so no card goes missing.
    private List<TechnicianRecord> PeopleFor(IEnumerable<TicketRecord> tickets)
    {
        var deskIds = (ShownDesks.Count > 0 ? ShownDesks : Desks).Select(x => x.Id).ToHashSet();
        var holders = tickets.Where(x => x.TechnicianId is not null).Select(x => x.TechnicianId!.Value).ToHashSet();
        return store.Technicians.Where(x => holders.Contains(x.Id) || x.IsActive && x.DeskIds.Any(deskIds.Contains)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---- The planner ----
    public bool IsPlanner => Show == "planner";
    public bool IsMonth => Span == "month";
    // The side lists never draw more cards than this; the rest are a link to the list.
    public const int PlannerSideLimit = 40;
    // A month's day shows this many cards, then "+ n more".
    public const int PlannerMonthDayLimit = 4;
    public sealed record PlannerDay(DateOnly Date, bool IsToday, bool InRange);
    // One person's week (Key is their id, or "none"), the week's row of other dates (IsDates), or one week of the month
    // (Key empty). Dates are the other things on each day (HelpdeskStore.PlannerDates) that belong in this row.
    public sealed record PlannerRow(string Key, string Name, IReadOnlyDictionary<DateOnly, List<TicketRecord>> Cells, IReadOnlyList<PlannerDay> Days,
        IReadOnlyDictionary<DateOnly, List<PlannerDate>> Dates, bool IsDates = false);
    // Kinds: the kinds of other date this person may see, whether shown or hidden.
    public sealed record PlannerView(DateOnly From, DateOnly To, string Title, IReadOnlyList<DayOfWeek> Columns, IReadOnlyList<PlannerRow> Rows,
        IReadOnlyList<TicketRecord> Late, int LateTotal, IReadOnlyList<TicketRecord> Unplanned, int UnplannedTotal, IReadOnlyList<TechnicianRecord> People, int PlannedInRange,
        IReadOnlyList<string> Kinds);
    // A month's day shows this many other dates, then the rest folded away.
    public const int PlannerMonthDateLimit = 3;
    public PlannerView? Planner { get; private set; }

    private PlannerView BuildPlanner(List<TicketRecord> matches)
    {
        var today = Today;
        var anchor = Day ?? today;
        var (from, to) = IsMonth ? (new DateOnly(anchor.Year, anchor.Month, 1), new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month))) : TicketInsights.WeekOf(anchor);
        // A month is drawn in whole weeks, so its first and last weeks run into the months either side.
        var (gridFrom, gridTo) = (TicketInsights.WeekOf(from).From, TicketInsights.WeekOf(to).To);
        var planned = matches.Where(x => x.PlannedFor is { } on && on >= gridFrom && on <= gridTo).ToList();
        var byDay = planned.GroupBy(x => x.PlannedFor!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var kinds = PlannerKindsAllowed();
        var dates = kinds.Count == 0 ? [] : VisibleDates(gridFrom, gridTo, kinds.Except(Hide).ToHashSet());
        var datesByDay = dates.GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.ToList());
        // The school's days, and any other day something is on: nobody's Saturday job should vanish.
        var schoolDays = store.SchoolDays;
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var columns = weekdays.Where(d => schoolDays.Contains(d) || byDay.Keys.Any(x => x.DayOfWeek == d) || datesByDay.Keys.Any(x => x.DayOfWeek == d)).ToList();
        if (columns.Count == 0) columns = weekdays.Take(5).ToList();

        List<PlannerDay> DaysOf(DateOnly monday) => columns.Select(d => monday.AddDays(((int)d + 6) % 7)).Select(d => new PlannerDay(d, d == today, d >= from && d <= to)).ToList();
        var open = matches.Where(x => !TicketInsights.IsClosed(x)).ToList();
        var people = PeopleFor(IsMonth ? open : planned.Concat(open));
        var rows = new List<PlannerRow>();
        if (IsMonth)
        {
            for (var monday = gridFrom; monday <= gridTo; monday = monday.AddDays(7))
            {
                var days = DaysOf(monday);
                // A month that starts on a Sunday has a first week with no school day of its own in it.
                if (days.All(d => !d.InRange && !byDay.ContainsKey(d.Date))) continue;
                rows.Add(new("", "", days.ToDictionary(d => d.Date, d => byDay.GetValueOrDefault(d.Date) ?? []), days,
                    days.ToDictionary(d => d.Date, d => datesByDay.GetValueOrDefault(d.Date) ?? [])));
            }
        }
        else
        {
            var days = DaysOf(from);
            // Recurring work goes in its technician's row, or Unassigned; anyone not on the week, and everything else, in
            // the row of other dates at the top.
            var rowIds = people.Select(x => x.Id).ToHashSet();
            bool InPersonRow(PlannerDate date) => date.ForPerson && (date.TechnicianId is null || rowIds.Contains(date.TechnicianId.Value));
            PlannerRow Row(string key, string name, Guid? technicianId) =>
                new(key, name, days.ToDictionary(d => d.Date, d => (byDay.GetValueOrDefault(d.Date) ?? []).Where(x => x.TechnicianId == technicianId).ToList()), days,
                    days.ToDictionary(d => d.Date, d => (datesByDay.GetValueOrDefault(d.Date) ?? []).Where(x => InPersonRow(x) && x.TechnicianId == technicianId).ToList()));
            if (dates.Any(x => !InPersonRow(x)))
                rows.Add(new("", "Also on", days.ToDictionary(d => d.Date, _ => new List<TicketRecord>()), days,
                    days.ToDictionary(d => d.Date, d => (datesByDay.GetValueOrDefault(d.Date) ?? []).Where(x => !InPersonRow(x)).ToList()), IsDates: true));
            rows.Add(Row(UnassignedKey, "Unassigned", null));
            rows.AddRange(people.Select(x => Row(x.Id.ToString(), x.Name, x.Id)));
        }

        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        var title = IsMonth ? from.ToString("MMMM yyyy", culture)
            : from.Month == to.Month ? $"{from.Day}–{to.ToString("d MMMM yyyy", culture)}"
            : $"{from.ToString(from.Year == to.Year ? "d MMMM" : "d MMMM yyyy", culture)} – {to.ToString("d MMMM yyyy", culture)}";
        // Planned for a day gone by and still open: shown whichever week is on screen, soonest-missed first.
        var late = open.Where(x => TicketInsights.IsPlanLate(x, today)).OrderBy(x => x.PlannedFor).ToList();
        var unplanned = open.Where(x => x.PlannedFor is null).ToList();
        return new(from, to, title, columns, rows, late.Take(PlannerSideLimit).ToList(), late.Count, unplanned.Take(PlannerSideLimit).ToList(), unplanned.Count, people,
            planned.Count(x => x.PlannedFor >= from && x.PlannedFor <= to), kinds);
    }

    // The kinds of other date this person may see: what's switched on, and what their role can open.
    private List<string> PlannerKindsAllowed()
    {
        var kinds = new List<string> { PlannerKinds.Recurring };
        if (store.IsFeatureOn(Features.Rounds) && store.UserHasFlag(User, Modules.Flags.Rounds)) kinds.Add(PlannerKinds.Rounds);
        if (store.IsFeatureOn(Features.Loans) && store.UserCan(User, Modules.Loans, ModulePermission.Access)) kinds.Add(PlannerKinds.Loans);
        if (store.IsFeatureOn(Features.Onboarding)) kinds.Add(PlannerKinds.Starters);
        if (store.IsFeatureOn(Features.Compliance) && store.UserCan(User, Modules.Contracts, ModulePermission.Access)) kinds.Add(PlannerKinds.Contracts);
        if (store.IsFeatureOn(Features.Assets) && store.UserCan(User, Modules.Assets, ModulePermission.Access)) kinds.Add(PlannerKinds.Checks);
        return kinds;
    }

    // The other dates for the days on screen that this person may see, on the desks being shown. Something every desk
    // shares (a kit, a contract, a cleaning round) shows when a desk being shown uses that part of the helpdesk.
    private List<PlannerDate> VisibleDates(DateOnly from, DateOnly to, IReadOnlySet<string> kinds)
    {
        var desks = ShownDesks.Count > 0 ? ShownDesks : Desks;
        var deskIds = desks.Select(x => x.Id).ToHashSet();
        bool AreaShown(string area) => !HasSeveralDesks || desks.Any(d => HelpdeskStore.DeskUses(d, area));
        var shared = new Dictionary<string, bool>
        {
            [PlannerKinds.Rounds] = AreaShown(DeskAreas.Facilities),
            [PlannerKinds.Loans] = AreaShown(DeskAreas.Loans),
            [PlannerKinds.Starters] = AreaShown(DeskAreas.People),
            [PlannerKinds.Contracts] = AreaShown(DeskAreas.Compliance),
        };
        return store.PlannerDates(from, to, Now.ToLocalTime())
            .Where(x => kinds.Contains(x.Kind) && (x.DeskId is { } desk ? !HasSeveralDesks || deskIds.Contains(desk) : shared.GetValueOrDefault(x.Kind, true)))
            .ToList();
    }

    // Where one of the other dates opens, or null when there's nothing this person can open.
    public string? DateUrl(PlannerDate date) => date.Kind switch
    {
        PlannerKinds.Recurring => store.UserCan(User, Modules.Settings, ModulePermission.Edit) || store.UserHasFlag(User, Modules.Flags.FacilitiesSetup) ? Url.Page("/Settings/RecurringTickets") : null,
        PlannerKinds.Rounds => Url.Page("/Rounds", new { date = date.Date.ToString("yyyy-MM-dd") }),
        PlannerKinds.Loans => date.DeskId is not null && store.UserCan(User, Modules.Assets, ModulePermission.View) ? Url.Page("/Asset", new { id = date.RecordId }) : Url.Page("/Loans"),
        PlannerKinds.Starters => Url.Page("/Onboarding/Details", new { number = date.Number }),
        PlannerKinds.Contracts => store.UserCan(User, Modules.Contracts, ModulePermission.View) ? Url.Page("/Contract", new { id = date.RecordId }) : Url.Page("/Contracts"),
        PlannerKinds.Checks => date.RecordId is { } asset && store.UserCan(User, Modules.Assets, ModulePermission.View) ? Url.Page("/Asset", new { id = asset })
            : Url.Page("/Assets", new { flag = date.Text.Contains("support", StringComparison.OrdinalIgnoreCase) ? "support" : "check" }),
        _ => null
    };

    // The planner with one kind of other date shown or hidden.
    public string? PlannerKindUrl(string kind)
    {
        var route = RouteFor(1, Sort!, Dir!, View);
        var hide = Hide.Contains(kind) ? Hide.Where(x => x != kind).ToArray() : Hide.Append(kind).ToArray();
        route["hide"] = hide.Length == 0 ? null : hide;
        return Url.Page("/Jobs", route);
    }

    // The planner on another week or month, or as the other of the two.
    public string? PlannerUrl(string? span, DateOnly? day)
    {
        var route = RouteFor(1, Sort!, Dir!, View);
        route["show"] = "planner"; route["by"] = null; route["span"] = span;
        route["day"] = day is { } on && on != Today ? on.ToString("yyyy-MM-dd") : null;
        return Url.Page("/Jobs", route);
    }
    public string? PlannerStepUrl(int direction) => Planner is not { } planner ? null
        : PlannerUrl(Span, IsMonth ? planner.From.AddMonths(direction) : planner.From.AddDays(7 * direction));
    public string? PlanUrl() => Url.Page("/Jobs", "Plan", RouteFor(1, Sort!, Dir!, View));
    // The list of what's planned for one day, or of one of the side lists in full.
    public string? PlannedListUrl(string key, object? value) { var route = RouteFor(1, Sort!, Dir!, View); route["show"] = null; route["by"] = null; route["span"] = null; route["day"] = null; route["hide"] = null; route[key] = value; return Url.Page("/Jobs", route); }

    // A card put on a day (and, on the week, a person), or taken off the plan: dropped there, or sent with its Plan menu.
    public IActionResult OnPostPlan(int number, string? planFor, string? tech, bool clear)
    {
        Prepare();
        if (!CanEdit) return Forbid();
        var scripted = Request.Headers.XRequestedWith == "fetch";
        IActionResult Done(bool ok, string message)
        {
            if (scripted) return new JsonResult(new { ok, message });
            Message = message;
            return Back();
        }
        if (store.TicketsFor(User).All(x => x.Number != number)) return Done(false, "That ticket can't be found. It may have been deleted or moved to another desk.");
        DateOnly? on = null;
        if (!clear)
        {
            if (!DateOnly.TryParseExact(planFor?.Trim(), "yyyy-MM-dd", out var parsed)) return Done(false, "Choose a day to plan it for.");
            on = parsed;
        }
        var assignee = HelpdeskStore.PlanAssignee.Keep;
        if (!string.IsNullOrWhiteSpace(tech) && tech != "keep")
        {
            if (tech == UnassignedKey) assignee = HelpdeskStore.PlanAssignee.Nobody;
            else if (Guid.TryParse(tech, out var id)) assignee = HelpdeskStore.PlanAssignee.To(id);
            else return Done(false, "Choose who to give it to.");
        }
        var (ok, message) = store.PlanTicket(number, on, assignee);
        return Done(ok, message);
    }

    // A card moved to another column: dropped there, or sent with its Move menu. A drop asks for JSON back; the menu
    // without scripts gets the page again with a message.
    public IActionResult OnPostMove(int number, string? target, string? closingMessage)
    {
        Prepare();
        if (!CanEdit) return Forbid();
        var scripted = Request.Headers.XRequestedWith == "fetch";
        IActionResult Done(bool ok, string message, bool needsMessage = false)
        {
            if (scripted) return new JsonResult(new { ok, message, needsMessage });
            Message = message;
            return Back();
        }
        if (store.TicketsFor(User).FirstOrDefault(x => x.Number == number) is not { } ticket) return Done(false, "That ticket can't be found. It may have been deleted or moved to another desk.");
        var name = store.Ref(ticket);
        target = target?.Trim() ?? "";

        if (ByTechnician)
        {
            Guid? technicianId = null;
            if (target != UnassignedKey)
            {
                if (!Guid.TryParse(target, out var id)) return Done(false, "Choose who to give it to.");
                technicianId = id;
            }
            var assigned = store.BulkUpdateTickets([number], new("technician", TechnicianId: technicianId));
            if (assigned.Error is { } assignError) return Done(false, assignError);
            if (assigned.Skipped > 0) return Done(false, assigned.SkippedReason ?? "That ticket couldn't be moved.");
            var who = technicianId is { } given ? store.Technicians.FirstOrDefault(x => x.Id == given)?.Name ?? "them" : null;
            return Done(true, assigned.Unchanged > 0 ? $"{name} is already there." : who is null ? $"{name} is now unassigned." : $"{name} given to {who}.");
        }

        if (string.Equals(target, TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (TicketInsights.IsClosed(ticket)) return Done(true, $"{name} is already closed.");
            if (store.UnansweredChecks(number) > 0) return Done(false, $"{name} is a premises check: open it and fill in its checklist before closing it.");
            if (store.RequiresCloseMessage(ticket) && string.IsNullOrWhiteSpace(closingMessage))
                return Done(false, scripted ? $"{name} needs a closing message." : $"{name} needs a closing message. Open the ticket and use Mark as closed.", needsMessage: true);
            var closedResult = store.CloseTicket(number, closingMessage);
            return Done(closedResult.Ok, closedResult.Ok ? $"{name} closed." : closedResult.Message);
        }
        var moved = store.BulkUpdateTickets([number], new("status", target));
        if (moved.Error is { } moveError) return Done(false, moveError);
        if (moved.Skipped > 0) return Done(false, moved.SkippedReason ?? "That ticket couldn't be moved.");
        return Done(true, moved.Unchanged > 0 ? $"{name} is already there." : TicketInsights.IsClosed(ticket) ? $"{name} reopened as {target}." : $"{name} moved to {target}.");
    }
    public static string PlanLabel(string? plan) => plan switch
    {
        "today" => "Today",
        "week" => "This week",
        "late" => "Planned day gone by",
        "none" => "Not planned",
        _ => "Any"
    };

    public IActionResult OnPostBulk(string? operation, int[]? ids, bool selectAll, string? newStatus, string? newPriority, string? newCategory, string? newType, string? newTeam, string? newTechnician, string? commentText, bool commentInternal, string? closingMessage, string? mergeTarget, string? newPlanned)
    {
        Prepare();
        // Editing in bulk is still editing, and merging in bulk is still merging - this path used to check neither,
        // so a role without the destructive level could merge tickets here that it could not merge one at a time.
        if (!store.UserCan(User, Modules.Tickets, ModulePermission.Edit)) return Forbid();
        if (operation == "merge" && !store.UserCan(User, Modules.Tickets, ModulePermission.Delete)) return Forbid();
        // "Select all matching" re-applies the current filters here, so it covers every page, not just the one on screen.
        var visible = store.TicketsFor(User);
        var visibleNumbers = visible.Select(x => x.Number).ToHashSet();
        var targets = selectAll ? BuildQuery().Run(visible, Context()).Select(x => x.Number).ToList() : (ids ?? []).Distinct().Where(visibleNumbers.Contains).ToList();
        if (targets.Count == 0)
        {
            Message = "Select at least one ticket.";
            return Back();
        }
        // The Disposal type marks or frees the tickets' assets, so making one or unmaking one needs "Dispose of assets".
        if (operation == "type" && !store.UserHasFlag(User, Modules.Flags.DisposeAssets)
            && (string.Equals(newType?.Trim(), TicketTypes.Disposal, StringComparison.OrdinalIgnoreCase) || visible.Any(x => targets.Contains(x.Number) && x.Type == TicketTypes.Disposal)))
        {
            Message = "Changing a ticket to or from the Disposal type needs the Dispose of assets tick.";
            return Back();
        }

        if (operation == "export")
        {
            var chosen = targets.ToHashSet();
            var sorted = BuildQuery().Run(visible.Where(x => chosen.Contains(x.Number)), Context());
            var csv = TicketCsv.Build(sorted, store.Users, store.Technicians, store.Assets, store.Slas, store.TicketAttributeDefinitions, store.TicketAttributeValues.Where(x => visibleNumbers.Contains(x.TicketNumber)).ToList(), store.HasSeveralDesks ? _deskOf : null);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv; charset=utf-8", TicketCsv.FileName(DateTime.Now));
        }

        Guid? technicianId = Guid.TryParse(newTechnician, out var parsedTechnician) ? parsedTechnician : null;
        // The ticket to merge into is typed as people see it (SITE-5).
        int? mergeNumber = null;
        if (operation == "merge")
        {
            var (found, error) = store.FindTicketByRef(mergeTarget, store.TicketVisibility(User));
            if (found is null) { Message = error; return Back(); }
            mergeNumber = found;
        }
        HelpdeskStore.TicketBulkChange? change = operation switch
        {
            "status" => new("status", newStatus),
            "priority" => new("priority", newPriority),
            "category" => new("category", newCategory),
            "type" => new("type", newType),
            "team" => new("team", string.IsNullOrWhiteSpace(newTeam) ? TicketListQuery.None : newTeam),
            "technician" => new("technician", TechnicianId: technicianId),
            "plan" => new("plan", newPlanned),
            "comment" => new("comment", Text: commentText, Internal: commentInternal),
            "close" => new("close", Text: closingMessage),
            "merge" => new("merge", TargetNumber: mergeNumber),
            _ => null
        };
        if (change is null)
        {
            Message = "Choose what to change.";
            return Back();
        }

        var result = store.BulkUpdateTickets(targets, change);
        Message = result.Error ?? Describe(operation!, result, mergeNumber is { } into ? store.Ref(into) : "");
        return Back();
    }

    private static string Describe(string operation, HelpdeskStore.TicketBulkResult result, string mergeTarget)
    {
        static string Tickets(int count) => $"{count} ticket{(count == 1 ? "" : "s")}";
        if (result.Updated == 0 && result.Unchanged == 0 && result.Skipped == 0) return "None of the selected tickets could be found.";
        var text = new StringBuilder();
        if (result.Updated > 0)
            text.Append(operation switch
            {
                "merge" => $"{Tickets(result.Updated)} merged into {mergeTarget}.",
                "comment" => $"Comment added to {Tickets(result.Updated)}.",
                "close" => $"{Tickets(result.Updated)} closed.",
                _ => $"{Tickets(result.Updated)} updated."
            });
        var unchangedText = operation switch
        {
            "merge" => $"{mergeTarget} was left as it is.",
            "close" => $"{Tickets(result.Unchanged)} already closed.",
            _ => $"{Tickets(result.Unchanged)} already had that value."
        };
        if (result.Updated == 0 && result.Unchanged > 0)
            text.Append(operation switch
            {
                "merge" => $"Nothing to merge: only {mergeTarget} was selected.",
                "close" => result.Unchanged == 1 ? "That ticket is already closed." : $"All {result.Unchanged} tickets are already closed.",
                _ => result.Unchanged == 1 ? "No change made: that ticket already had that value." : $"No changes made: all {result.Unchanged} tickets already had that value."
            });
        else if (result.Unchanged > 0) text.Append(' ').Append(unchangedText);
        if (result.Skipped > 0) text.Append($" {Tickets(result.Skipped)} skipped. {result.SkippedReason}");
        return text.ToString().Trim();
    }

    private TicketContext Context() => new(store.Users, store.Technicians, store.Assets, store.Priorities, store.Statuses, Now, store.TicketDueSoonHours, CurrentTechnicianId, Onboardings,
        string.IsNullOrWhiteSpace(Search) ? null : store.TicketAttributeAnswers(), _deskOf);

    // Onboarding tickets' checklists, for the Onboarding queue, "My tickets", and each row's IT progress.
    private IReadOnlyDictionary<int, OnboardingRecord>? _onboardings;
    // The parts of the list that belong to a feature the school can switch off (Services/Features.cs).
    public bool ShowAssets => store.IsFeatureOn(Features.Assets);
    public bool ShowOnboarding => store.IsFeatureOn(Features.Onboarding);
    // None while onboarding is switched off (Services/Features.cs): they are listed and opened as ordinary tickets.
    public IReadOnlyDictionary<int, OnboardingRecord> Onboardings => _onboardings ??=
        store.IsFeatureOn(Features.Onboarding) ? store.Onboardings.ToDictionary(x => x.TicketNumber) : new Dictionary<int, OnboardingRecord>();

    private TicketListQuery BuildQuery() => new()
    {
        View = View, Search = Search, Status = Status, Priority = Priority, Category = Category, Type = Type, Technician = Technician, Team = Team,
        Desks = ShownDesks.Select(x => x.Id).ToList(),
        Requester = Guid.TryParse(Requester, out var requesterId) ? requesterId : null,
        Department = Department,
        Location = Location,
        Asset = Guid.TryParse(Asset, out var assetId) ? assetId : null,
        LoggedFrom = LoggedFrom, LoggedTo = LoggedTo,
        Planned = Planned, PlannedOn = PlannedOn,
        Sort = Sort!, Descending = Descending
    };

    // Ignores unknown views, sort columns and page sizes rather than failing, and works out who "me" is.
    private void Prepare()
    {
        View = TicketListQuery.Views.Contains(View?.Trim().ToLowerInvariant()) ? View!.Trim().ToLowerInvariant() : "open";
        if (View == "onboarding" && !ShowOnboarding) View = "open";
        var (defaultSort, defaultDescending) = TicketListQuery.DefaultSort(View);
        if (!TicketListQuery.SortColumns.Contains(Sort)) { Sort = defaultSort; Dir = defaultDescending ? "desc" : "asc"; }
        else Dir = string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        Planned = TicketListQuery.PlanFilters.Contains(Planned?.Trim().ToLowerInvariant()) ? Planned!.Trim().ToLowerInvariant() : null;
        Show = Show?.Trim().ToLowerInvariant() is "board" or "planner" ? Show.Trim().ToLowerInvariant() : null;
        Span = Show == "planner" && string.Equals(Span?.Trim(), "month", StringComparison.OrdinalIgnoreCase) ? "month" : null;
        if (Show != "planner") Day = null;
        Hide = Show == "planner" ? (Hide ?? []).Select(x => x?.Trim().ToLowerInvariant() ?? "").Where(PlannerKinds.All.Contains).Distinct().ToList() : [];
        By = Show == "board" && string.Equals(By?.Trim(), "tech", StringComparison.OrdinalIgnoreCase) ? "tech" : null;
        if (!PageSizes.Contains(Size)) Size = 50;
        if (PageNumber < 1) PageNumber = 1;
        Status = Clean(Status); Priority = Clean(Priority); Category = Clean(Category); Type = Clean(Type);
        // My tickets are always the signed-in account's own; a colleague's are the Technician filter.
        CurrentTechnicianId = SignedInTechnician.Id(store, User);
        PrepareDesks();
    }

    // Which desks to show. Nothing asked for opens on the desk switcher's choice; "all" is every desk. With one desk
    // there is nothing to choose.
    private void PrepareDesks()
    {
        _deskOf = store.DeskLookup();
        if (!HasSeveralDesks) { ShownDesks = []; Desk = []; return; }
        var desks = Desks;
        _defaultDesks = store.WorkingDesks(User).ToList();
        // Every desk is the same as no filter.
        if (_defaultDesks.Count == desks.Count) _defaultDesks = [];
        var asked = Request.Query.ContainsKey("desk") || (Request.HasFormContentType && Request.Form.ContainsKey("desk"));
        if (!asked) ShownDesks = _defaultDesks.ToList();
        else if (Desk.Any(x => string.Equals(x, "all", StringComparison.OrdinalIgnoreCase))) ShownDesks = [];
        else ShownDesks = desks.Where(d => Desk.Any(x => string.Equals(x, d.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        if (ShownDesks.Count == desks.Count) ShownDesks = [];
        Desk = ShownDesks.Select(x => x.Name).ToList();
    }

    // The desk part of an address: nothing when it's the desk switcher's choice, "all" for every desk.
    private object? DeskRoute(List<Desk> desks) =>
        !HasSeveralDesks || desks.Select(x => x.Id).ToHashSet().SetEquals(_defaultDesks.Select(x => x.Id)) ? null
        : desks.Count == 0 ? "all"
        : desks.Select(x => x.Name).ToArray();

    private static List<string> Clean(List<string>? values) => (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // The current view, filters, sort and page as hidden form fields, so a bulk action re-applies them and returns to the same place.
    public IEnumerable<(string Name, string Value)> StateFields()
    {
        yield return ("view", View);
        if (!string.IsNullOrWhiteSpace(Search)) yield return ("q", Search);
        foreach (var value in Status) yield return ("status", value);
        foreach (var value in Priority) yield return ("priority", value);
        foreach (var value in Category) yield return ("category", value);
        foreach (var value in Type) yield return ("type", value);
        if (!string.IsNullOrWhiteSpace(Technician)) yield return ("tech", Technician);
        if (!string.IsNullOrWhiteSpace(Team)) yield return ("team", Team);
        if (HasSeveralDesks)
        {
            if (ShownDesks.Count == 0) yield return ("desk", "all");
            foreach (var desk in ShownDesks) yield return ("desk", desk.Name);
        }
        if (!string.IsNullOrWhiteSpace(Requester)) yield return ("requester", Requester);
        if (!string.IsNullOrWhiteSpace(Department)) yield return ("dept", Department);
        if (!string.IsNullOrWhiteSpace(Location)) yield return ("loc", Location);
        if (!string.IsNullOrWhiteSpace(Asset)) yield return ("asset", Asset);
        if (LoggedFrom is { } from) yield return ("from", from.ToString("yyyy-MM-dd"));
        if (LoggedTo is { } to) yield return ("to", to.ToString("yyyy-MM-dd"));
        if (!string.IsNullOrWhiteSpace(Planned)) yield return ("plan", Planned);
        if (PlannedOn is { } plannedOn) yield return ("planday", plannedOn.ToString("yyyy-MM-dd"));
        yield return ("sort", Sort!);
        yield return ("dir", Dir!);
        yield return ("p", PageNumber.ToString());
        yield return ("size", Size.ToString());
    }

    private IActionResult Back() => Redirect(Url.Page("/Jobs", RouteFor(PageNumber, Sort!, Dir!, View)) ?? "/Jobs");

    // Builds the query string for a link, leaving out anything that is at its default so URLs stay short.
    private Dictionary<string, object?> RouteFor(int page, string sort, string dir, string view)
    {
        var (defaultSort, defaultDescending) = TicketListQuery.DefaultSort(view);
        var isDefaultSort = sort == defaultSort && (dir == "desc") == defaultDescending;
        var route = new Dictionary<string, object?>
        {
            ["view"] = view == "open" ? null : view,
            ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
            ["status"] = Status.Count == 0 ? null : Status.ToArray(),
            ["priority"] = Priority.Count == 0 ? null : Priority.ToArray(),
            ["category"] = Category.Count == 0 ? null : Category.ToArray(),
            ["type"] = Type.Count == 0 ? null : Type.ToArray(),
            ["desk"] = DeskRoute(ShownDesks),
            ["tech"] = Blank(Technician), ["team"] = Blank(Team), ["requester"] = Blank(Requester), ["dept"] = Blank(Department), ["loc"] = Blank(Location), ["asset"] = Blank(Asset),
            ["from"] = LoggedFrom?.ToString("yyyy-MM-dd"), ["to"] = LoggedTo?.ToString("yyyy-MM-dd"),
            ["plan"] = Blank(Planned), ["planday"] = PlannedOn?.ToString("yyyy-MM-dd"),
            ["show"] = Show, ["by"] = By, ["span"] = Span, ["day"] = Day?.ToString("yyyy-MM-dd"), ["hide"] = Hide.Count == 0 ? null : Hide.ToArray(),
            ["sort"] = isDefaultSort ? null : sort,
            ["dir"] = isDefaultSort ? null : dir,
            ["p"] = page > 1 ? page : null,
            ["size"] = Size == 50 ? null : Size
        };
        return route;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // A new column starts newest-first for numbers and dates that mean "recent", and A to Z or soonest-first for the rest; the same column again flips it.
    public string? SortUrl(string column)
    {
        var direction = Sort == column ? (Descending ? "asc" : "desc") : (column is "number" or "created" or "updated" ? "desc" : "asc");
        return Url.Page("/Jobs", RouteFor(1, column, direction, View));
    }
    // The same queue, search and filters, laid out another way.
    public string? ShowUrl(string? show, string? by = null) { var route = RouteFor(1, Sort!, Dir!, View); route["show"] = show; route["by"] = show == "board" ? by : null; if (show != "planner") { route["span"] = null; route["day"] = null; route["hide"] = null; } return Url.Page("/Jobs", route); }
    // Where a card's move is posted: the board as it stands, so the page comes back the same.
    public string? MoveUrl() => Url.Page("/Jobs", "Move", RouteFor(1, Sort!, Dir!, View));
    public string? PageUrl(int page) => Url.Page("/Jobs", RouteFor(page, Sort!, Dir!, View));
    // A queue tab keeps the search and filters but starts on the first page and that queue's own sort order.
    public string? ViewUrl(string view) => Url.Page("/Jobs", RouteFor(1, TicketListQuery.DefaultSort(view).Sort, TicketListQuery.DefaultSort(view).Descending ? "desc" : "asc", view));
    // The printable daily sheet for what the list is showing: the same desks, and the team if one is chosen.
    // With a day: that day's planned jobs (planner stage 5), a page for each person.
    public string? DailySheetUrl(DateOnly? day = null) => Url.Page("/DailySheet", new Dictionary<string, object?> { ["desk"] = ShownDesks.Count == 0 ? null : ShownDesks.Select(x => x.Name).ToArray(), ["team"] = string.IsNullOrWhiteSpace(Team) || Team == TicketListQuery.None ? null : Team,
        ["day"] = day?.ToString("yyyy-MM-dd"), ["split"] = day is null ? null : "person" });
    public string? ClearUrl() => Url.Page("/Jobs", new { view = View == "open" ? null : View, show = Show, by = By, span = Span, day = Day?.ToString("yyyy-MM-dd"), hide = Hide.Count == 0 ? null : Hide.ToArray() });

    // Filters in the Filters panel that are switched on - the number on its button.
    public int PanelFilterCount => Status.Count + Priority.Count + Category.Count + Type.Count + (DesksAreDefault ? 0 : 1)
        + new[] { Technician, Team, Requester, Department, Location, Planned }.Count(x => !string.IsNullOrWhiteSpace(x));

    // Every filter in force, each as a chip that takes just that one off. With the panel closed, this is how the list
    // says what it is showing.
    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        string? Without(string key, object? value) { var route = RouteFor(1, Sort!, Dir!, View); route[key] = value; return Url.Page("/Jobs", route); }
        void Many(string key, string label, List<string> values)
        {
            foreach (var value in values)
            {
                var rest = values.Where(x => !string.Equals(x, value, StringComparison.OrdinalIgnoreCase)).ToArray();
                chips.Add(new($"{label}: {value}", Without(key, rest.Length == 0 ? null : rest)));
            }
        }

        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q", null)));
        if (!string.IsNullOrWhiteSpace(Technician))
        {
            var name = Technician switch
            {
                "me" => CurrentTechnician is { } me ? $"Me ({me.Name})" : "Me",
                "none" => "Unassigned",
                _ => Guid.TryParse(Technician, out var id) ? Technicians.FirstOrDefault(x => x.Id == id)?.Name ?? "Unknown" : Technician
            };
            chips.Add(new($"Technician: {name}", Without("tech", null)));
        }
        if (!string.IsNullOrWhiteSpace(Team)) chips.Add(new($"Team: {(Team == TicketListQuery.None ? "No team" : Team)}", Without("team", null)));
        // Shown even when it's the desk switcher's choice, so the list says it isn't everything.
        foreach (var desk in ShownDesks)
            chips.Add(new($"Desk: {desk.Name}", Without("desk", DeskRoute(ShownDesks.Where(x => x.Id != desk.Id).ToList()))));
        if (!string.IsNullOrWhiteSpace(Requester))
            chips.Add(new($"Requester: {(Guid.TryParse(Requester, out var requesterId) ? Users.FirstOrDefault(x => x.Id == requesterId)?.Name ?? "Unknown" : Requester)}", Without("requester", null)));
        if (!string.IsNullOrWhiteSpace(Department)) chips.Add(new($"Department: {Department}", Without("dept", null)));
        if (!string.IsNullOrWhiteSpace(Location)) chips.Add(new($"Location: {(Location == TicketListQuery.None ? "Not specified" : Location)}", Without("loc", null)));
        if (!string.IsNullOrWhiteSpace(Asset))
            chips.Add(new($"Asset: {(Guid.TryParse(Asset, out var assetId) && AssetsById.TryGetValue(assetId, out var asset) ? asset.AssetTag : "Unknown")}", Without("asset", null)));
        if (LoggedFrom is not null || LoggedTo is not null)
        {
            var label = LoggedFrom == LoggedTo ? $"Logged {LoggedFrom:ddd d MMM}"
                : LoggedTo is null ? $"Logged from {LoggedFrom:d MMM yyyy}"
                : LoggedFrom is null ? $"Logged up to {LoggedTo:d MMM yyyy}"
                : $"Logged {LoggedFrom:d MMM} to {LoggedTo:d MMM yyyy}";
            var route = RouteFor(1, Sort!, Dir!, View); route["from"] = null; route["to"] = null;
            chips.Add(new(label, Url.Page("/Jobs", route)));
        }
        if (!string.IsNullOrWhiteSpace(Planned)) chips.Add(new($"Planned: {PlanLabel(Planned)}", Without("plan", null)));
        if (PlannedOn is { } plannedOn) chips.Add(new($"Planned for {plannedOn:ddd d MMM}", Without("planday", null)));
        Many("status", "Status", Status);
        Many("type", "Type", Type);
        Many("priority", "Priority", Priority);
        Many("category", "Category", Category);
        return chips;
    }
    public string SortMark(string column) => Sort != column ? "" : Descending ? " ▼" : " ▲";

    public static string ViewLabel(string view) => view switch
    {
        "mine" => "My tickets",
        "unassigned" => "Unassigned",
        "overdue" => "Overdue / due soon",
        "onboarding" => "Onboarding",
        "all" => "All tickets",
        _ => "Open"
    };
}
