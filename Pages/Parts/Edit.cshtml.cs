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

    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> Categories => store.PartCategories;
    public IReadOnlyList<string> Locations => store.PartLocations;
    public int DefaultReorderThreshold => store.PartsDefaultReorderThreshold;

    public void OnGet(Guid? id)
    {
        if (!id.HasValue) return;
        var existing = store.Parts.FirstOrDefault(x => x.Id == id);
        if (existing is not null)
        {
            Part = existing;
            SupplierIds = existing.SupplierIds.ToArray();
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
        var validSupplierIds = store.Suppliers.Select(x => x.Id).ToHashSet();
        var item = Part with
        {
            Name = Part.Name.Trim(),
            Sku = Part.Sku?.Trim() ?? "",
            Category = Part.Category?.Trim() ?? "",
            QuantityOnHand = Math.Max(0, Part.QuantityOnHand),
            Location = Part.Location?.Trim() ?? "",
            SupplierIds = SupplierIds.Where(validSupplierIds.Contains).Distinct().ToList()
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
}
