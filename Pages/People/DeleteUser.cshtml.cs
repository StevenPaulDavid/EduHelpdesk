using EduHelpdesk.Services; using Microsoft.AspNetCore.Mvc; using Microsoft.AspNetCore.Mvc.RazorPages;
namespace EduHelpdesk.Pages.People;
public class DeleteUserModel(HelpdeskStore store):PageModel { public EduHelpdesk.Models.UserRecord? Person{get;private set;} public IActionResult OnGet(Guid id){Person=store.Users.FirstOrDefault(x=>x.Id==id);return Person is null?NotFound():Page();} public IActionResult OnPost(Guid id){TempData["Message"]=store.DeleteUser(id)??"User deleted.";return RedirectToPage("/People");}}
