using System.Text.Json;
using System.Text.Json.Nodes;

namespace EduHelpdesk.Services;

// Settings → EduNotify: what counts as a usable EduNotify address and API key, the shapes EduNotify's sending API
// (docs/API.md in the EduNotify project) sends back, and the JSON a send is made of. EduNotifyClient does the calling.
public static class EduNotify
{
    // EduNotify's own limits on what a caller may send.
    public const int MaxTitle = 120;
    public const int MaxBody = 4000;
    private const int LongestAddress = 500;
    private const int LongestKey = 200;
    private const string KeyPrefix = "enk_";

    // A template the key may send. Kind is "static" (goes out exactly as saved), "quickMessage" (the message text can be
    // changed) or "editable" (title and message can be changed). Emergency templates (LOCKDOWN and the like) are only
    // listed when the key is allowed them, and the helpdesk never offers them: an outage notice is not an emergency.
    public sealed record Template(string Id, string Name, string Kind, bool Emergency, bool CanChangeTitle, bool CanChangeBody, string Title, string Body);
    public sealed record Group(string Id, string Name);

    // What the key can send and to whom, from the last time EduNotify was asked.
    public sealed record Catalogue(DateTime FetchedAt, IReadOnlyList<Template> Templates, IReadOnlyList<Group> Groups)
    {
        public IReadOnlyList<Template> Sendable => Templates.Where(x => !x.Emergency).ToList();
    }

    // One notice to send. Groups are EduNotify group names; Everyone overrides them. Title and Body left blank keep the
    // template's own.
    public sealed record Send(string TemplateId, bool Everyone, IReadOnlyList<string> Groups, string? Title, string? Body, string Reference);

    // What EduNotify said about a send: its message id, and how many PCs showed it now out of how many it was meant for
    // (the rest are switched off and get it when they start).
    public sealed record Sent(long Id, int Online, int Targeted);

    // The reference a ticket's notice is sent with. EduNotify replaces a live message from the same key with the same
    // reference, so sending again from the ticket updates the notice rather than adding a second, and a recall by
    // reference takes down whatever that ticket has showing.
    public static string ReferenceFor(int ticketNumber) => $"EH-{ticketNumber}";

    // The technician ping's own reference, so it and the ticket's staff notice never replace or recall each other.
    public static string PingReferenceFor(int ticketNumber) => $"EH-{ticketNumber}-ping";

    // http or https, a machine name or address with an optional port, and nothing after it but an optional folder (for an
    // EduNotify behind a reverse proxy). Unlike the Teams webhook, a name inside the school is expected: EduNotify runs on
    // the school's own network. Only someone with Settings: Edit can set it. Kept without the trailing slash.
    public static (bool Ok, string Address, string Problem) CheckAddress(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0) return (false, "", "Enter the EduNotify server's address, e.g. http://edunotify.school.local:5080.");
        if (value.Length > LongestAddress) return (false, "", "That address is too long.");
        if (!value.Contains("://")) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            return (false, "", "That isn't a web address. Enter the address of the EduNotify console, e.g. http://edunotify.school.local:5080.");
        if (uri.UserInfo.Length > 0) return (false, "", "Leave any user name or password out of the address. The API key is entered separately.");
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0) return (false, "", "Enter just the server's address, without anything after a ? or #.");
        var path = uri.AbsolutePath.TrimEnd('/');
        // Someone pasting the API's own address (…/api/v1) means the server.
        if (path.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)) path = path[..^"/api/v1".Length];
        return (true, $"{uri.Scheme}://{uri.Authority}{path}", "");
    }

    public static bool IsHttps(string address) => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // EduNotify shows a key once, starting enk_. Anything else is a paste of the wrong thing.
    public static (bool Ok, string Key, string Problem) CheckKey(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) value = value[7..].Trim();
        if (value.Length == 0) return (false, "", "Paste the API key EduNotify showed when it was made.");
        if (!value.StartsWith(KeyPrefix, StringComparison.Ordinal) || value.Length > LongestKey || value.Any(char.IsWhiteSpace))
            return (false, "", "That isn't an EduNotify API key. Keys start enk_ and are made in EduNotify under Settings → Integrations.");
        return (true, value, "");
    }

    // The body of POST /api/v1/messages. A blank title or message is left out, so EduNotify uses the template's: an empty
    // string would replace the template's message with nothing.
    public static string SendJson(Send send)
    {
        var json = new JsonObject
        {
            ["template"] = int.TryParse(send.TemplateId, out var id) ? JsonValue.Create(id) : JsonValue.Create(send.TemplateId),
            ["recipients"] = send.Everyone
                ? new JsonObject { ["everyone"] = true }
                : new JsonObject { ["groups"] = new JsonArray(send.Groups.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) },
            ["reference"] = send.Reference
        };
        if (!string.IsNullOrWhiteSpace(send.Title)) json["title"] = send.Title.Trim();
        if (!string.IsNullOrWhiteSpace(send.Body)) json["body"] = send.Body.Trim();
        return json.ToJsonString();
    }

    // ---- Reading EduNotify's answers. Each is lenient about types (an id may be a number or a string) and returns null
    // for anything that isn't the expected shape, which the caller reports as an unexpected answer. ----

    public static IReadOnlyList<Template>? ParseTemplates(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var templates = new List<Template>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = Text(item, "id");
                var name = Text(item, "name");
                if (id.Length == 0 || name.Length == 0) continue;
                templates.Add(new(id, name, Text(item, "kind") is { Length: > 0 } kind ? kind : "static", Flag(item, "emergency"),
                    Flag(item, "canChangeTitle"), Flag(item, "canChangeBody"), Text(item, "title"), Text(item, "body")));
            }
            return templates;
        }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyList<Group>? ParseGroups(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            return document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => new Group(Text(x, "id"), Text(x, "name")))
                .Where(x => x.Name.Length > 0)
                .ToList();
        }
        catch (JsonException) { return null; }
    }

    public static Sent? ParseSent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !long.TryParse(Text(root, "id"), out var id)) return null;
            return new(id, Number(root, "online"), Number(root, "targeted"));
        }
        catch (JsonException) { return null; }
    }

    // EduNotify's own explanation of a refusal ({"error": "…"}), or "" when it didn't give one.
    public static string ParseError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Text(document.RootElement, "error") : "";
        }
        catch (JsonException) { return ""; }
    }

    // Property names are matched in any case: EduNotify sends camelCase, but nothing here should break if that changes.
    private static JsonElement? Property(JsonElement item, string name)
    {
        foreach (var property in item.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return null;
    }

    private static string Text(JsonElement item, string name) => Property(item, name) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString()?.Trim() ?? "",
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => ""
    };

    private static bool Flag(JsonElement item, string name) => Property(item, name) is { ValueKind: JsonValueKind.True };

    private static int Number(JsonElement item, string name) => Property(item, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : 0;
}
