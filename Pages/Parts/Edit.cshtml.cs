using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Parts;

// The part's page. An existing part opens read-only, with its details as plain text and the stock panel below; the
// form only appears after "Edit details" (?edit=true), so a quick look to check a bin or a SKU can't change anything by
// accident. Adding a part goes straight to the form, since there is nothing to show yet.
public class EditModel(HelpdeskStore store) : PageModel
{
    [BindProperty]
    public PartRecord Part { get; set; } = new(Guid.Empty, "", "", "", 0, DateTime.UtcNow);

    [BindProperty]
    public Guid[] SupplierIds { get; set; } = [];

    [BindProperty]
    public string[] AssetTypes { get; set; } = [];

    // The part as saved, for the read-only view and the stock panel. What the form posted can differ from it, and a
    // refused save must not make the page claim the part already says what was typed.
    public PartRecord? Stored { get; private set; }
    public bool Adding => Stored is null;
    // Showing the form rather than the read-only view.
    public bool Editing { get; private set; }

    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> Categories => store.PartCategories;
    public IReadOnlyList<string> Locations => store.PartLocations;
    public IReadOnlyList<string> AssetTypeOptions => store.AssetTypes;
    public int DefaultReorderThreshold => store.PartsDefaultReorderThreshold;
    public int ReorderAt => Stored?.ReorderThreshold ?? DefaultReorderThreshold;
    public bool IsLow => Stored is { } part && PartInsights.IsLow(part, DefaultReorderThreshold);
    public IReadOnlyList<string> SupplierNames => Stored is null ? [] :
        Suppliers.Where(x => Stored.SupplierIds.Contains(x.Id)).Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    // Newest first. Empty for a part that hasn't been saved yet.
    public IReadOnlyList<PartActivity> StockHistory => Stored?.History.OrderByDescending(x => x.CreatedAt).ToList() ?? [];
    [TempData] public string? Message { get; set; }

    // One page serves adding and editing, so the convention lets in anyone with either permission and the handlers
    // sort out which of the two this actually is. See Program.cs.
    private static ModulePermission Needed(bool adding) => adding ? ModulePermission.New : ModulePermission.Edit;

    public IActionResult OnGet(Guid? id, bool edit)
    {
        if (!store.UserCan(User, Modules.Parts, Needed(!id.HasValue))) return Forbid();
        if (!id.HasValue) { Editing = true; return Page(); }
        Stored = store.Parts.FirstOrDefault(x => x.Id == id);
        if (Stored is null) return NotFound();
        Part = Stored;
        SupplierIds = Stored.SupplierIds.ToArray();
        AssetTypes = Stored.AssetTypes.ToArray();
        Editing = edit;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!store.UserCan(User, Modules.Parts, Needed(Part.Id == Guid.Empty))) return Forbid();
        // The bound Part never carries History (no form fields map to it) and quantity is only ever changed through
        // the stock panel once a part exists, so both come from the stored record, never from what was posted here.
        Stored = Part.Id == Guid.Empty ? null : store.Parts.FirstOrDefault(x => x.Id == Part.Id);
        if (Part.Id != Guid.Empty && Stored is null) return NotFound();
        Editing = true;
        if (string.IsNullOrWhiteSpace(Part.Name))
        {
            ModelState.AddModelError("", "Part name is required.");
            return Page();
        }
        if (Part.ReorderThreshold is < 0)
        {
            ModelState.AddModelError("", "Reorder threshold cannot be negative.");
            return Page();
        }
        var validSupplierIds = store.Suppliers.Select(x => x.Id).ToHashSet();
        var validAssetTypes = store.AssetTypes;
        var item = Part with
        {
            Name = Part.Name.Trim(),
            Sku = Part.Sku?.Trim() ?? "",
            Category = Part.Category?.Trim() ?? "",
            QuantityOnHand = Stored?.QuantityOnHand ?? Math.Max(0, Part.QuantityOnHand),
            Location = Part.Location?.Trim() ?? "",
            SupplierIds = SupplierIds.Where(validSupplierIds.Contains).Distinct().ToList(),
            AssetTypes = AssetTypes.Where(x => validAssetTypes.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            History = Stored?.History ?? []
        };
        var duplicate = store.FindDuplicateSku(item.Sku, item.Id == Guid.Empty ? null : item.Id);
        var warning = duplicate is null ? "" : $" Warning: SKU {item.Sku} is also used by part {duplicate.Name}.";
        if (item.Id == Guid.Empty)
        {
            store.AddPart(item with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
            TempData["Message"] = "Part added." + warning;
            return RedirectToPage("/Parts");
        }
        // Back to the part itself, read-only again, so the saved result is what you see.
        Message = (store.UpdatePart(item) ? "Part updated." : "Part was not found.") + warning;
        return RedirectToPage(new { id = item.Id });
    }

    public IActionResult OnPostRestock(Guid id, int? delivered, string? note)
    {
        if (!store.UserCan(User, Modules.Parts, ModulePermission.Edit)) return Forbid();
        Message = delivered is null ? "Enter how many were delivered." : store.RestockPart(id, delivered.Value, note).Message;
        return RedirectToPage(new { id });
    }

    // For when the shelf and the system disagree - a stocktake, breakage, something that walked. Sets the exact count,
    // with a reason, rather than adding to it.
    public IActionResult OnPostAdjustStock(Guid id, int newQuantity, string? reason)
    {
        if (!store.UserCan(User, Modules.Parts, ModulePermission.Edit)) return Forbid();
        var error = store.AdjustPartStock(id, newQuantity, reason);
        Message = error ?? "Stock count corrected.";
        return RedirectToPage(new { id });
    }
}
