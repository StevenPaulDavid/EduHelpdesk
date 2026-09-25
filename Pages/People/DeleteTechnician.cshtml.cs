using EduHelpdesk.Services; using Microsoft.AspNetCore.Mvc; using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.People;
public class DeleteTechnicianModel(HelpdeskStore store):PageModel { public EduHelpdesk.Models.TechnicianRecord? Technician{get;private set;} public IActionResult OnGet(Guid id){Technician=store.Technicians.FirstOrDefault(x=>x.Id==id);return Technician is null?NotFound():Page();} public IActionResult OnPost(Guid id){TempData["Message"]=store.DeleteTechnician(id)??"Technician deleted.";return RedirectToPage("/People", new { tab = "technicians" });}}
