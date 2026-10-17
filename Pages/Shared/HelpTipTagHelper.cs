using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EduHelpdesk.Pages;

// <help-tip key="round.counted-from" /> - the ? beside a field (Services/HelpTips.cs). Clicking or tapping it opens a
// small box with the explanation and a "More in help" link (wwwroot/js/help.js); Esc or a click elsewhere closes it.
//
// It is a span with role="button", not a <button>: the tips sit inside <label>s, and a button there would become the
// label's control, taking the click and the name that belong to the input. help.js stops the click reaching the label.
// In a stacked form label (.settings-form, display:grid), wrap the label's words and the tip in one <span> so the ?
// stays beside the words: <label><span>Counted from <help-tip key="..." /></span><input ... /></label>.
[HtmlTargetElement("help-tip", TagStructure = TagStructure.WithoutEndTag)]
public sealed class HelpTipTagHelper(IUrlHelperFactory urls) : TagHelper
{
    public string Key { get; set; } = "";
    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        if (HelpTips.Find(Key) is not { } tip) { output.SuppressOutput(); return; }
        var items = ViewContext.HttpContext.Items;
        var n = items["help-tip-count"] is int count ? count + 1 : 1;
        items["help-tip-count"] = n;
        var id = $"help-tip-{n}";
        var href = urls.GetUrlHelper(ViewContext).Page("/Help/Article", null, new { slug = tip.Article }, null, null, tip.Section);
        var enc = System.Text.Encodings.Web.HtmlEncoder.Default;
        output.Content.SetHtmlContent(
            $"<span class=\"help-tip\"><span class=\"help-tip-mark\" role=\"button\" tabindex=\"0\" aria-expanded=\"false\" aria-controls=\"{id}\" aria-label=\"Help\" title=\"What's this?\">?</span>" +
            $"<span class=\"help-tip-pop\" id=\"{id}\" role=\"note\" hidden><span>{enc.Encode(tip.Text)}</span> <a href=\"{enc.Encode(href ?? "")}\">More in help</a></span></span>");
    }
}
