using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Saves which columns a list shows for the signed-in account (the Columns menu, Pages/Shared/_ColumnPicker.cshtml),
// then goes back to the list. Anyone signed in may choose for themselves; the list pages still decide what they may see.
public class ColumnsModel(HelpdeskStore store) : PageModel
{
    public IActionResult OnGet() => NotFound();

    public IActionResult OnPost(string? list, List<string>? show, bool reset, string? returnUrl)
    {
        var back = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/");
        if (list is null || !ListColumns.Lists.TryGetValue(list, out var columns)) return LocalRedirect(back);
        if (ListColumns.AccountId(User) is not { } account) return LocalRedirect(back);
        var keys = columns.Where(x => show?.Contains(x.Key, StringComparer.OrdinalIgnoreCase) == true).Select(x => x.Key).ToList();
        store.SetPreference(account, ListColumns.Key(list), reset ? null : keys.Count == 0 ? ListColumns.None : string.Join(",", keys));
        return LocalRedirect(back);
    }
}
