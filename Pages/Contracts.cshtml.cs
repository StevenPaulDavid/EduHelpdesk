using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The DfE digital technology contracts register as a list: what is due (renewals, notice dates, endings), what it all
// costs a year, and a search and filters like every other list. Opens at Contracts: Access; each contract needs View.
public class ContractsModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "show")] public string? Show { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public string? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "type")] public string? Type { get; set; }
    [BindProperty(SupportsGet = true, Name = "spend")] public string? Spend { get; set; }
    [BindProperty(SupportsGet = true, Name = "supplier")] public Guid? SupplierId { get; set; }
    [TempData] public string? Message { get; set; }

    public static readonly string[] Shows = ["attention", "renewal", "notice", "ending", "live", "ended", "apps", "personal", "related"];
    public static string ShowLabel(string? show) => show switch
    {
        "attention" => "Needs attention",
        "renewal" => "Renewing in the next 3 months",
        "notice" => "Notice date in the next month",
        "ending" => "Ending in the next month, or past its end",
        "live" => "Live (not expired)",
        "ended" => "Expired",
        "apps" => "Approved applications",
        "personal" => "Processes personal data",
        "related" => "Related party",
        _ => show ?? ""
    };

    public bool CanView => store.UserCan(User, Modules.Contracts, ModulePermission.View);
    public bool CanAdd => store.UserCan(User, Modules.Contracts, ModulePermission.New);
    public bool CanEdit => store.UserCan(User, Modules.Contracts, ModulePermission.Edit);

    public DateOnly Today { get; } = AssetInsights.Today;
    public IReadOnlyList<ContractRecord> All { get; private set; } = [];
    public IReadOnlyList<ContractRecord> Rows { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> SupplierNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyList<SupplierRecord> Suppliers { get; private set; } = [];
    public IReadOnlyList<string> Statuses => store.ContractStatuses;
    public IReadOnlyList<string> Types => store.ContractTypes;
    public IReadOnlyList<string> SpendCategories => store.SpendCategories;
    public ColumnSet Columns { get; private set; } = null!;

    // The figures across the top, for the whole register whatever the filters.
    public int LiveCount { get; private set; }
    public decimal AnnualSpend { get; private set; }
    public int UnpricedLive { get; private set; }
    public int RenewingSoon { get; private set; }
    public int NoticeSoon { get; private set; }
    public int NeedAttention { get; private set; }

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || PanelFilterCount > 0;
    public int PanelFilterCount => new[] { Show, Status, Type, Spend }.Count(x => !string.IsNullOrWhiteSpace(x)) + (SupplierId.HasValue ? 1 : 0);

    public void OnGet()
    {
        Columns = ListColumns.For(store, User, "contracts");
        Load();
    }

    // The rows as they are filtered, for the auditor's spreadsheet. Costs are on it, so it needs View, as a contract does.
    public IActionResult OnGetExport()
    {
        if (!CanView) return Forbid();
        Load();
        var csv = Csv.Table(
            ["Name of contract or service", "Description", "Contract type", "Spend category", "Supplier", "Supplier contact", "Cost (£)", "Cost period",
             "Annual cost (£)", "Cost notes", "Duration", "Renewal", "Start date", "End date", "Next renewal date", "Notice (months)", "Notice date",
             "Contract owner", "Procurement approach", "Status", "Approved app", "Processes personal data", "Related party", "Reported to DfE on", "Additional comments"],
            Rows,
            x => [x.Name, x.Description, x.ContractType, x.SpendCategory, SupplierName(x), x.SupplierContact, Money(x.Cost), x.CostPeriod,
                  Money(ContractRules.AnnualCost(x)), x.CostNotes, x.Duration, x.RenewalType, Day(x.StartDate), Day(x.EndDate), Day(x.NextRenewalDate),
                  x.NoticeMonths?.ToString() ?? "", Day(ContractRules.NoticeDate(x)), x.ContractOwner, x.ProcurementApproach, x.Status,
                  Yn(x.ApprovedApp), Yn(x.ProcessesPersonalData), Yn(x.RelatedParty), Day(x.RelatedPartyReportedOn), x.Notes]);
        return File(Csv.ToBytes(csv), Csv.ContentType, Csv.FileName("contracts-register", DateTime.Now));
        static string Money(decimal? value) => value?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        static string Day(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "";
        static string Yn(bool value) => value ? "Yes" : "No";
    }

    private void Load()
    {
        All = store.Contracts;
        Suppliers = store.Suppliers.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SupplierNames = store.Suppliers.ToDictionary(x => x.Id, x => x.Name);
        if (!Shows.Contains(Show)) Show = null;

        var live = All.Where(ContractRules.IsLive).ToList();
        LiveCount = live.Count;
        AnnualSpend = live.Sum(x => ContractRules.AnnualCost(x) ?? 0m);
        UnpricedLive = live.Count(x => x.Cost is null);
        RenewingSoon = live.Count(x => x.NextRenewalDate is { } d && d >= Today && d <= Today.AddMonths(3));
        NoticeSoon = live.Count(x => ContractRules.NoticeDate(x) is { } d && d >= Today && d <= Today.AddMonths(1));
        NeedAttention = All.Count(x => ContractRules.Flags(x, Today).Any(f => f.Tone.Length > 0));

        IEnumerable<ContractRecord> query = All;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(x => terms.All(t => new[] { x.Name, x.Description, x.ContractType, x.SpendCategory, SupplierName(x), x.SupplierContact, x.ContractOwner, x.Notes, x.CostNotes }
                .Any(v => (v ?? "").Contains(t, StringComparison.OrdinalIgnoreCase))));
        }
        if (!string.IsNullOrWhiteSpace(Status)) query = query.Where(x => string.Equals(x.Status, Status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(Type)) query = query.Where(x => string.Equals(x.ContractType, Type, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(Spend)) query = query.Where(x => string.Equals(x.SpendCategory, Spend, StringComparison.OrdinalIgnoreCase));
        if (SupplierId is { } supplier) query = query.Where(x => x.SupplierId == supplier);
        query = Show switch
        {
            "attention" => query.Where(x => ContractRules.Flags(x, Today).Any(f => f.Tone.Length > 0)),
            "renewal" => query.Where(x => ContractRules.IsLive(x) && x.NextRenewalDate is { } d && d >= Today && d <= Today.AddMonths(3)),
            "notice" => query.Where(x => ContractRules.IsLive(x) && ContractRules.NoticeDate(x) is { } d && d >= Today && d <= Today.AddMonths(1)),
            "ending" => query.Where(x => ContractRules.End(x, Today) is not null),
            "live" => query.Where(ContractRules.IsLive),
            "ended" => query.Where(x => !ContractRules.IsLive(x)),
            "apps" => query.Where(x => x.ApprovedApp),
            "personal" => query.Where(x => x.ProcessesPersonalData),
            "related" => query.Where(x => x.RelatedParty),
            _ => query
        };
        // Live first, then whatever needs acting on soonest, then by name.
        Rows = query.OrderBy(x => ContractRules.IsLive(x) ? 0 : 1)
            .ThenBy(x => ContractRules.Flags(x, Today).Select(f => (DateOnly?)f.Date).FirstOrDefault() ?? DateOnly.MaxValue)
            .ThenBy(x => x.Name, NaturalComparer.Instance).ToList();
    }

    public string SupplierName(ContractRecord x) => x.SupplierId is { } id ? SupplierNames.GetValueOrDefault(id, "") : "";

    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        string? Without(string key)
        {
            var route = Route();
            route[key] = null;
            return Url.Page("/Contracts", route);
        }
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q")));
        if (!string.IsNullOrWhiteSpace(Show)) chips.Add(new(ShowLabel(Show), Without("show")));
        if (!string.IsNullOrWhiteSpace(Status)) chips.Add(new($"Status: {Status}", Without("status")));
        if (!string.IsNullOrWhiteSpace(Type)) chips.Add(new($"Type: {Type}", Without("type")));
        if (!string.IsNullOrWhiteSpace(Spend)) chips.Add(new($"Spend: {Spend}", Without("spend")));
        if (SupplierId is { } supplier) chips.Add(new($"Supplier: {SupplierNames.GetValueOrDefault(supplier, "Unknown")}", Without("supplier")));
        return chips;
    }

    public Dictionary<string, object?> Route() => new()
    {
        ["q"] = Blank(Search), ["show"] = Blank(Show), ["status"] = Blank(Status), ["type"] = Blank(Type), ["spend"] = Blank(Spend), ["supplier"] = SupplierId
    };
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
