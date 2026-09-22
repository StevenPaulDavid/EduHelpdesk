using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Loans;

public class IssueModel(HelpdeskStore store) : PageModel
{
    public LoanKit? Kit { get; private set; }
    public IReadOnlyList<UserRecord> Users => store.Users.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<string> Reasons => store.LoanReasons;
    // Same day by default; the technician can push it out when someone needs it for longer.
    public DateOnly DefaultDueBack { get; } = AssetInsights.Today;
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(Guid id)
    {
        Kit = store.LoanKits.FirstOrDefault(x => x.Id == id);
        if (Kit is null) { TempData["Message"] = "Loan kit was not found."; return RedirectToPage("/Loans"); }
        if (store.KitLoans.Any(x => x.KitId == id && x.ReturnedAt is null))
        {
            TempData["Message"] = $"{Kit.Name} is already out on loan.";
            return RedirectToPage("/Loans");
        }
        return Page();
    }

    public IActionResult OnPost(Guid id, Guid? borrowerUserId, string? borrowerName, string? reason, DateOnly? dueBack, string? notes)
    {
        Kit = store.LoanKits.FirstOrDefault(x => x.Id == id);
        if (Kit is null) { TempData["Message"] = "Loan kit was not found."; return RedirectToPage("/Loans"); }

        var (ok, message) = store.IssueKit(id, borrowerUserId, borrowerName, reason, dueBack ?? DefaultDueBack, IssuedBy(), notes);
        if (ok)
        {
            TempData["Message"] = message;
            return RedirectToPage("/Loans");
        }
        ModelState.AddModelError("", message);
        return Page();
    }

    // Loans record who handed the kit over - useful at the desk, and unrelated to the generic audit log,
    // which deliberately records what changed rather than who.
    private string IssuedBy() => User.Identity?.Name ?? string.Empty;
}
