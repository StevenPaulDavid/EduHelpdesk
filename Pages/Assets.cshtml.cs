using System.Text;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class AssetsModel(HelpdeskStore store) : PageModel
{
    public static readonly int[] PageSizes = [25, 50, 100, 200];

    // Short query-string names. "area", "action" and "page" are reserved by routing, so they are avoided.
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public string? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "type")] public string? Type { get; set; }
    [BindProperty(SupportsGet = true, Name = "make")] public string? Make { get; set; }
    [BindProperty(SupportsGet = true, Name = "location")] public string? Location { get; set; }
    [BindProperty(SupportsGet = true, Name = "holder")] public string? Holder { get; set; }
    [BindProperty(SupportsGet = true, Name = "flag")] public string? Flag { get; set; }
    [BindProperty(SupportsGet = true, Name = "sort")] public string Sort { get; set; } = "tag";
    [BindProperty(SupportsGet = true, Name = "dir")] public string Dir { get; set; } = "asc";
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int Size { get; set; } = 50;

    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<string> AssetMakes => store.AssetMakes;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<string> Statuses => store.AssetStatuses;
    [TempData] public string? Message { get; set; }

    // The list opens at Access, so the view asks what this role can actually do before drawing any of the buttons.
    public bool CanView => store.UserCan(User, Modules.Assets, ModulePermission.View);
    public bool CanAdd => store.UserCan(User, Modules.Assets, ModulePermission.New);
    public bool CanEdit => store.UserCan(User, Modules.Assets, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Assets, ModulePermission.Delete);

    public IReadOnlyList<AssetRecord> Rows { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> UserNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, int> TicketCounts { get; private set; } = new Dictionary<Guid, int>();
    public int TotalAssets { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public DateOnly Today { get; } = AssetInsights.Today;
    public bool Descending => Dir == "desc";
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Status) || !string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(Make)
        || !string.IsNullOrWhiteSpace(Location) || !string.IsNullOrWhiteSpace(Holder) || !string.IsNullOrWhiteSpace(Flag);

    // Which optional columns this person shows (the Columns menu), and what the extra ones need.
    public ColumnSet Columns { get; private set; } = null!;
    public IReadOnlyDictionary<Guid, string> SupplierNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<string, int> Lifespans => store.AssetTypeLifespans;

    public void OnGet()
    {
        Columns = ListColumns.For(store, User, "assets");
        SupplierNames = store.Suppliers.ToDictionary(x => x.Id, x => x.Name);
        Normalize();
        var matches = Run();
        TotalAssets = store.Assets.Count;
        MatchCount = matches.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)Size));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Rows = matches.Skip((PageNumber - 1) * Size).Take(Size).ToList();
        UserNames = store.Users.ToDictionary(x => x.Id, x => x.Name);
        TicketCounts = store.Tickets.SelectMany(t => t.AssetIds.Distinct()).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
    }

    public IActionResult OnPostBulk(string? operation, Guid[]? ids, bool selectAll, string? newStatus, string? newOwner, string? newLocation)
    {
        // The list itself only needs Access, so the handlers on it carry their own level: export reads the register,
        // everything else writes to it.
        if (!store.UserCan(User, Modules.Assets, operation == "export" ? ModulePermission.View : ModulePermission.Edit)) return Forbid();
        Normalize();
        // "Select all matching" re-applies the current filters here, so it covers every page, not just the one on screen.
        var targets = selectAll ? Run().Select(x => x.Id).ToList() : (ids ?? []).Distinct().ToList();
        if (targets.Count == 0)
        {
            Message = "Select at least one asset.";
            return Back();
        }

        if (operation == "export")
        {
            var chosen = targets.ToHashSet();
            var sorted = new AssetListQuery { Sort = Sort, Descending = Descending }
                .Run(store.Assets.Where(x => chosen.Contains(x.Id)), store.Users, store.AssetTypeLifespans, store.AssetReviewDays, AssetInsights.Today);
            var csv = AssetCsv.Build(sorted, store.Users, store.Suppliers, store.AssetAttributeDefinitions, store.AssetAttributeValues, store.AssetTypeLifespans);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv; charset=utf-8", AssetCsv.FileName(DateTime.Now));
        }

        HelpdeskStore.AssetBulkChange? change = operation switch
        {
            "status" => new(newStatus, false, null, false, null),
            "owner" when string.IsNullOrWhiteSpace(newOwner) || string.Equals(newOwner, "none", StringComparison.OrdinalIgnoreCase) => new(null, true, null, false, null),
            "owner" when Guid.TryParse(newOwner, out var ownerId) => new(null, true, ownerId, false, null),
            // "(none)" is how the form asks for the location to be cleared; an empty value would be read as "not provided".
            "location" when newLocation is not null => new(null, false, null, true, newLocation == AssetListQuery.None ? string.Empty : newLocation),
            _ => null
        };
        if (change is null)
        {
            Message = operation switch
            {
                "owner" => "Select who to assign the assets to.",
                "location" => "Select a location.",
                _ => "Choose what to change."
            };
            return Back();
        }

        var (updated, unchanged, error) = store.BulkUpdateAssets(targets, change);
        Message = error ?? (updated == 0 && unchanged == 0
            ? "None of the selected assets could be found."
            : updated == 0
            ? $"No changes made: all {unchanged} selected asset{(unchanged == 1 ? "" : "s")} already had that value."
            : $"{updated} asset{(updated == 1 ? "" : "s")} updated" + (unchanged > 0 ? $", {unchanged} already had that value." : "."));
        return Back();
    }

    private List<AssetRecord> Run() => BuildQuery().Run(store.Assets, store.Users, store.AssetTypeLifespans, store.AssetReviewDays, AssetInsights.Today);

    private AssetListQuery BuildQuery() => new()
    {
        Search = Search, Status = Status, Type = Type, Make = Make, Location = Location, Holder = Holder, Flag = Flag,
        Sort = Sort, Descending = Descending
    };

    // Ignores unknown sort columns, page sizes and flags rather than failing.
    private void Normalize()
    {
        if (!AssetListQuery.SortColumns.Contains(Sort)) Sort = "tag";
        Dir = string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        if (!PageSizes.Contains(Size)) Size = 50;
        if (PageNumber < 1) PageNumber = 1;
        Flag = AssetListQuery.Flags.Contains(Flag?.Trim().ToLowerInvariant()) ? Flag!.Trim().ToLowerInvariant() : null;
    }

    private IActionResult Back() => RedirectToPage(RouteFor(PageNumber, Sort, Dir));

    private Dictionary<string, object?> RouteFor(int page, string sort, string dir) => new()
    {
        ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["status"] = Blank(Status), ["type"] = Blank(Type), ["make"] = Blank(Make), ["location"] = Blank(Location), ["holder"] = Blank(Holder), ["flag"] = Blank(Flag),
        ["sort"] = sort == "tag" ? null : sort,
        ["dir"] = dir == "desc" ? "desc" : null,
        ["p"] = page > 1 ? page : null,
        ["size"] = Size == 50 ? null : Size
    };

    // Filters in the Filters panel that are switched on - the number on its button.
    public int PanelFilterCount => new[] { Status, Type, Make, Location, Holder, Flag }.Count(x => !string.IsNullOrWhiteSpace(x));

    public static string FlagLabel(string? flag) => flag switch { "review" => "Needs review", "loan" => "On loan", "overdue" => "Loan overdue", _ => flag ?? "" };

    // Every filter in force as a chip that takes just that one off.
    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        string? Without(string key) { var route = RouteFor(1, Sort, Dir); route[key] = null; return Url.Page("/Assets", route); }
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q")));
        if (!string.IsNullOrWhiteSpace(Status)) chips.Add(new($"Status: {Status}", Without("status")));
        if (!string.IsNullOrWhiteSpace(Type)) chips.Add(new($"Type: {Type}", Without("type")));
        if (!string.IsNullOrWhiteSpace(Make)) chips.Add(new($"Make: {Make}", Without("make")));
        if (!string.IsNullOrWhiteSpace(Location)) chips.Add(new($"Location: {(Location == AssetListQuery.None ? "None" : Location)}", Without("location")));
        if (!string.IsNullOrWhiteSpace(Holder))
            chips.Add(new($"Held by: {(Holder == "none" ? "Unassigned" : Guid.TryParse(Holder, out var id) ? store.Users.FirstOrDefault(x => x.Id == id)?.Name ?? "Unknown" : Holder)}", Without("holder")));
        if (!string.IsNullOrWhiteSpace(Flag)) chips.Add(new(FlagLabel(Flag), Without("flag")));
        return chips;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public string? SortUrl(string column) => Url.Page("/Assets", RouteFor(1, column, Sort == column && !Descending ? "desc" : "asc"));
    public string? PageUrl(int page) => Url.Page("/Assets", RouteFor(page, Sort, Dir));
    public string SortMark(string column) => Sort != column ? "" : Descending ? " ▼" : " ▲";

    public IActionResult OnPostDeleteAsset(Guid id)
    {
        if (!store.UserCan(User, Modules.Assets, ModulePermission.Delete)) return Forbid();
        Message = store.DeleteAsset(id) ?? "Asset deleted.";
        return RedirectToPage();
    }
}
