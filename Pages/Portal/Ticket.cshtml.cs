using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

public class TicketModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public TicketRecord? Ticket { get; private set; }
    public IReadOnlyList<TicketComment> Comments => Ticket is null ? [] : Ticket.Comments.Where(x => !x.IsInternal).OrderBy(x => x.CreatedAt).ToList();
    // Drives the note under the message box warning that replying will reopen the ticket.
    public bool IsClosed => Ticket is not null && TicketInsights.IsClosed(Ticket);
    [BindProperty] public string Comment { get; set; } = "";
    [TempData] public string? Message { get; set; }

    // Returns NotFound for both "doesn't exist" and "not yours" - a portal visitor can't tell a stranger's ticket
    // number from a made-up one.
    public IActionResult OnGet(int number)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number && x.RequesterId == id);
        return Ticket is null ? NotFound() : Page();
    }

    public IActionResult OnPostComment(int number)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number && x.RequesterId == id);
        if (ticket is null) return NotFound();
        if (string.IsNullOrWhiteSpace(Comment))
        {
            Message = "Enter a message before sending.";
            return RedirectToPage(new { number });
        }
        var (ok, reopened) = store.AddRequesterComment(number, Comment);
        if (!ok) return NotFound();
        Message = reopened
            ? "Sent - and because the ticket had been closed, it has been reopened so the team pick it up again."
            : "Sent.";
        return RedirectToPage(new { number });
    }
}
