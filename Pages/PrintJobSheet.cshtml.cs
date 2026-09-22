using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// A condensed job sheet for the STAR TSP100III (80mm continuous roll) - meant to be handed to a technician.
public class PrintJobSheetModel(HelpdeskStore store) : PageModel
{
    private const int DescriptionLimit = 220;

    public TicketRecord? Ticket { get; private set; }
    public UserRecord? Requester { get; private set; }
    public IReadOnlyList<AssetRecord> LinkedAssets { get; private set; } = [];
    public string ShortDescription { get; private set; } = "";

    public IActionResult OnGet(int number)
    {
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (Ticket is null) return NotFound();
        Requester = store.Users.FirstOrDefault(x => x.Id == Ticket.RequesterId);
        LinkedAssets = store.Assets.Where(x => Ticket.AssetIds.Contains(x.Id)).ToList();
        var description = Ticket.Description.Trim();
        ShortDescription = description.Length <= DescriptionLimit ? description : description[..DescriptionLimit].TrimEnd() + "…";
        return Page();
    }
}
