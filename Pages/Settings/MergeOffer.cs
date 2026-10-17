using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// The bin on a list value: deletes it - or, when something still uses it, asks instead where to move everything on it
// first (_MergePrompt.cshtml, HelpdeskStore.MergeOption), rather than only saying no.
public static class MergeOffer
{
    public const string KindKey = "MergeKind", ValueKey = "MergeValue";

    public static void Delete(PageModel page, HelpdeskStore store, string kind, string value, Func<string> delete)
    {
        if (HelpdeskStore.CanMergeAway(kind, value) && store.OptionUsage(kind, value).Count > 0)
        {
            page.TempData[KindKey] = kind;
            page.TempData[ValueKey] = value;
            return;
        }
        page.TempData["Message"] = delete();
    }

    public static void Merge(PageModel page, HelpdeskStore store, string kind, string value, string? into) =>
        page.TempData["Message"] = store.MergeOption(kind, value, into).Message;
}
