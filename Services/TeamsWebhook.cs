using System.Text.Json;

namespace EduHelpdesk.Services;

// Settings → Teams channel: what counts as a usable webhook address, and the message Teams is sent. The address is a
// secret - whoever holds it can post to the channel - so it is only ever checked, stored and posted to here, never echoed.
public static class TeamsWebhook
{
    private const int LongestAddress = 2000;
    // A name that only means something inside the school's own network is never a Teams address.
    private static readonly string[] LocalSuffixes = [".local", ".localhost", ".internal", ".lan", ".home", ".corp", ".intranet"];

    // https only, a real internet name (not an IP address, "localhost" or a one-word machine name) and no password in it. The
    // server will post to whatever is saved here, so this keeps the page from being used to poke at the school's own
    // network. Redirects are refused as well when posting (Program.cs). The address is kept exactly as typed apart from
    // surrounding spaces: it carries a signature that must not be re-encoded.
    public static (bool Ok, string Url, string Problem) Check(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0) return (false, "", "Paste the webhook address from Teams.");
        if (value.Length > LongestAddress) return (false, "", "That address is too long to be a Teams webhook.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return (false, "", "That isn't a web address. Paste the whole link Teams gave you, starting https://.");
        if (uri.Scheme != Uri.UriSchemeHttps) return (false, "", "A Teams webhook address starts https://.");
        if (uri.UserInfo.Length > 0) return (false, "", "A Teams webhook address has no user name or password in it.");
        var host = uri.Host.ToLowerInvariant();
        if (uri.HostNameType != UriHostNameType.Dns || !host.Contains('.') || LocalSuffixes.Any(host.EndsWith))
            return (false, "", "That points at a computer inside the school rather than at Microsoft Teams, so it was not saved.");
        return (true, value, "");
    }

    // Where it posts, without the secret part of the address, so the page can say which channel's workflow it is.
    public static string HostOf(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";

    // A small Adaptive Card in the envelope both the Teams Workflows webhook and the older connector webhooks accept.
    public static string Message(HelpdeskStore.TeamsPost post)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", text = post.Heading, weight = "Bolder", size = "Medium", wrap = true }
        };
        if (!string.IsNullOrWhiteSpace(post.Text)) body.Add(new { type = "TextBlock", text = post.Text, wrap = true, spacing = "Small" });
        var card = new Dictionary<string, object>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body
        };
        if (!string.IsNullOrWhiteSpace(post.LinkUrl))
            card["actions"] = new[] { new { type = "Action.OpenUrl", title = post.LinkText ?? "Open", url = post.LinkUrl } };
        return JsonSerializer.Serialize(new
        {
            type = "message",
            attachments = new[] { new { contentType = "application/vnd.microsoft.card.adaptive", contentUrl = (string?)null, content = card } }
        });
    }
}
