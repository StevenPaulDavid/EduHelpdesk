using Microsoft.AspNetCore.Html;

namespace EduHelpdesk.Pages;

// The few line icons the pages use, drawn inline so they take the text colour (currentColor), need no extra request,
// and are allowed by the content security policy. Each is decorative (aria-hidden): the button or link around it
// carries the name through aria-label or title. Before these, buttons used text characters - ♜ for delete, ✎ for
// edit - which read oddly and announced as "black chess rook" to a screen reader.
//
// In a page: <button class="icon-button" aria-label="Remove">@Icons.Bin</button>, or @Icons.Plus before a label.
public static class Icons
{
    static IHtmlContent Svg(string paths) => new HtmlString(
        $"<svg class=\"ico\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\">{paths}</svg>");

    public static readonly IHtmlContent Bin = Svg("<path d=\"M4 7h16M10 11v6M14 11v6M9 7V4h6v3M6 7l1 13h10l1-13\"/>");
    public static readonly IHtmlContent Edit = Svg("<path d=\"M4 20h4L19 9l-4-4L4 16v4Z\"/><path d=\"m13.5 6.5 4 4\"/>");
    public static readonly IHtmlContent Check = Svg("<path d=\"m5 12 5 5 9-10\"/>");
    public static readonly IHtmlContent Plus = Svg("<path d=\"M12 5v14M5 12h14\"/>");
    public static readonly IHtmlContent Close = Svg("<path d=\"M6 6l12 12M18 6 6 18\"/>");
    public static readonly IHtmlContent Upload = Svg("<path d=\"M12 16V4M7 9l5-5 5 5M4 20h16\"/>");
    public static readonly IHtmlContent File = Svg("<path d=\"M14 3H6v18h12V7l-4-4Z\"/><path d=\"M14 3v4h4\"/>");
    public static readonly IHtmlContent Filter = Svg("<path d=\"M3 5h18M6 12h12M10 19h4\"/>");
    public static readonly IHtmlContent Columns = Svg("<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M9 4v16M15 4v16\"/>");
    public static readonly IHtmlContent Search = Svg("<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-3.5-3.5\"/>");
}
