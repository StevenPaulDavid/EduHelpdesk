using EduHelpdesk.Services; using Microsoft.AspNetCore.Mvc; using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.Suppliers;
public class DeleteModel(HelpdeskStore store):PageModel { public EduHelpdesk.Models.SupplierRecord? Supplier{get;private set;} public IActionResult OnGet(Guid id){Supplier=store.Suppliers.FirstOrDefault(x=>x.Id==id);return Supplier is null?NotFound():Page();}public IActionResult OnPost(Guid id){TempData["Message"]=store.DeleteSupplier(id)??"Supplier deleted.";return RedirectToPage("/Suppliers");}}
