using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EduHelpdesk.Services;

// Parsing for the asset fields shared by the Add asset and Edit asset forms.
public static class AssetForm
{
    private static readonly string[] DateFields = ["purchaseDate", "warrantyEnd", "replacementDate", "loanDueDate"];

    // A message when one of the date fields could not be read, otherwise null. Only those fields are checked: ModelState can also
    // hold an unrelated error when the custom attribute dictionary (keyed by Guid) has no posted values and ASP.NET tries to read
    // every form field as an entry, which is harmless.
    public static string? DescribeInvalid(ModelStateDictionary state) =>
        state.Any(x => x.Value?.Errors.Count > 0 && DateFields.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            ? "Enter valid dates for the purchase, warranty, replacement and loan fields."
            : null;

    // Blank means no price. Returns false for anything that is not a non-negative amount.
    public static bool TryPrice(string? text, out decimal? price)
    {
        price = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0 || value > 100_000_000m) return false;
        price = Math.Round(value, 2);
        return true;
    }
}
