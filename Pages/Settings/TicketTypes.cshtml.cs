using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Ticket types: the school's own, each on one desk, alongside the three built in (HelpdeskStore.TicketTypes).
public class TicketTypesModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<HelpdeskStore.CustomTicketType> Custom => store.CustomTicketTypes;
    public bool HasSeveralDesks => store.HasSeveralDesks;
    public IReadOnlyList<Desk> Desks => store.Desks;
    public string DeskName(Guid id) => store.Desks.FirstOrDefault(x => x.Id == id)?.Name ?? "a desk that's switched off";
    public int Usage(string type) => store.TicketTypeUsage(type);
    // Where a removed type's tickets can go: the built-in types (not Disposal, which would mark their assets) and the
    // other types on the same desk.
    public IEnumerable<string> MoveTargets(HelpdeskStore.CustomTicketType type) =>
        new[] { TicketTypes.Incident, TicketTypes.Request }.Concat(Custom.Where(x => x.DeskId == type.DeskId && x.Name != type.Name).Select(x => x.Name));
    [TempData] public string? Message { get; set; }

    public IActionResult OnPostAdd(string? name, Guid? deskId)
    {
        Message = store.AddTicketType(name, deskId).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostRename(string? current, string? name)
    {
        Message = store.RenameTicketType(current, name).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostDesk(string? type, Guid deskId)
    {
        Message = store.SetTicketTypeDesk(type, deskId).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostMove(string? type, bool up)
    {
        Message = store.MoveTicketType(type, up).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(string? type, string? into)
    {
        Message = store.DeleteTicketType(type, into).Message;
        return RedirectToPage();
    }
}
