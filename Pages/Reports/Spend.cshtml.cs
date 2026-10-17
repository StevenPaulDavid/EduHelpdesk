using System.Globalization;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// What jobs cost (HelpdeskStore.Costs), for the site manager and business manager: by budget code, desk, month and
// supplier, with every line below. An academic year at a time, like the finance report. Needs the Spend report tick, and
// only counts tickets this person may see (HelpdeskStore.Desks).
public class SpendReportModel(HelpdeskStore store) : PageModel
{
    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);
    [BindProperty(SupportsGet = true, Name = "year")] public int? Year { get; set; }
    [BindProperty(SupportsGet = true, Name = "desk")] public string? DeskName { get; set; }

    public sealed record Line(TicketCost Cost, string Ref, string Title, string Desk, string Supplier);
    public sealed record Group(string Name, int Lines, int Jobs, decimal Net, decimal Vat)
    {
        public decimal Gross => Net + Vat;
    }

    public IReadOnlyList<int> Years { get; private set; } = [];
    public int StartMonth { get; private set; }
    public string YearLabel => AcademicYear.Label(Year!.Value, StartMonth);
    public IReadOnlyList<Desk> Desks => store.DesksVisibleTo(User);
    public bool HasSeveralDesks => Desks.Count > 1;
    public Desk? ReportDesk { get; private set; }
    public IReadOnlyList<Line> Lines { get; private set; } = [];
    public Group Total { get; private set; } = new("Total", 0, 0, 0, 0);
    public IReadOnlyList<Group> ByCode { get; private set; } = [];
    public IReadOnlyList<Group> ByDesk { get; private set; } = [];
    public IReadOnlyList<Group> ByMonth { get; private set; } = [];
    public IReadOnlyList<Group> BySupplier { get; private set; } = [];

    public void OnGet()
    {
        StartMonth = store.AcademicYearStartMonth;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var deskOf = store.DeskLookup();
        // Not asked for, it opens on the desk switcher's desk when that is one desk (HelpdeskStore.DeskSwitch).
        if (!Request.Query.ContainsKey("desk") && store.WorkingDesks(User) is [var working]) DeskName = working.Name;
        ReportDesk = HasSeveralDesks ? Desks.FirstOrDefault(x => string.Equals(x.Name, DeskName?.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
        DeskName = ReportDesk?.Name;
        var tickets = store.TicketsFor(User).Where(t => ReportDesk is null || deskOf(t.Category).Id == ReportDesk.Id).ToDictionary(x => x.Number);
        var costs = store.TicketCosts.Where(x => tickets.ContainsKey(x.TicketNumber)).ToList();
        Years = AcademicYear.StartYearsFrom(costs.Select(x => x.Date), StartMonth, today);
        Year = Year is { } asked && Years.Contains(asked) ? asked : AcademicYear.StartYearOf(today, StartMonth);
        var suppliers = store.Suppliers.ToDictionary(x => x.Id, x => x.Name);
        Lines = costs.Where(x => AcademicYear.Contains(x.Date, Year.Value, StartMonth))
            .OrderBy(x => x.Date).ThenBy(x => x.AddedAt)
            .Select(x => new Line(x, HelpdeskStore.RefFor(deskOf(tickets[x.TicketNumber].Category), tickets[x.TicketNumber]), tickets[x.TicketNumber].Title,
                deskOf(tickets[x.TicketNumber].Category).Name, x.SupplierId is { } id ? suppliers.GetValueOrDefault(id, "") : ""))
            .ToList();
        Total = Sum("Total", Lines);
        ByCode = GroupBy(x => x.Cost.BudgetCode.Length == 0 ? "No budget code" : x.Cost.BudgetCode);
        ByDesk = HasSeveralDesks && ReportDesk is null ? GroupBy(x => x.Desk) : [];
        // Months in the order of the academic year, not by size.
        ByMonth = Lines.GroupBy(x => new DateOnly(x.Cost.Date.Year, x.Cost.Date.Month, 1)).OrderBy(g => g.Key)
            .Select(g => Sum(g.Key.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB")), g)).ToList();
        BySupplier = GroupBy(x => x.Supplier.Length == 0 ? "No supplier" : x.Supplier);
    }

    private IReadOnlyList<Group> GroupBy(Func<Line, string> key) =>
        Lines.GroupBy(key).Select(g => Sum(g.Key, g)).OrderByDescending(x => x.Gross).ThenBy(x => x.Name).ToList();

    private static Group Sum(string name, IEnumerable<Line> lines)
    {
        var list = lines.ToList();
        return new Group(name, list.Count, list.Select(x => x.Cost.TicketNumber).Distinct().Count(), list.Sum(x => x.Cost.Net), list.Sum(x => x.Cost.Vat));
    }

    public IActionResult OnGetExport()
    {
        if (!CanExport) return Forbid();
        OnGet();
        var csv = Csv.Table(["Date", "Ticket", "Title", "Desk", "Description", "Supplier", "Budget code", "Net", "VAT", "Gross"], Lines,
            x => [x.Cost.Date.ToString("yyyy-MM-dd"), x.Ref, x.Title, x.Desk, x.Cost.Description, x.Supplier, x.Cost.BudgetCode,
                  x.Cost.Net.ToString("0.00", CultureInfo.InvariantCulture), x.Cost.Vat.ToString("0.00", CultureInfo.InvariantCulture), x.Cost.Gross.ToString("0.00", CultureInfo.InvariantCulture)]);
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName("job-spend", DateTime.Now));
    }
}
