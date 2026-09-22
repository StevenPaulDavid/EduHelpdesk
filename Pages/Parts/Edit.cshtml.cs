using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Parts;

public class EditModel(HelpdeskStore store) : PageModel
{
    [BindProperty]
    public PartRecord Part { get; set; } = new(Guid.Empty, "", "", "", 0, DateTime.UtcNow);

    [BindProperty]
    public Guid[] SupplierIds { get; set; } = [];

    [BindProperty]
    public string[] AssetTypes { get; set; } = [];

    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> Categories => store.PartCategories;
    public IReadOnlyList<string> Locations => store.PartLocations;
    public IReadOnlyList<string> AssetTypeOptions => store.AssetTypes;
    public int DefaultReorderThreshold => store.PartsDefaultReorderThreshold;
    // Newest first. Empty for a part that hasn't been saved yet.
    public IReadOnlyList<PartActivity> StockHistory => Part.History.OrderByDescending(x => x.CreatedAt).ToList();
    [TempData] public string? Message { get; set; }

    public void OnGet(Guid? id)
    {
        if (!id.HasValue) return;
        var existing = store.Parts.FirstOrDefault(x => x.Id == id);
        if (existing is not null)
        {
            Part = existing;
            SupplierIds = existing.SupplierIds.ToArray();
            AssetTypes = existing.AssetTypes.ToArray();
        }
    }

    public IActionResult OnPost()
    {
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
        // The bound Part never carries History (no form fields map to it) and quantity is only ever changed through
        // AdjustStock once a part exists, so both come from the stored record, never from what was posted here.
        var existing = Part.Id == Guid.Empty ? null : store.Parts.FirstOrDefault(x => x.Id == Part.Id);
        var validSupplierIds = store.Suppliers.Select(x => x.Id).ToHashSet();
        var validAssetTypes = store.AssetTypes;
        var item = Part with
        {
            Name = Part.Name.Trim(),
            Sku = Part.Sku?.Trim() ?? "",
            Category = Part.Category?.Trim() ?? "",
            QuantityOnHand = existing?.QuantityOnHand ?? Math.Max(0, Part.QuantityOnHand),
            Location = Part.Location?.Trim() ?? "",
            SupplierIds = SupplierIds.Where(validSupplierIds.Contains).Distinct().ToList(),
            AssetTypes = AssetTypes.Where(x => validAssetTypes.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            History = existing?.History ?? []
        };
        var duplicate = store.FindDuplicateSku(item.Sku, item.Id == Guid.Empty ? null : item.Id);
        var warning = duplicate is null ? "" : $" Warning: SKU {item.Sku} is also used by part {duplicate.Name}.";
        if (item.Id == Guid.Empty)
        {
            store.AddPart(item with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
            TempData["Message"] = "Part added." + warning;
        }
        else
        {
            TempData["Message"] = (store.UpdatePart(item) ? "Part updated." : "Part was not found.") + warning;
        }
        return RedirectToPage("/Parts");
    }

    public IActionResult OnPostAdjustStock(Guid id, int newQuantity, string? reason)
    {
        var error = store.AdjustPartStock(id, newQuantity, reason);
        Message = error ?? "Stock adjusted.";
        return RedirectToPage(new { id });
    }
}
