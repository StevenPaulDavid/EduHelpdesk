using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Suppliers;

public class EditModel(HelpdeskStore store) : PageModel
{
    [BindProperty]
    public SupplierRecord Supplier { get; set; } = new(Guid.Empty, "", "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);

    public void OnGet(Guid? id)
    {
        if (!id.HasValue) return;
        var existing = store.Suppliers.FirstOrDefault(x => x.Id == id);
        if (existing is not null) Supplier = existing;
    }

    public IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(Supplier.Name))
        {
            ModelState.AddModelError("", "Supplier name is required.");
            return Page();
        }
        var item = Supplier with
        {
            Name = Supplier.Name.Trim(),
            ContactName = Supplier.ContactName?.Trim() ?? "",
            Email = Supplier.Email?.Trim() ?? "",
            Phone = Supplier.Phone?.Trim() ?? "",
            AddressLine1 = Supplier.AddressLine1?.Trim() ?? "",
            AddressLine2 = Supplier.AddressLine2?.Trim() ?? "",
            City = Supplier.City?.Trim() ?? "",
            StateRegion = Supplier.StateRegion?.Trim() ?? "",
            PostalCode = Supplier.PostalCode?.Trim() ?? "",
            Country = Supplier.Country?.Trim() ?? "",
            Website = Supplier.Website?.Trim() ?? "",
            Notes = Supplier.Notes?.Trim() ?? ""
        };
        if (item.Id == Guid.Empty)
        {
            store.AddSupplier(item with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
            TempData["Message"] = "Supplier added.";
        }
        else
        {
            TempData["Message"] = store.UpdateSupplier(item) ? "Supplier updated." : "Supplier was not found.";
        }
        return RedirectToPage("/Suppliers");
    }
}
