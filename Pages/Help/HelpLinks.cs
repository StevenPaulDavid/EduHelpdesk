using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;

namespace EduHelpdesk.Pages.Help;

// Turns help Markdown into HTML with this site's addresses: article links to /Help/Article, page links through the
// app's base path, pictures from wwwroot/help/images.
public static class HelpLinks
{
    public static HelpMarkdown.Links For(IUrlHelper url) => new(
        (slug, anchor) => url.Page("/Help/Article", null, new { slug }, null, null, anchor) ?? "#",
        path => url.Content("~" + path),
        file => url.Content("~/help/images/" + file));

    public static HelpMarkdown.Result Render(IUrlHelper url, string markdown) => HelpMarkdown.Render(markdown, For(url));
}
