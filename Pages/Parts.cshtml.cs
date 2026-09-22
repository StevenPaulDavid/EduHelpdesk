using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class PartsModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<PartRecord> Parts => store.Parts;
    public int DefaultReorderThreshold => store.PartsDefaultReorderThreshold;
    [TempData] public string? Message { get; set; }
    public void OnGet() { }
    public bool IsLow(PartRecord part) => part.QuantityOnHand <= (part.ReorderThreshold ?? DefaultReorderThreshold);
}
