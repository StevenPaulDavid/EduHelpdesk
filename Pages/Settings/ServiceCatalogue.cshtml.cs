using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

public class ServiceCatalogueModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<ServiceItem> Items => store.ServiceItems;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Priorities => store.Priorities;

    public CategoryStyle StyleFor(string category) => store.StyleFor(category);
    // What staff see in a category, in the order they see it: shown items and shown templates together.
    public IReadOnlyList<HelpdeskStore.PortalChoice> ButtonsFor(string category) => store.PortalChoices(category);
    public IEnumerable<ServiceItem> HiddenItems(string category) => Items.Where(x => !x.ShowInPortal && string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
    public ServiceItem? ItemFor(HelpdeskStore.PortalChoice choice) => choice.Kind == HelpdeskStore.PortalChoice.ItemKind ? store.FindServiceItem(choice.Category, choice.Key) : null;
    // Categories worth a section in the catalogue list: anything with an item or a shown template.
    public IEnumerable<string> ListedCategories => Categories.Where(c => Items.Any(i => string.Equals(i.Category, c, StringComparison.OrdinalIgnoreCase)) || ButtonsFor(c).Count > 0);

    // Adds an item, or saves changes to one when an id is given.
    public IActionResult OnPostSave(Guid? id, string? category, string? name, string? priority, string? helperLine, bool showInPortal)
    {
        TempData["Message"] = id is { } existing
            ? store.UpdateServiceItem(existing, category, name, priority, helperLine, showInPortal)
            : store.AddServiceItem(category, name, priority, helperLine, showInPortal);
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        TempData["Message"] = store.DeleteServiceItem(id);
        return RedirectToPage();
    }

    public IActionResult OnPostStyle(string? category, string? icon, string? color)
    {
        TempData["Message"] = store.SetCategoryStyle(category, icon, color);
        return RedirectToPage();
    }

    // Direction is -1 for up and 1 for down. Moving something already at the end of the list is not an error.
    public IActionResult OnPostMoveCategory(string? category, int direction)
    {
        if (store.MoveCategory(category, direction) is { } problem) TempData["Message"] = problem;
        return RedirectToPage(null, null, new { }, category is null ? null : "tile-" + Slug(category));
    }

    public IActionResult OnPostMoveChoice(string? category, string? kind, string? key, int direction)
    {
        if (store.MovePortalChoice(category, kind, key, direction) is { } problem) TempData["Message"] = problem;
        return RedirectToPage(null, null, new { }, category is null ? null : "cat-" + Slug(category));
    }

    // An id the browser can jump to, so the page stays where the person was working after a move.
    public static string Slug(string value) => new string(value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
}
