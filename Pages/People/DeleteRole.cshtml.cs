using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class DeleteRoleModel(HelpdeskStore store) : PageModel
{
    public RoleRecord? Role { get; private set; }

    public IActionResult OnGet(string name)
    {
        Role = store.Roles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return Role is null ? NotFound() : Page();
    }

    public IActionResult OnPost(string name)
    {
        TempData["Message"] = store.DeleteRole(name);
        return RedirectToPage("/People", new { tab = "roles" });
    }
}
