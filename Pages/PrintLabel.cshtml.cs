using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// A small sticky label for the STAR TSP100III (80mm continuous roll) - meant to be peeled/cut and stuck on the machine.
public class PrintLabelModel(HelpdeskStore store) : PageModel
{
    public TicketRecord? Ticket { get; private set; }

    public IActionResult OnGet(int number)
    {
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        return Ticket is null ? NotFound() : Page();
    }
}
