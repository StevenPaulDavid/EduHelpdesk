using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Parts;

public class EditModel(HelpdeskStore store) : PageModel
{
    [BindProperty]
    public PartRecord Part { get; set; } = new(Guid.Empty, "", "", "", 0, DateTime.UtcNow);

    public void OnGet(Guid? id)
    {
        if (!id.HasValue) return;
        var existing = store.Parts.FirstOrDefault(x => x.Id == id);
        if (existing is not null) Part = existing;
    }

    public IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(Part.Name))
        {
            ModelState.AddModelError("", "Part name is required.");
            return Page();
        }
        var item = Part with
        {
            Name = Part.Name.Trim(),
            Sku = Part.Sku?.Trim() ?? "",
            Category = Part.Category?.Trim() ?? "",
            QuantityOnHand = Math.Max(0, Part.QuantityOnHand)
        };
        if (item.Id == Guid.Empty)
        {
            store.AddPart(item with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
            TempData["Message"] = "Part added.";
        }
        else
        {
            TempData["Message"] = store.UpdatePart(item) ? "Part updated." : "Part was not found.";
        }
        return RedirectToPage("/Parts");
    }
}
