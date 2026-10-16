using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Desks: the parts of the school the helpdesk serves (HelpdeskStore.Desks). Categories, teams and statuses
// are placed on a desk on their own Settings pages; people on their staff account.
public class DesksModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<Desk> Desks => store.Desks;
    public CategoryStyle StyleFor(Desk desk) => store.StyleForDesk(desk);
    public IReadOnlyList<string> CategoriesOn(Desk desk) => store.CategoriesFor(desk.Id);
    public IReadOnlyList<string> TeamsOn(Desk desk) => store.TeamsFor(desk.Id);
    public int PeopleOn(Desk desk) => store.TechniciansOnDesk(desk.Id).Count(x => x.IsActive);
    public int OpenTicketsOn(Desk desk)
    {
        var lookup = store.DeskLookup();
        return store.Tickets.Count(x => !TicketInsights.IsClosed(x) && lookup(x.Category).Id == desk.Id);
    }
    [TempData] public string? Message { get; set; }

    public IActionResult OnPostAdd(string? name, string? prefix, string? helperLine, int? firstNumber)
    {
        Message = store.AddDesk(name, prefix, helperLine, firstNumber ?? 1).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostNextNumber(Guid id, int? next)
    {
        Message = next is { } value ? store.SetDeskNextNumber(id, value).Message : "Enter the number the next ticket should get.";
        return RedirectToPage();
    }

    public IActionResult OnPostSave(Guid id, string? name, string? prefix, string? helperLine, bool showInPortal)
    {
        Message = store.UpdateDesk(id, name, prefix, helperLine, showInPortal).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostStyle(Guid id, string? icon, string? color)
    {
        Message = store.SetDeskLook(id, icon, color);
        return RedirectToPage();
    }

    public IActionResult OnPostMove(Guid id, int direction)
    {
        Message = store.MoveDesk(id, direction);
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        Message = store.DeleteDesk(id).Message;
        return RedirectToPage();
    }
}
