using PdfSharp.Fonts;

namespace EduHelpdesk.Services;

// Fonts for the PDFs this app writes (the project proposal). PDFsharp's cross-platform build finds no fonts by itself,
// so this hands it one family, whatever family a document asks for. It looks in order for:
//   1. Fonts/regular.ttf, bold.ttf, italic.ttf, bolditalic.ttf under the app's content root - drop a family there to
//      choose one, or to give a server with no fonts of its own (a bare Linux container) something to use;
//   2. Arial or Segoe UI on Windows;
//   3. Liberation Sans or DejaVu Sans on Linux, Arial on a Mac.
// A missing bold or italic face is drawn from the regular one. With no family at all, IsAvailable is false and the
// proposal says so rather than failing somewhere inside PDFsharp.
public sealed class PdfFonts : IFontResolver
{
    private static readonly object Sync = new();
    private static PdfFonts? _installed;
    private readonly string?[] _faces; // regular, bold, italic, bold italic

    private PdfFonts(string?[] faces) => _faces = faces;

    public static bool IsAvailable => _installed?._faces[0] is not null;

    // Called once at startup. PDFsharp only allows its resolver to be set before the first document is made.
    public static void Install(string contentRoot)
    {
        lock (Sync)
        {
            if (_installed is not null) return;
            _installed = new PdfFonts(Find(contentRoot));
            GlobalFontSettings.FontResolver = _installed;
        }
    }

    private static string?[] Find(string contentRoot)
    {
        var windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } w ? w : @"C:\Windows", "Fonts");
        string[][] families =
        [
            [.. new[] { "regular.ttf", "bold.ttf", "italic.ttf", "bolditalic.ttf" }.Select(x => Path.Combine(contentRoot, "Fonts", x))],
            [.. new[] { "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf" }.Select(x => Path.Combine(windows, x))],
            [.. new[] { "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf" }.Select(x => Path.Combine(windows, x))],
            .. new[] { "/usr/share/fonts/truetype/liberation", "/usr/share/fonts/truetype/liberation2", "/usr/share/fonts/liberation-sans", "/usr/share/fonts/liberation" }
                .Select(folder => new[] { "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf" }.Select(x => Path.Combine(folder, x)).ToArray()),
            .. new[] { "/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/dejavu", "/usr/share/fonts/dejavu-sans-fonts" }
                .Select(folder => new[] { "DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf" }.Select(x => Path.Combine(folder, x)).ToArray()),
            [.. new[] { "Arial.ttf", "Arial Bold.ttf", "Arial Italic.ttf", "Arial Bold Italic.ttf" }.Select(x => Path.Combine("/System/Library/Fonts/Supplemental", x))]
        ];
        foreach (var family in families)
            if (File.Exists(family[0])) return family.Select(x => File.Exists(x) ? x : null).ToArray();
        return [null, null, null, null];
    }

    // Face names are the index into _faces, so GetFont never has to trust a name it didn't hand out.
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (_faces[0] is null) return null;
        var wanted = (bold ? 1 : 0) + (italic ? 2 : 0);
        if (_faces[wanted] is not null) return new FontResolverInfo($"face{wanted}");
        // Fall back to whichever real face is closest, and let PDFsharp embolden or slant it.
        var usable = bold && _faces[1] is not null ? 1 : italic && _faces[2] is not null ? 2 : 0;
        return new FontResolverInfo($"face{usable}", bold && usable is not 1 and not 3, italic && usable is not 2 and not 3);
    }

    public byte[]? GetFont(string faceName) =>
        faceName.StartsWith("face", StringComparison.Ordinal) && int.TryParse(faceName[4..], out var index) && index is >= 0 and < 4 && _faces[index] is { } path
            ? File.ReadAllBytes(path)
            : null;
}
