using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Parts;

public class DeleteModel(HelpdeskStore store) : PageModel
{
    public PartRecord? Part { get; private set; }

    public IActionResult OnGet(Guid id)
    {
        Part = store.Parts.FirstOrDefault(x => x.Id == id);
        return Part is null ? NotFound() : Page();
    }

    public IActionResult OnPost(Guid id)
    {
        TempData["Message"] = store.DeletePart(id) ?? "Part deleted.";
        return RedirectToPage("/Parts");
    }
}
