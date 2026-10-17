using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// The checklist a recurring ticket carries (HelpdeskStore.Checks): the lines each ticket it logs asks to be filled in.
public class ChecklistModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    // A schedule on a desk outside this person's reach (RecurringDeskScope) is as good as missing.
    public RecurringTicket? Schedule => store.GetRecurringTicket(Id) is { } item
        && (store.RecurringDeskScope(User) is not { } desks || desks.Contains(store.DeskForCategory(item.Category).Id)) ? item : null;
    public bool CanOpenSettings => store.UserCan(User, Modules.Settings, ModulePermission.Access);
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    // How many assets an "every asset of a type" line would list today.
    public int AssetsOfType(string type) => store.Assets.Count(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase) && !HelpdeskStore.IsDisposed(x));
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet() => Schedule is null ? NotFound() : Page();

    public IActionResult OnPostSave(Guid? itemId, string? kind, string? label, string? min, string? max, string? unit, string? assetType)
    {
        if (Schedule is null) return NotFound();
        Message = store.SaveCheckItem(Id, itemId, new HelpdeskStore.CheckItemInput(kind, label, min, max, unit, assetType)).Message;
        return RedirectToPage(new { id = Id });
    }

    public IActionResult OnPostDelete(Guid itemId)
    {
        if (Schedule is null) return NotFound();
        Message = store.DeleteCheckItem(Id, itemId).Message;
        return RedirectToPage(new { id = Id });
    }

    public IActionResult OnPostMove(Guid itemId, int direction)
    {
        if (Schedule is null) return NotFound();
        Message = store.MoveCheckItem(Id, itemId, direction);
        return RedirectToPage(new { id = Id });
    }
}
