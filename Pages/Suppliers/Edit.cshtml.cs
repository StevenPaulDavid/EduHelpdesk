using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Suppliers;

public class EditModel(HelpdeskStore store) : PageModel
{
    [BindProperty]
    public SupplierRecord Supplier { get; set; } = new(Guid.Empty, "", "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);
    // A contractor the site team calls out, and their trade (Facilities → Contractors). Bound on their own: a record's
    // init-only extras aren't.
    [BindProperty] public bool IsContractor { get; set; }
    [BindProperty] public string? Trade { get; set; }
    public const int MaxTradeLength = 60;
    // Offered as suggestions; any trade can be typed.
    public static readonly string[] Trades = ["Electrician", "Plumber", "Heating engineer", "Gas engineer", "Roofer", "Builder", "Carpenter or joiner", "Glazier",
        "Locksmith", "Decorator", "Fire safety", "Water hygiene (legionella)", "Lift maintenance", "Pest control", "Grounds maintenance", "Cleaning contractor",
        "Security and CCTV", "Waste collection"];

    // One page serves adding and editing, so the convention lets in anyone with either permission and the handlers
    // sort out which of the two this actually is. See Program.cs.
    private static ModulePermission Needed(bool adding) => adding ? ModulePermission.New : ModulePermission.Edit;

    public IActionResult OnGet(Guid? id)
    {
        if (!store.UserCan(User, Modules.Suppliers, Needed(!id.HasValue))) return Forbid();
        if (!id.HasValue) return Page();
        var existing = store.Suppliers.FirstOrDefault(x => x.Id == id);
        if (existing is not null) (Supplier, IsContractor, Trade) = (existing, existing.IsContractor, existing.Trade);
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!store.UserCan(User, Modules.Suppliers, Needed(Supplier.Id == Guid.Empty))) return Forbid();
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
            Notes = Supplier.Notes?.Trim() ?? "",
            IsContractor = IsContractor,
            Trade = IsContractor ? (Trade ?? "").Trim() : ""
        };
        if (item.Trade.Length > MaxTradeLength)
        {
            ModelState.AddModelError("", $"Keep the trade to {MaxTradeLength} characters.");
            return Page();
        }
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
