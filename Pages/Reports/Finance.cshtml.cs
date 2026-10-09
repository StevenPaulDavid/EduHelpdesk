using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Reports;

// The report finance, the Trust, the headteacher and auditors get handed: what is owned, what it cost, what arrived
// and what left, for a chosen academic year with the calendar year beside it.
// Two rules run through the whole thing. Spend only - no depreciation, because a written-down value worked out here
// would eventually disagree with the finance team's own figure and theirs is the one that counts. And nothing is
// quietly dropped: assets with no price or no date are counted and stated, because an auditor has to know a total is
// incomplete rather than discover it later.
public class FinanceReportsModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "year")] public int? Year { get; set; }

    public bool CanExport => store.UserHasFlag(User, Modules.Flags.ReportExport);

    public int StartMonth { get; private set; }
    public int StartYear { get; private set; }
    public string AcademicLabel { get; private set; } = "";
    public int CalendarYear { get; private set; }
    public DateOnly Today { get; } = AssetInsights.Today;
    public IReadOnlyList<int> AvailableYears { get; private set; } = [];

    public sealed record Group(string Name, int Count, decimal Spend, int MissingPrice);
    public sealed record OrderRow(string Reference, string Kind, DateOnly? Date, bool MixedDates, decimal Spend, string Supplier, int Count, string Bought);

    // Everything still owned: the register minus disposals.
    public IReadOnlyList<AssetRecord> Held { get; private set; } = [];
    public decimal HeldSpend { get; private set; }
    public IReadOnlyList<AssetRecord> AcademicAdditions { get; private set; } = [];
    public IReadOnlyList<AssetRecord> CalendarAdditions { get; private set; } = [];
    public IReadOnlyList<AssetRecord> AcademicDisposals { get; private set; } = [];
    public IReadOnlyList<AssetRecord> CalendarDisposals { get; private set; } = [];
    public IReadOnlyList<Group> ByType { get; private set; } = [];
    public IReadOnlyList<Group> ByAcademicYear { get; private set; } = [];
    public IReadOnlyList<Group> ByLocation { get; private set; } = [];
    public IReadOnlyList<Group> BySupplier { get; private set; } = [];
    public IReadOnlyList<OrderRow> Orders { get; private set; } = [];

    public int MissingPrice { get; private set; }
    public int MissingPurchaseDate { get; private set; }
    public int MissingReference { get; private set; }

    public void OnGet()
    {
        StartMonth = store.AcademicYearStartMonth;
        var register = store.Assets;
        var purchaseDates = register.Where(x => x.PurchaseDate is not null).Select(x => x.PurchaseDate!.Value);
        AvailableYears = AcademicYear.StartYearsFrom(purchaseDates, StartMonth, Today);
        StartYear = Year is { } chosen && AvailableYears.Contains(chosen) ? chosen : AcademicYear.StartYearOf(Today, StartMonth);
        AcademicLabel = AcademicYear.Label(StartYear, StartMonth);
        // The calendar year shown beside it is the one the academic year starts in.
        CalendarYear = StartYear;

        Held = register.Where(x => !HelpdeskStore.IsDisposed(x)).ToList();
        HeldSpend = Sum(Held);

        AcademicAdditions = register.Where(x => x.PurchaseDate is { } d && AcademicYear.Contains(d, StartYear, StartMonth))
            .OrderBy(x => x.PurchaseDate).ThenBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
        CalendarAdditions = register.Where(x => x.PurchaseDate is { } d && d.Year == CalendarYear)
            .OrderBy(x => x.PurchaseDate).ThenBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
        AcademicDisposals = register.Where(x => x.DisposalDate is { } d && AcademicYear.Contains(d, StartYear, StartMonth))
            .OrderBy(x => x.DisposalDate).ThenBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
        CalendarDisposals = register.Where(x => x.DisposalDate is { } d && d.Year == CalendarYear)
            .OrderBy(x => x.DisposalDate).ThenBy(x => x.AssetTag, NaturalComparer.Instance).ToList();

        // Holdings breakdowns describe what is owned now, so they run over Held rather than the whole register.
        ByType = Breakdown(Held, x => x.Type);
        ByLocation = Breakdown(Held, x => x.Location);
        BySupplier = Breakdown(Held, x => x.SupplierId is { } id ? store.Suppliers.FirstOrDefault(s => s.Id == id)?.Name ?? "" : "");
        ByAcademicYear = Held
            .GroupBy(x => x.PurchaseDate is { } d ? AcademicYear.LabelFor(d, StartMonth) : NotRecorded)
            .Select(g => new Group(g.Key, g.Count(), Sum(g), g.Count(x => x.PurchasePrice is null)))
            .OrderByDescending(x => x.Name == NotRecorded ? "" : x.Name, StringComparer.Ordinal).ToList();

        Orders = BuildOrders(register);

        MissingPrice = Held.Count(x => x.PurchasePrice is null);
        MissingPurchaseDate = Held.Count(x => x.PurchaseDate is null);
        MissingReference = Held.Count(x => Reference(x) is null);
    }

    public const string NotRecorded = "Not recorded";

    private static decimal Sum(IEnumerable<AssetRecord> assets) => assets.Sum(x => x.PurchasePrice ?? 0m);

    private static IReadOnlyList<Group> Breakdown(IEnumerable<AssetRecord> assets, Func<AssetRecord, string> key) => assets
        .GroupBy(x => string.IsNullOrWhiteSpace(key(x)) ? NotRecorded : key(x), StringComparer.OrdinalIgnoreCase)
        .Select(g => new Group(g.Key, g.Count(), Sum(g), g.Count(x => x.PurchasePrice is null)))
        .OrderByDescending(x => x.Spend).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

    // An order is identified by the Trust's PO where there is one, otherwise the supplier's quote. Orders placed via
    // the Trust often never come back with a PO, so the quote is all the school has to tie purchases together.
    public static (string Reference, string Kind)? Reference(AssetRecord asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.PurchaseOrder)) return (asset.PurchaseOrder.Trim(), "PO");
        if (!string.IsNullOrWhiteSpace(asset.QuoteReference)) return (asset.QuoteReference.Trim(), "Quote");
        return null;
    }

    private IReadOnlyList<OrderRow> BuildOrders(IEnumerable<AssetRecord> register) => register
        .Select(x => (Asset: x, Ref: Reference(x)))
        .Where(x => x.Ref is not null)
        .GroupBy(x => (x.Ref!.Value.Kind, x.Ref!.Value.Reference), TupleComparer)
        .Select(g =>
        {
            var assets = g.Select(x => x.Asset).ToList();
            var dates = assets.Where(x => x.PurchaseDate is not null).Select(x => x.PurchaseDate!.Value).ToList();
            var suppliers = assets.Select(x => x.SupplierId is { } id ? store.Suppliers.FirstOrDefault(s => s.Id == id)?.Name : null)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var bought = assets.GroupBy(x => string.IsNullOrWhiteSpace(x.Type) ? "Unspecified" : x.Type, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{x.Count()} × {x.Key}");
            return new OrderRow(
                g.Key.Item2,
                g.Key.Item1,
                // The order date is when it was placed, so the earliest asset date is the closest thing the system knows.
                dates.Count == 0 ? null : dates.Min(),
                dates.Distinct().Count() > 1,
                Sum(assets),
                suppliers.Count switch { 0 => "", 1 => suppliers[0]!, _ => string.Join(", ", suppliers) },
                assets.Count,
                string.Join(", ", bought));
        })
        .OrderByDescending(x => x.Date ?? DateOnly.MinValue).ThenBy(x => x.Reference, NaturalComparer.Instance).ToList();

    private static readonly IEqualityComparer<(string, string)> TupleComparer =
        EqualityComparer<(string, string)>.Create(
            (a, b) => string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase),
            x => HashCode.Combine(x.Item1.ToLowerInvariant(), x.Item2.ToLowerInvariant()));

    public string AcademicLabelFor(DateOnly? date) => date is { } d ? AcademicYear.LabelFor(d, StartMonth) : "—";
    public string SupplierName(AssetRecord asset) => asset.SupplierId is { } id ? store.Suppliers.FirstOrDefault(s => s.Id == id)?.Name ?? "" : "";
    public static string Money(decimal? value) => value is { } v ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "—";
    // The same figure for a spreadsheet: a missing amount has to be an empty cell, not a dash, or it lands in Excel as
    // text and breaks the column's sum.
    public static string MoneyCell(decimal? value) => value?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string YearLabel(int startYear) => AcademicYear.Label(startYear, StartMonth);

    public IActionResult OnGetExport(string? table)
    {
        if (!CanExport) return Forbid();
        OnGet();
        var (name, csv) = (table ?? "").ToLowerInvariant() switch
        {
            "holdings" => ("finance-holdings", Csv.Table(
                ["Breakdown", "Name", "Assets", "Spend", "Assets with no price"],
                new[] { ("By type", ByType), ("By academic year", ByAcademicYear), ("By location", ByLocation), ("By supplier", BySupplier) }
                    .SelectMany(b => b.Item2.Select(r => (b.Item1, r))),
                x => [x.Item1, x.r.Name, x.r.Count.ToString(), MoneyCell(x.r.Spend), x.r.MissingPrice.ToString()])),
            "additions" => ("finance-additions", Csv.Table(
                ["Asset tag", "Type", "Make", "Model", "Purchase date", "Academic year", "Price", "Reference", "Reference kind", "Supplier", "Status"],
                AcademicAdditions,
                x => [x.AssetTag, x.Type, x.Make, x.Model, x.PurchaseDate?.ToString("yyyy-MM-dd") ?? "", AcademicLabelFor(x.PurchaseDate),
                      MoneyCell(x.PurchasePrice), Reference(x)?.Reference ?? "", Reference(x)?.Kind ?? "", SupplierName(x), x.Status])),
            "disposals" => ("finance-disposals", Csv.Table(
                ["Asset tag", "Type", "Make", "Model", "Disposed", "Academic year", "Method", "Proceeds", "Original price", "Purchase date", "Disposed by", "Certificate"],
                AcademicDisposals,
                x => [x.AssetTag, x.Type, x.Make, x.Model, x.DisposalDate?.ToString("yyyy-MM-dd") ?? "", AcademicLabelFor(x.DisposalDate),
                      x.DisposalMethod, MoneyCell(x.DisposalProceeds), MoneyCell(x.PurchasePrice), x.PurchaseDate?.ToString("yyyy-MM-dd") ?? "", x.DisposedBy, x.DisposalCertificate])),
            "orders" => ("finance-orders", Csv.Table(
                ["Reference", "Kind", "Date", "Dates differ", "Amount", "Supplier", "Assets", "What was bought"],
                Orders,
                x => [x.Reference, x.Kind, x.Date?.ToString("yyyy-MM-dd") ?? "", x.MixedDates ? "Yes" : "No", MoneyCell(x.Spend), x.Supplier, x.Count.ToString(), x.Bought])),
            "held" => ("finance-assets-held", Csv.Table(
                ["Asset tag", "Type", "Make", "Model", "Serial number", "Location", "Purchase date", "Academic year", "Price", "Reference", "Reference kind", "Supplier"],
                Held.OrderBy(x => x.AssetTag, NaturalComparer.Instance),
                x => [x.AssetTag, x.Type, x.Make, x.Model, x.SerialNumber, x.Location, x.PurchaseDate?.ToString("yyyy-MM-dd") ?? "",
                      AcademicLabelFor(x.PurchaseDate), MoneyCell(x.PurchasePrice), Reference(x)?.Reference ?? "", Reference(x)?.Kind ?? "", SupplierName(x)])),
            _ => ("", "")
        };
        if (name.Length == 0) return NotFound();
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName(name, DateTime.Now));
    }
}
