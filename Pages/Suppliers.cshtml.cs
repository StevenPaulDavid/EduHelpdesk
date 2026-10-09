using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class SuppliersModel(HelpdeskStore store) : PageModel
{
    public SupplierRecord EmptySupplier => new(Guid.Empty, "", "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);
    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    [TempData] public string? Message { get; set; }

    // The list opens at Access, so the view asks what this role can actually do before drawing any of the buttons.
    public bool CanView => store.UserCan(User, Modules.Suppliers, ModulePermission.View);
    public bool CanAdd => store.UserCan(User, Modules.Suppliers, ModulePermission.New);
    public bool CanEdit => store.UserCan(User, Modules.Suppliers, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Suppliers, ModulePermission.Delete);

    // The directory as a list like the others: a search, one filter (what the supplier is used for) and columns to choose.
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "use")] public string? Use { get; set; }
    public static readonly string[] Uses = ["assets", "parts", "projects", "unused"];
    public static string UseLabel(string? use) => use switch
    {
        "assets" => "Bought assets from",
        "parts" => "Supplies parts",
        "projects" => "Asked to quote on a project",
        "unused" => "Not used anywhere yet",
        _ => use ?? ""
    };

    public ColumnSet Columns { get; private set; } = null!;
    public IReadOnlyList<SupplierRecord> Rows { get; private set; } = [];
    public IReadOnlyDictionary<Guid, int> AssetCounts { get; private set; } = new Dictionary<Guid, int>();
    public IReadOnlyDictionary<Guid, int> PartCounts { get; private set; } = new Dictionary<Guid, int>();
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Use);
    public int PanelFilterCount => string.IsNullOrWhiteSpace(Use) ? 0 : 1;

    public void OnGet()
    {
        Columns = ListColumns.For(store, User, "suppliers");
        if (!Uses.Contains(Use)) Use = null;
        AssetCounts = store.Assets.Where(x => x.SupplierId is not null).GroupBy(x => x.SupplierId!.Value).ToDictionary(g => g.Key, g => g.Count());
        PartCounts = store.Parts.SelectMany(x => x.SupplierIds.Distinct()).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var quoting = store.Projects.SelectMany(p => p.Items).SelectMany(i => i.Suppliers).Select(s => s.SupplierId).ToHashSet();

        IEnumerable<SupplierRecord> query = store.Suppliers;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(s => terms.All(t => new[] { s.Name, s.ContactName, s.Email, s.Phone, s.City, s.PostalCode, s.Website, s.Notes }
                .Any(x => (x ?? "").Contains(t, StringComparison.OrdinalIgnoreCase))));
        }
        query = Use switch
        {
            "assets" => query.Where(s => AssetCounts.ContainsKey(s.Id)),
            "parts" => query.Where(s => PartCounts.ContainsKey(s.Id)),
            "projects" => query.Where(s => quoting.Contains(s.Id)),
            "unused" => query.Where(s => !AssetCounts.ContainsKey(s.Id) && !PartCounts.ContainsKey(s.Id) && !quoting.Contains(s.Id)),
            _ => query
        };
        Rows = query.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Every filter in force as a chip that takes just that one off.
    public IReadOnlyList<ActiveFilter> ActiveFilters()
    {
        var chips = new List<ActiveFilter>();
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new($"Search: “{Search.Trim()}”", Url.Page("/Suppliers", new { use = Use })));
        if (!string.IsNullOrWhiteSpace(Use)) chips.Add(new(UseLabel(Use), Url.Page("/Suppliers", new { q = string.IsNullOrWhiteSpace(Search) ? null : Search })));
        return chips;
    }

    public IActionResult OnPostAdd(SupplierRecord input) => Save(input with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow }, true);
    public IActionResult OnPostSave(SupplierRecord input) => Save(input, false);
    // The list itself only needs Access - the add/edit/delete forms on it carry their own permission, and adding is a
    // different permission from changing.
    private IActionResult Save(SupplierRecord input, bool add)
    {
        if (!store.UserCan(User, Modules.Suppliers, add ? ModulePermission.New : ModulePermission.Edit)) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Name)) { Message = "Supplier name is required."; return RedirectToPage(); }
        if (!string.IsNullOrWhiteSpace(input.Email))
        {
            try { if (!new System.Net.Mail.MailAddress(input.Email.Trim()).Address.Equals(input.Email.Trim(), StringComparison.OrdinalIgnoreCase)) throw new FormatException(); }
            catch (FormatException) { Message = "Enter a valid supplier email."; return RedirectToPage(); }
        }
        var item = input with { Name = input.Name.Trim(), ContactName = input.ContactName?.Trim() ?? "", Email = input.Email?.Trim() ?? "", Phone = input.Phone?.Trim() ?? "", AddressLine1 = input.AddressLine1?.Trim() ?? "", AddressLine2 = input.AddressLine2?.Trim() ?? "", City = input.City?.Trim() ?? "", StateRegion = input.StateRegion?.Trim() ?? "", PostalCode = input.PostalCode?.Trim() ?? "", Country = input.Country?.Trim() ?? "", Website = input.Website?.Trim() ?? "", Notes = input.Notes?.Trim() ?? "" };
        if (add) { store.AddSupplier(item); Message = "Supplier added."; } else Message = store.UpdateSupplier(item) ? "Supplier updated." : "Supplier was not found.";
        return RedirectToPage();
    }
    public IActionResult OnPostDelete(Guid id)
    {
        if (!store.UserCan(User, Modules.Suppliers, ModulePermission.Delete)) return Forbid();
        Message = store.DeleteSupplier(id) ?? "Supplier deleted.";
        return RedirectToPage();
    }
}
