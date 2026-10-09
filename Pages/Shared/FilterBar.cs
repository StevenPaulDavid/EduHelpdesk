using Microsoft.AspNetCore.Html;

namespace EduHelpdesk.Pages;

// One filter in force, shown as a chip above a list; following its link takes just that filter off.
public sealed record ActiveFilter(string Label, string? RemoveUrl);

// The search box, Filters button, chips and slide-in filter panel above a list (Pages/Shared/_FilterBar.cshtml). Every
// list uses the same one, so searching and filtering work the same way from page to page: type and press Search, or open
// Filters for the rest; what is switched on shows as chips that take one filter off each.
//
// The page supplies its own fields for the panel as a Razor template, e.g.
//     Fields = @<text><label>Technician<select name="tech">…</select></label></text>
// and the list state the form has to keep (the queue, the sort) as hidden fields.
public sealed class FilterBar
{
    // Prefixes the form's and the panel's element ids, so a page could hold more than one.
    public required string Id { get; init; }
    public string SearchName { get; init; } = "q";
    public string? Search { get; init; }
    public required string SearchPlaceholder { get; init; }
    // The panel's fields. The argument is unused - Razor templates take one.
    public required Func<object?, IHtmlContent> Fields { get; init; }
    public IReadOnlyList<ActiveFilter> Chips { get; init; } = [];
    // How many of the panel's own filters are on - the number on the Filters button. The search box isn't counted: it
    // shows for itself.
    public int PanelCount { get; init; }
    // The list with every filter off (keeping the queue or tab), for "Clear all".
    public string? ClearUrl { get; init; }
    public IReadOnlyList<(string Name, string? Value)> Hidden { get; init; } = [];
    public string Title { get; init; } = "Filters";
}
