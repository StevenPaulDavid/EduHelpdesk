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
    public void OnGet() { }

    public IActionResult OnPostAdd(SupplierRecord input) => Save(input with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow }, true);
    public IActionResult OnPostSave(SupplierRecord input) => Save(input, false);
    private IActionResult Save(SupplierRecord input, bool add)
    {
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
    public IActionResult OnPostDelete(Guid id) { Message = store.DeleteSupplier(id) ?? "Supplier deleted."; return RedirectToPage(); }
}
