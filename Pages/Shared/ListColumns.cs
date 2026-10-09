using System.Security.Claims;
using EduHelpdesk.Services;

namespace EduHelpdesk.Pages;

// The columns a list can show beyond the ones it always does (the name and the row's buttons), and which each person
// has chosen. The choice is kept against their account (HelpdeskStore.Preferences), so it follows them between
// computers. Chosen with the Columns menu (Pages/Shared/_ColumnPicker.cshtml), saved by Pages/Columns.
public sealed record ListColumn(string Key, string Label, bool OnByDefault = true);

public sealed class ColumnSet(string list, IReadOnlyList<ListColumn> all, IReadOnlySet<string> shown, bool chosen)
{
    public string List { get; } = list;
    public IReadOnlyList<ListColumn> All { get; } = all;
    // Whether this person has changed the list from its default columns.
    public bool Chosen { get; } = chosen;
    public bool Show(string key) => shown.Contains(key);
    // For an empty table's single cell, which has to span what is there.
    public int ShownCount => All.Count(x => shown.Contains(x.Key));
}

public static class ListColumns
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<ListColumn>> Lists = new Dictionary<string, IReadOnlyList<ListColumn>>(StringComparer.OrdinalIgnoreCase)
    {
        ["assets"] =
        [
            new("type", "Type"), new("serial", "Serial number"), new("holder", "Held by"), new("building", "Building", false), new("location", "Room"),
            new("status", "Status"), new("warranty", "Warranty ends"), new("tickets", "Tickets"),
            new("purchased", "Purchased", false), new("price", "Purchase price", false), new("supplier", "Supplier", false),
            new("order", "Purchase order", false), new("replacement", "Replacement due", false),
            // The DfE register's columns.
            new("os", "Operating system", false), new("condition", "Condition", false), new("ownership", "Ownership", false),
            new("nextcheck", "Next check", false), new("support", "End of support", false),
        ],
        ["contracts"] =
        [
            new("supplier", "Supplier"), new("type", "Type"), new("status", "Status"), new("cost", "Cost"), new("annual", "Cost a year", false),
            new("renewal", "Next renewal"), new("notice", "Notice by"), new("end", "Ends", false), new("spend", "Spend category", false),
            new("owner", "Owner", false), new("compliance", "Compliance", false),
        ],
        ["parts"] =
        [
            new("sku", "SKU"), new("category", "Category"), new("location", "Location"), new("suppliers", "Suppliers"),
            new("quantity", "Quantity"), new("reorder", "Reorder at", false), new("types", "For asset types", false),
        ],
        ["suppliers"] =
        [
            new("contact", "Contact"), new("email", "Email"), new("phone", "Phone"), new("assets", "Assets"), new("parts", "Parts"),
            new("website", "Website", false), new("town", "Town or city", false), new("postcode", "Postcode", false),
        ],
    };

    // What the stored value means when nothing optional is to show (an empty value would mean "use the default").
    public const string None = "-";

    public static string Key(string list) => "columns:" + list.ToLowerInvariant();

    public static ColumnSet For(HelpdeskStore store, ClaimsPrincipal user, string list)
    {
        var all = Lists[list];
        var stored = AccountId(user) is { } account ? store.Preference(account, Key(list)) : null;
        if (stored is null) return new ColumnSet(list, all, all.Where(x => x.OnByDefault).Select(x => x.Key).ToHashSet(), false);
        var keys = stored == None ? [] : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new ColumnSet(list, all, all.Select(x => x.Key).Intersect(keys, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase), true);
    }

    public static Guid? AccountId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
