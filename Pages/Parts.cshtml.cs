using System.Text;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class PartsModel(HelpdeskStore store) : PageModel
{
    public static readonly int[] PageSizes = [25, 50, 100, 200];

    // Short query-string names. "area", "action" and "page" are reserved by routing, so they are avoided.
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "category")] public string? Category { get; set; }
    [BindProperty(SupportsGet = true, Name = "location")] public string? Location { get; set; }
    [BindProperty(SupportsGet = true, Name = "supplier")] public string? Supplier { get; set; }
    [BindProperty(SupportsGet = true, Name = "flag")] public string? Flag { get; set; }
    [BindProperty(SupportsGet = true, Name = "sort")] public string Sort { get; set; } = "name";
    [BindProperty(SupportsGet = true, Name = "dir")] public string Dir { get; set; } = "asc";
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int Size { get; set; } = 50;

    public IReadOnlyList<string> Categories => store.PartCategories;
    public IReadOnlyList<string> Locations => store.PartLocations;
    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public int DefaultReorderThreshold => store.PartsDefaultReorderThreshold;
    [TempData] public string? Message { get; set; }

    // The list opens at Access, so the view asks what this role can actually do before drawing any of the buttons.
    public bool CanView => store.UserCan(User, Modules.Parts, ModulePermission.View);
    public bool CanAdd => store.UserCan(User, Modules.Parts, ModulePermission.New);
    public bool CanEdit => store.UserCan(User, Modules.Parts, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Parts, ModulePermission.Delete);

    public IReadOnlyList<PartRecord> Rows { get; private set; } = [];
    public int TotalParts { get; private set; }
    public int MatchCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public bool Descending => Dir == "desc";
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Category) || !string.IsNullOrWhiteSpace(Location)
        || !string.IsNullOrWhiteSpace(Supplier) || !string.IsNullOrWhiteSpace(Flag);

    // Which optional columns this person shows (the Columns menu).
    public ColumnSet Columns { get; private set; } = null!;

    public void OnGet()
    {
        Columns = ListColumns.For(store, User, "parts");
        Normalize();
        var matches = Run();
        TotalParts = store.Parts.Count;
        MatchCount = matches.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchCount / (double)Size));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Rows = matches.Skip((PageNumber - 1) * Size).Take(Size).ToList();
    }

    public bool IsLow(PartRecord part) => PartInsights.IsLow(part, DefaultReorderThreshold);

    public string SupplierNames(PartRecord part) =>
        string.Join(", ", part.SupplierIds.Select(id => Suppliers.FirstOrDefault(x => x.Id == id)?.Name).Where(x => x is not null));

    public IActionResult OnPostBulk(string? operation, Guid[]? ids, bool selectAll, string? newCategory, string? newLocation)
    {
        // The list itself only needs Access, so the handler on it carries its own level per operation.
        var needed = operation switch { "export" => ModulePermission.View, "delete" => ModulePermission.Delete, _ => ModulePermission.Edit };
        if (!store.UserCan(User, Modules.Parts, needed)) return Forbid();
        Normalize();
        // "Select all matching" re-applies the current filters here, so it covers every page, not just the one on screen.
        var targets = selectAll ? Run().Select(x => x.Id).ToList() : (ids ?? []).Distinct().ToList();
        if (targets.Count == 0)
        {
            Message = "Select at least one part.";
            return Back();
        }

        if (operation == "export")
        {
            var chosen = targets.ToHashSet();
            var sorted = new PartListQuery { Sort = Sort, Descending = Descending }
                .Run(store.Parts.Where(x => chosen.Contains(x.Id)), store.Suppliers, DefaultReorderThreshold);
            var csv = PartCsv.Build(sorted, store.Suppliers);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv; charset=utf-8", PartCsv.FileName(DateTime.Now));
        }

        if (operation == "delete")
        {
            var (deleted, skipped) = store.BulkDeleteParts(targets);
            Message = deleted == 0
                ? skipped > 0 ? $"None deleted: all {skipped} selected part{(skipped == 1 ? "" : "s")} are assigned to a ticket." : "None of the selected parts could be found."
                : $"{deleted} part{(deleted == 1 ? "" : "s")} deleted" + (skipped > 0 ? $", {skipped} skipped (assigned to a ticket)." : ".");
            return Back();
        }

        HelpdeskStore.PartBulkChange? change = operation switch
        {
            // "(none)" is how the form asks for the value to be cleared; an empty value would be read as "not provided".
            "category" when newCategory is not null => new(true, newCategory == PartListQuery.None ? string.Empty : newCategory, false, null),
            "location" when newLocation is not null => new(false, null, true, newLocation == PartListQuery.None ? string.Empty : newLocation),
            _ => null
        };
        if (change is null)
        {
            Message = operation switch
            {
                "category" => "Select a category.",
                "location" => "Select a location.",
                _ => "Choose what to change."
            };
            return Back();
        }

        var (updated, unchanged, error) = store.BulkUpdateParts(targets, change);
        Message = error ?? (updated == 0 && unchanged == 0
            ? "None of the selected parts could be found."
            : updated == 0
            ? $"No changes made: all {unchanged} selected part{(unchanged == 1 ? "" : "s")} already had that value."
            : $"{updated} part{(updated == 1 ? "" : "s")} updated" + (unchanged > 0 ? $", {unchanged} already had that value." : "."));
        return Back();
    }

    private List<PartRecord> Run() => BuildQuery().Run(store.Parts, store.Suppliers, DefaultReorderThreshold);

    private PartListQuery BuildQuery() => new()
    {
        Search = Search, Category = Category, Location = Location, Supplier = Supplier, Flag = Flag,
        Sort = Sort, Descending = Descending
    };

    // Ignores unknown sort columns, page sizes and flags rather than failing.
    private void Normalize()
    {
        if (!PartListQuery.SortColumns.Contains(Sort)) Sort = "name";
        Dir = string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        if (!PageSizes.Contains(Size)) Size = 50;
        if (PageNumber < 1) PageNumber = 1;
        Flag = PartListQuery.Flags.Contains(Flag?.Trim().ToLowerInvariant()) ? Flag!.Trim().ToLowerInvariant() : null;
    }

    private Dictionary<string, object?> RouteFor(int page, string sort, string dir) => new()
    {
        ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["category"] = Blank(Category), ["location"] = Blank(Location), ["supplier"] = Blank(Supplier), ["flag"] = Blank(Flag),
        ["sort"] = sort == "name" ? null : sort,
        ["dir"] = dir == "desc" ? "desc" : null,
        ["p"] = page > 1 ? page : null,
        ["size"] = Size == 50 ? null : Size
    };

    // Filters in the Filters panel that are switched on - the number on its button.
    public int PanelFilterCount => new[] { Category, Location, Supplier, Flag }.Count(x => !string.IsNullOrWhiteSpace(x));

    // Every filter in force as a chip that takes just that one off.
    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        string? Without(string key) { var route = RouteFor(1, Sort, Dir); route[key] = null; return Url.Page("/Parts", route); }
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Without("q")));
        if (!string.IsNullOrWhiteSpace(Category)) chips.Add(new($"Category: {(Category == PartListQuery.None ? "Uncategorized" : Category)}", Without("category")));
        if (!string.IsNullOrWhiteSpace(Location)) chips.Add(new($"Location: {(Location == PartListQuery.None ? "Unassigned" : Location)}", Without("location")));
        if (!string.IsNullOrWhiteSpace(Supplier))
            chips.Add(new($"Supplier: {(Supplier == "none" ? "None" : Guid.TryParse(Supplier, out var id) ? Suppliers.FirstOrDefault(x => x.Id == id)?.Name ?? "Unknown" : Supplier)}", Without("supplier")));
        if (Flag == "low") chips.Add(new("Low stock only", Without("flag")));
        return chips;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private IActionResult Back() => RedirectToPage(RouteFor(PageNumber, Sort, Dir));

    public string? SortUrl(string column) => Url.Page("/Parts", RouteFor(1, column, Sort == column && !Descending ? "desc" : "asc"));
    public string? PageUrl(int page) => Url.Page("/Parts", RouteFor(page, Sort, Dir));
    public string SortMark(string column) => Sort != column ? "" : Descending ? " ▼" : " ▲";
}
