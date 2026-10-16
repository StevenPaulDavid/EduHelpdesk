using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

public class RecurringTicketsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<RecurringTicket> Items => store.RecurringTickets;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<string> Teams => store.TechnicianTeams;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    public DateTime? NextDue(RecurringTicket item) => store.NextRecurringDue(item);
    // Each schedule's last ticket, to show whether it is still open (and so holding up the next one).
    public IReadOnlyDictionary<int, TicketRecord> LastTickets => _lastTickets ??= LoadLastTickets();
    private Dictionary<int, TicketRecord>? _lastTickets;
    private Dictionary<int, TicketRecord> LoadLastTickets()
    {
        var numbers = Items.Select(x => x.LastTicketNumber).OfType<int>().ToHashSet();
        return store.Tickets.Where(x => numbers.Contains(x.Number)).ToDictionary(x => x.Number);
    }

    [BindProperty] public FormInput Input { get; set; } = new();
    [TempData] public string? Message { get; set; }
    // A save that was refused comes back with what was typed, in the form it came from (EditingId, or the add form).
    public string? Error { get; private set; }
    public Guid? EditingId { get; private set; }

    public void OnGet() { }

    public FormInput FormFor(RecurringTicket item) => EditingId == item.Id ? Input : FormInput.From(item, store.Assets);
    public FormInput NewForm() => EditingId is null && Error is not null ? Input : FormInput.Blank(store.Categories, store.Priorities);

    public IActionResult OnPostSave(Guid? id)
    {
        var (ok, message) = store.SaveRecurringTicket(id, Input.ToStore(), DateTime.Now);
        if (!ok)
        {
            Error = message;
            EditingId = id;
            return Page();
        }
        Message = message;
        return RedirectToPage();
    }

    public IActionResult OnPostLogNow(Guid id)
    {
        var (ok, message, number) = store.LogRecurringTicketNow(id);
        Message = message;
        return ok && number is { } n ? RedirectToPage("/Job", new { number = n }) : RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        Message = store.DeleteRecurringTicket(id).Message;
        return RedirectToPage();
    }

    public sealed class FormInput
    {
        public string Name { get; set; } = "";
        // An unticked box posts nothing, so the ticks default to off here and Blank turns them on for a new form.
        public bool IsActive { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = TicketTypes.Request;
        public string Category { get; set; } = "";
        public string Priority { get; set; } = "";
        public Guid RequesterId { get; set; }
        public Guid? TechnicianId { get; set; }
        public string? TeamName { get; set; }
        public string? Location { get; set; }
        public string AssetTags { get; set; } = "";
        public string Frequency { get; set; } = Recurrence.Weekly;
        public int Interval { get; set; } = 1;
        public DateOnly? StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DateOnly? EndDate { get; set; }
        public TimeOnly? At { get; set; } = new(7, 30);
        public List<DayOfWeek> Days { get; set; } = [];
        public int MonthDay { get; set; } = 1;
        public int WeekOfMonth { get; set; }
        public DayOfWeek WeekDay { get; set; } = DayOfWeek.Monday;
        public int Month { get; set; } = 9;
        public bool MoveToSchoolDay { get; set; }

        public HelpdeskStore.RecurringTicketInput ToStore() => new(Name, IsActive, Title, Description, Type, Category, Priority, RequesterId,
            TechnicianId, TeamName, Location, AssetTags, Frequency, Interval, StartDate, EndDate, At, Days, MonthDay, WeekOfMonth, WeekDay, Month, MoveToSchoolDay);

        public static FormInput Blank(IReadOnlyList<string> categories, IReadOnlyList<string> priorities) => new()
        {
            IsActive = true, MoveToSchoolDay = true, Days = [DayOfWeek.Monday],
            Category = categories.FirstOrDefault() ?? "",
            Priority = priorities.Contains("Normal") ? "Normal" : priorities.FirstOrDefault() ?? "",
        };

        public static FormInput From(RecurringTicket item, IReadOnlyList<AssetRecord> assets) => new()
        {
            Name = item.Name, IsActive = item.IsActive, Title = item.Title, Description = item.Description, Type = item.Type,
            Category = item.Category, Priority = item.Priority, RequesterId = item.RequesterId, TechnicianId = item.TechnicianId,
            TeamName = item.TeamName, Location = item.Location,
            AssetTags = string.Join(", ", item.AssetIds.Select(id => assets.FirstOrDefault(a => a.Id == id)?.AssetTag).OfType<string>()),
            Frequency = item.Rule.Frequency, Interval = item.Rule.Interval, StartDate = item.Rule.StartDate, EndDate = item.Rule.EndDate, At = item.Rule.At,
            Days = item.Rule.Days.Count > 0 ? [.. item.Rule.Days] : [item.Rule.StartDate.DayOfWeek],
            MonthDay = item.Rule.MonthDay, WeekOfMonth = item.Rule.WeekOfMonth, WeekDay = item.Rule.WeekDay, Month = item.Rule.Month,
            MoveToSchoolDay = item.Rule.MoveToSchoolDay,
        };
    }
}
