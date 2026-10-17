using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EduHelpdesk.Services;

// The help centre's articles are written in a small, safe slice of Markdown (wwwroot/help/articles, and the school's
// own articles typed in the app). Everything is HTML-encoded first, so nothing in an article - least of all one typed
// by an admin - can add markup or script; only the shapes below come out as HTML:
//   ## Heading / ### Subheading          - an anchor id each, for the contents list and links to a section
//   paragraphs, - bullet lists, 1. numbered lists
//   > **Tip:** ... / > **Note:** ... / > **Important:** ...   - a callout box
//   | table | rows |  (the second row is the |---| separator)
//   ![What it shows](tickets-list.png)    - a screenshot from wwwroot/help/images, on a line of its own
//   **bold**, *italic*, `code`, [text](link)
// Links: another article by its name ("ticket-list" or "help:ticket-list#closing"), a page in the app ("/Jobs"), or a
// web address (https://...). Anything else - javascript:, data: - is shown as plain text.
public static partial class HelpMarkdown
{
    public sealed record Heading(int Level, string Text, string Id);
    public sealed record Result(string Html, IReadOnlyList<Heading> Headings);

    // How links and pictures become addresses: articleHref("ticket-list", "closing"), pageHref("/Jobs"), imageHref("x.png").
    public sealed record Links(Func<string, string?, string> ArticleHref, Func<string, string> PageHref, Func<string, string> ImageHref);

