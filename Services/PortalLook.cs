using Microsoft.AspNetCore.Html;

namespace EduHelpdesk.Services;

// The ready-made icons and colours a category tile can have in the staff portal. A fixed set on purpose: what is stored
// is only a key, so nothing a person types ever reaches the page as markup or CSS, and every tile looks like it belongs.
public static class PortalLook
{
    public sealed record IconChoice(string Key, string Label, string Shapes);
    public sealed record ColorChoice(string Key, string Label, string Hex);

    public const string DefaultIcon = "folder";
    public const string DefaultColor = "teal";

    // 24x24 line icons, drawn with a round 1.8 stroke in the tile's colour (see Svg).
    public static readonly IReadOnlyList<IconChoice> Icons =
    [
        new("laptop", "Laptop", "<rect x=\"5\" y=\"5\" width=\"14\" height=\"10\" rx=\"1.5\"/><path d=\"M2 19h20\"/>"),
        new("monitor", "Computer", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"12\" rx=\"2\"/><path d=\"M8 20h8M12 16v4\"/>"),
        new("tablet", "Tablet or phone", "<rect x=\"6\" y=\"2.5\" width=\"12\" height=\"19\" rx=\"2.5\"/><path d=\"M11 18h2\"/>"),
        new("projector", "Projector", "<rect x=\"3\" y=\"8\" width=\"18\" height=\"8\" rx=\"2\"/><circle cx=\"8\" cy=\"12\" r=\"2\"/><path d=\"M14 11h4M14 13h4M6 16v2M18 16v2\"/>"),
        new("whiteboard", "Whiteboard", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"12\" rx=\"1.5\"/><path d=\"M7 12l3-3 3 3 4-4M8 20l2-4M16 20l-2-4\"/>"),
        new("speaker", "Sound", "<path d=\"M4 9h4l5-4v14l-5-4H4z\"/><path d=\"M16.5 8.5a5 5 0 0 1 0 7M19 6a8.5 8.5 0 0 1 0 12\"/>"),
        new("camera", "Camera", "<path d=\"M4 8h3l2-3h6l2 3h3v11H4z\"/><circle cx=\"12\" cy=\"13\" r=\"3.5\"/>"),
        new("printer", "Printer", "<path d=\"M7 9V4h10v5\"/><rect x=\"4\" y=\"9\" width=\"16\" height=\"8\" rx=\"2\"/><path d=\"M7 14h10v6H7z\"/>"),
        new("keyboard", "Keyboard", "<rect x=\"2\" y=\"6\" width=\"20\" height=\"12\" rx=\"2\"/><path d=\"M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10\"/>"),
        new("mouse", "Mouse", "<rect x=\"7\" y=\"3\" width=\"10\" height=\"18\" rx=\"5\"/><path d=\"M12 3v6\"/>"),
        new("wifi", "Wi-Fi", "<path d=\"M2 9a15 15 0 0 1 20 0M5 12.5a10 10 0 0 1 14 0M8.5 16a5 5 0 0 1 7 0\"/><circle cx=\"12\" cy=\"19.5\" r=\"1\"/>"),
        new("globe", "Internet", "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18\"/>"),
        new("app", "Software", "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M3 9h18M7 6.5h.01M10 6.5h.01\"/>"),
        new("lock", "Account or login", "<rect x=\"5\" y=\"11\" width=\"14\" height=\"9\" rx=\"2\"/><path d=\"M8 11V8a4 4 0 0 1 8 0v3\"/>"),
        new("key", "Password", "<circle cx=\"8\" cy=\"15\" r=\"4\"/><path d=\"M11 12l9-9M16 7l3 3\"/>"),
        new("person", "Person", "<circle cx=\"12\" cy=\"8\" r=\"4\"/><path d=\"M4 21c0-4 4-6 8-6s8 2 8 6\"/>"),
        new("newstarter", "New starter", "<circle cx=\"9\" cy=\"8\" r=\"4\"/><path d=\"M2 21c0-4 3-6 7-6M17 9v6M14 12h6\"/>"),
        new("mail", "Email", "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\"/><path d=\"M3 7l9 6 9-6\"/>"),
        new("shield", "Safety and security", "<path d=\"M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z\"/>"),
        new("building", "Building", "<rect x=\"5\" y=\"3\" width=\"14\" height=\"18\"/><path d=\"M9 7h.01M12 7h.01M15 7h.01M9 11h.01M12 11h.01M15 11h.01M10 21v-4h4v4\"/>"),
        new("gear", "Repairs and settings", "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9L7 7M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1\"/>"),
        new("alert", "Something is wrong", "<path d=\"M12 3l10 18H2z\"/><path d=\"M12 10v5M12 18h.01\"/>"),
        new("folder", "Files and general", "<path d=\"M3 6h6l2 2h10v11H3z\"/>"),
        new("question", "Other", "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.7.4-1 .9-1 1.7M12 17h.01\"/>"),
    ];

    // Mid tones that read on a white or a dark page, since the colour is only the accent border and the icon.
    public static readonly IReadOnlyList<ColorChoice> Colors =
    [
        new("teal", "Teal", "#067a78"),
        new("blue", "Blue", "#2f6fb2"),
        new("indigo", "Indigo", "#5b52b8"),
        new("purple", "Purple", "#8a4fa3"),
        new("pink", "Pink", "#b8487a"),
        new("red", "Red", "#af3f3f"),
        new("orange", "Orange", "#c0641c"),
        new("gold", "Gold", "#a07a12"),
        new("green", "Green", "#3f8a4a"),
        new("slate", "Slate", "#5d6f7a"),
    ];

    // What a category looks like until someone chooses: the starter categories get a fitting look, anything else the
    // folder icon in teal. Matched by name, so a school's own "Classroom AV" or "Network" is covered too.
    private static readonly Dictionary<string, (string Icon, string Color)> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hardware"] = ("laptop", "blue"),
        ["Software"] = ("app", "purple"),
        ["Account"] = ("lock", "orange"),
        ["Network"] = ("wifi", "green"),
        ["Classroom AV"] = ("projector", "pink"),
        ["Other"] = ("question", "slate"),
        ["Onboarding"] = ("newstarter", "indigo"),
    };

    public static (string Icon, string Color) DefaultFor(string category) =>
        Defaults.TryGetValue(category?.Trim() ?? "", out var found) ? found : (DefaultIcon, DefaultColor);

    public static IconChoice? FindIcon(string? key) => Icons.FirstOrDefault(x => string.Equals(x.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
    public static ColorChoice? FindColor(string? key) => Colors.FirstOrDefault(x => string.Equals(x.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    // The colour as CSS. An unknown key (it can only come from a hand-edited database) falls back to teal.
    public static string Hex(string? key) => (FindColor(key) ?? FindColor(DefaultColor)!).Hex;

    // Inline SVG for a key, or the folder for an unknown one. The shapes are the constants above, never user input.
    public static HtmlString Svg(string? key)
    {
        var icon = FindIcon(key) ?? FindIcon(DefaultIcon)!;
        return new HtmlString($"<svg class=\"portal-icon\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{icon.Shapes}</svg>");
    }
}
