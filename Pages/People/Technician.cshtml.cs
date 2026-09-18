using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.People;
public class TechnicianModel(HelpdeskStore store) : PageModel
{
 public IReadOnlyList<string> Teams=>store.TechnicianTeams; public EduHelpdesk.Models.TechnicianRecord? Technician{get;private set;}
 public void OnGet(Guid? id){if(id.HasValue) Technician=store.Technicians.FirstOrDefault(x=>x.Id==id);}
 public IActionResult OnPost(Guid? id,string name,string email,string team){if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(email)){ModelState.AddModelError("","Technician name and email are required."); Technician=id.HasValue?store.Technicians.FirstOrDefault(x=>x.Id==id):null; return Page();} var item=new EduHelpdesk.Models.TechnicianRecord(id??Guid.NewGuid(),name.Trim(),email.Trim(),team.Trim()); if(id.HasValue)store.UpdateTechnician(item);else store.AddTechnician(item);TempData["Message"]=id.HasValue?"Technician updated.":"Technician added.";return RedirectToPage("/People");}
}
