using System.Text.Encodings.Web;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Html;

namespace EduHelpdesk.Pages.Settings;

// One cell of the raw database pages. NULL, an empty string and a hidden secret each look different, because telling
// them apart is often the whole point of looking at raw data. A value in a column that points at another table links
// to that row.
public static class RawCell
{
    public static IHtmlContent Render(object? value, RawDatabase.Column column)
    {
        if (value is null) return new HtmlString("<span class=\"raw-null\">NULL</span>");
        if (value is RawDatabase.Hidden) return new HtmlString($"<span class=\"raw-masked\" title=\"Only an Administrator sees this value\">{RawDatabase.Masked}</span>");
        var text = RawDatabase.Text(value);
        if (text.Length == 0) return new HtmlString("<span class=\"raw-empty\">(empty text)</span>");
        var encoded = HtmlEncoder.Default.Encode(text);
        if (column.References is { } reference && reference.Split('.') is [var table, var target] && target.Length > 0)
        {
            var href = $"/Settings/DatabaseTable?name={UrlEncoder.Default.Encode(table)}&col={UrlEncoder.Default.Encode(target)}&val={UrlEncoder.Default.Encode(text)}";
            return new HtmlString($"<a class=\"raw-value\" href=\"{href}\" title=\"Open this row in {HtmlEncoder.Default.Encode(table)}\">{encoded}</a>");
        }
        return new HtmlString($"<span class=\"raw-value\">{encoded}</span>");
    }
}
