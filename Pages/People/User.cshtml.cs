using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.People;
public class UserModel(HelpdeskStore store) : PageModel
{
 public IReadOnlyList<string> Departments=>store.Departments; public IReadOnlyList<string> Locations=>store.Locations; public EduHelpdesk.Models.UserRecord? Person {get;private set;}
 public void OnGet(Guid? id){if(id.HasValue) Person=store.Users.FirstOrDefault(x=>x.Id==id);}
 public IActionResult OnPost(Guid? id,string name,string email,string? department,string? location){if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(email)){ModelState.AddModelError("","User name and email are required."); Person=id.HasValue?store.Users.FirstOrDefault(x=>x.Id==id):null; return Page();} var item=new EduHelpdesk.Models.UserRecord(id??Guid.NewGuid(),name.Trim(),email.Trim(),(department??"").Trim(),(location??"").Trim()); if(id.HasValue) store.UpdateUser(item); else store.AddUser(item); TempData["Message"]=id.HasValue?"User updated.":"User added."; return RedirectToPage("/People");}
}