    public static Result Render(string markdown, Links links)
    {
        var html = new StringBuilder();
        var headings = new List<Heading>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        string? list = null;

        void CloseParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append("<p>").Append(Inline(string.Join(" ", paragraph), links)).Append("</p>\n");
            paragraph.Clear();
        }
        void CloseList()
        {
            if (list is null) return;
            html.Append("</").Append(list).Append(">\n");
            list = null;
        }
        void CloseAll() { CloseParagraph(); CloseList(); }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0) { CloseAll(); continue; }

            if (HeadingLine().Match(trimmed) is { Success: true } heading)
            {
                CloseAll();
                var level = Math.Clamp(heading.Groups[1].Length, 2, 4);
                var text = heading.Groups[2].Value.Trim();
                var id = UniqueId(Slug(text), ids);
                headings.Add(new Heading(level, text, id));
                html.Append($"<h{level} id=\"{id}\">").Append(Inline(text, links)).Append($"</h{level}>\n");
                continue;
            }
            if (ImageLine().Match(trimmed) is { Success: true } image)
            {
                CloseAll();
                var alt = image.Groups[1].Value.Trim();
                var file = image.Groups[2].Value.Trim();
                if (SafeImage().IsMatch(file) && !file.Contains(".."))
                    html.Append("<figure class=\"help-shot\"><img src=\"").Append(Attr(links.ImageHref(file))).Append("\" alt=\"").Append(Attr(alt)).Append("\" loading=\"lazy\" />")
                        .Append(alt.Length > 0 ? $"<figcaption>{Encode(alt)}</figcaption>" : "").Append("</figure>\n");
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                CloseAll();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('>'); i++) quote.Add(lines[i].TrimStart()[1..].Trim());
                i--;
                var text = string.Join(" ", quote.Where(x => x.Length > 0));
                var kind = CalloutKind().Match(text) is { Success: true } k ? k.Groups[1].Value.ToLowerInvariant() : "note";
                html.Append($"<div class=\"help-callout help-callout-{kind}\"><p>").Append(Inline(text, links)).Append("</p></div>\n");
                continue;
            }
            if (trimmed.StartsWith('|') && i + 1 < lines.Length && TableSeparator().IsMatch(lines[i + 1].Trim()))
            {
                CloseAll();
                html.Append("<div class=\"table-wrap\"><table>\n<thead><tr>");
                foreach (var cell in Cells(trimmed)) html.Append("<th>").Append(Inline(cell, links)).Append("</th>");
                html.Append("</tr></thead>\n<tbody>\n");
                for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                {
                    html.Append("<tr>");
                    foreach (var cell in Cells(lines[i].Trim())) html.Append("<td>").Append(Inline(cell, links)).Append("</td>");
                    html.Append("</tr>\n");
                }
                i--;
                html.Append("</tbody></table></div>\n");
                continue;
            }
            var bullet = BulletLine().Match(trimmed);
            var numbered = NumberedLine().Match(trimmed);
            if (bullet.Success || numbered.Success)
            {
                CloseParagraph();
                var kind = bullet.Success ? "ul" : "ol";
                if (list != kind) { CloseList(); html.Append('<').Append(kind).Append(">\n"); list = kind; }
                var text = (bullet.Success ? bullet : numbered).Groups[1].Value;
                // A following indented line carries on the same item.
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ") && lines[i + 1].Trim().Length > 0 && !BulletLine().IsMatch(lines[i + 1].Trim()) && !NumberedLine().IsMatch(lines[i + 1].Trim()))
                    text += " " + lines[++i].Trim();
                html.Append("<li>").Append(Inline(text, links)).Append("</li>\n");
                continue;
            }
            CloseList();
            paragraph.Add(trimmed);
        }
        CloseAll();
        return new Result(html.ToString(), headings);
    }

    // Plain text of an article, for search and summaries: the markup characters dropped.
    public static string PlainText(string markdown) =>
        WebUtility.HtmlDecode(StripMarks().Replace(LinkMarkup().Replace(markdown ?? "", "$1"), " "));

    // Inline markup on one block's text. Code spans are lifted out first so their contents stay literal.
    public static string Inline(string text, Links links)
    {
        var codes = new List<string>();
        text = CodeSpan().Replace(text, m => { codes.Add(m.Groups[1].Value); return $"\u0001{codes.Count - 1}\u0001"; });
        var encoded = Encode(text);
        encoded = EncodedLink().Replace(encoded, m => Link(WebUtility.HtmlDecode(m.Groups[1].Value), WebUtility.HtmlDecode(m.Groups[2].Value), links));
        encoded = Bold().Replace(encoded, "<strong>$1</strong>");
        encoded = Italic().Replace(encoded, "<em>$1</em>");
        return Placeholder().Replace(encoded, m => $"<code>{Encode(codes[int.Parse(m.Groups[1].Value)])}</code>");
    }

    private static string Link(string text, string target, HelpMarkdown.Links links)
    {
        var label = Encode(text);
        if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return $"<a href=\"{Attr(target)}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>";
        if (target.StartsWith('/') && !target.StartsWith("//") && SafePath().IsMatch(target))
            return $"<a href=\"{Attr(links.PageHref(target))}\">{label}</a>";
        var article = target.StartsWith("help:", StringComparison.OrdinalIgnoreCase) ? target[5..] : target;
        var hash = article.IndexOf('#');
        var (slug, anchor) = hash >= 0 ? (article[..hash], article[(hash + 1)..]) : (article, null);
        if (SafeSlug().IsMatch(slug) && (anchor is null || SafeSlug().IsMatch(anchor)))
            return $"<a href=\"{Attr(links.ArticleHref(slug, anchor))}\">{label}</a>";
        return label;
    }

    public static string Slug(string text)
    {
        var slug = NotSlug().Replace(WebUtility.HtmlDecode(StripMarks().Replace(text, "")).ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "section" : slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    private static string UniqueId(string id, HashSet<string> used)
    {
        var candidate = id;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{id}-{n}";
        return candidate;
    }

    private static IEnumerable<string> Cells(string row) => row.Trim().Trim('|').Split('|').Select(x => x.Trim());
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
    private static string Attr(string text) => WebUtility.HtmlEncode(text);

    [GeneratedRegex(@"^(#{1,4})\s+(.+)$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)\s]+)\)$")] private static partial Regex ImageLine();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-/]*\.(png|jpg|jpeg|gif|webp)$", RegexOptions.IgnoreCase)] private static partial Regex SafeImage();
    [GeneratedRegex(@"^\*\*(Tip|Note|Important|Warning):?\*\*", RegexOptions.IgnoreCase)] private static partial Regex CalloutKind();
    [GeneratedRegex(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$")] private static partial Regex TableSeparator();
    [GeneratedRegex(@"^[-*]\s+(.+)$")] private static partial Regex BulletLine();
    [GeneratedRegex(@"^\d+[.)]\s+(.+)$")] private static partial Regex NumberedLine();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex CodeSpan();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")] private static partial Regex EncodedLink();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)\s]+\)")] private static partial Regex LinkMarkup();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"(?<![\*\w])\*(?![\s\*])(.+?)(?<![\s\*])\*(?![\*\w])")] private static partial Regex Italic();
    [GeneratedRegex("\u0001(\\d+)\u0001")] private static partial Regex Placeholder();
    [GeneratedRegex(@"^/[A-Za-z0-9_\-/]*(\?[A-Za-z0-9_\-=&%.]*)?(#[A-Za-z0-9_\-]*)?$")] private static partial Regex SafePath();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]*$")] private static partial Regex SafeSlug();
    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NotSlug();
    [GeneratedRegex(@"[#*`>|_]+|!\[[^\]]*\]\([^)]*\)")] private static partial Regex StripMarks();
}
