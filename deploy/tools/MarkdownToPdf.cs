#:package PDFsharp-MigraDoc@6.2.4
// Turns the install guide (Markdown) into the PDF that goes in the release zip. Used by deploy/New-Release.ps1:
//   dotnet run --file deploy/tools/MarkdownToPdf.cs -- <in.md> <out.pdf> "<footer text>"
// It understands the Markdown the guide uses and no more: # to #### headings, paragraphs, - and 1. lists (one level of
// nesting), ``` code blocks, | tables |, > notes, **bold**, `code` and [links](url). Fonts come from Windows.
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

var lines = File.ReadAllLines(args[0]);
var output = args[1];
var footer = args.Length > 2 ? args[2] : "";
GlobalFontSettings.FontResolver = new WindowsFonts();

var teal = Color.FromRgb(0x06, 0x7A, 0x78);
var ink = Color.FromRgb(0x17, 0x25, 0x2F);
var muted = Color.FromRgb(0x64, 0x74, 0x7C);
var document = new Document();
document.Info.Title = lines.FirstOrDefault(x => x.StartsWith("# "))?[2..] ?? "EduHelpdesk";
var normal = document.Styles[StyleNames.Normal]!;
normal.Font.Name = "Body"; normal.Font.Size = 10; normal.Font.Color = ink;
normal.ParagraphFormat.SpaceAfter = 5; normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple; normal.ParagraphFormat.LineSpacing = 1.15;
void Heading(string style, double size, double before, Color colour)
{
    var s = document.Styles[style]!;
    s.Font.Name = "Body"; s.Font.Size = size; s.Font.Bold = true; s.Font.Color = colour;
    s.ParagraphFormat.SpaceBefore = before; s.ParagraphFormat.SpaceAfter = 5; s.ParagraphFormat.KeepWithNext = true;
}
Heading(StyleNames.Heading1, 22, 0, ink);
Heading(StyleNames.Heading2, 15, 16, teal);
Heading(StyleNames.Heading3, 12, 12, ink);
Heading(StyleNames.Heading4, 10.5, 8, ink);
var code = document.Styles.AddStyle("Code", StyleNames.Normal);
code.Font.Name = "Mono"; code.Font.Size = 8.5;
code.ParagraphFormat.Shading.Color = Color.FromRgb(0xF2, 0xF6, 0xF6);
code.ParagraphFormat.LeftIndent = Unit.FromMillimeter(2); code.ParagraphFormat.SpaceAfter = 0; code.ParagraphFormat.LineSpacing = 1.0;
var note = document.Styles.AddStyle("Note", StyleNames.Normal);
note.ParagraphFormat.LeftIndent = Unit.FromMillimeter(4);
note.ParagraphFormat.Borders.Left.Width = 2; note.ParagraphFormat.Borders.Left.Color = teal; note.ParagraphFormat.Borders.DistanceFromLeft = Unit.FromMillimeter(3);
note.ParagraphFormat.Shading.Color = Color.FromRgb(0xE8, 0xF0, 0xEF);

var section = document.AddSection();
section.PageSetup = document.DefaultPageSetup.Clone();
section.PageSetup.PageFormat = PageFormat.A4;
section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromMillimeter(20);
section.PageSetup.TopMargin = Unit.FromMillimeter(18); section.PageSetup.BottomMargin = Unit.FromMillimeter(20);
var foot = section.Footers.Primary.AddParagraph();
foot.Format.Font.Size = 8; foot.Format.Font.Color = muted; foot.Format.Alignment = ParagraphAlignment.Center;
foot.AddText(footer.Length > 0 ? footer + "   ·   page " : "Page "); foot.AddPageField(); foot.AddText(" of "); foot.AddNumPagesField();

// **bold**, *italic*, `code` and [text](url) inside a line. Bold and italic can hold code; code holds nothing else.
void Inline(Paragraph paragraph, string text) => Runs(paragraph.AddFormattedText(), text, false, false);
void Runs(FormattedText into, string text, bool bold, bool italic)
{
    foreach (Match m in Regex.Matches(text, @"\*\*(.+?)\*\*|(?<![\w*])\*([^*\s][^*]*?)\*(?![\w*])|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)|([^*`\[]+|[*`\[])"))
    {
        if (m.Groups[1].Success) Runs(into, m.Groups[1].Value, true, italic);
        else if (m.Groups[2].Success) Runs(into, m.Groups[2].Value, bold, true);
        else if (m.Groups[3].Success)
        {
            var f = into.AddFormattedText(m.Groups[3].Value);
            f.Font.Name = "Mono"; f.Font.Size = 9;
            if (bold) f.Bold = true;
        }
        else if (m.Groups[4].Success)
        {
            var link = into.AddHyperlink(m.Groups[5].Value, HyperlinkType.Web);
            var t = link.AddFormattedText(m.Groups[4].Value); t.Color = teal; t.Underline = Underline.Single;
        }
        else
        {
            // Only ever switched on: a heading's or header row's own bold must show through.
            var f = into.AddFormattedText(m.Value);
            if (bold) f.Bold = true;
            if (italic) f.Italic = true;
        }
    }
}
var i = 0;
var paragraphText = new List<string>();
void FlushParagraph()
{
    if (paragraphText.Count == 0) return;
    Inline(section.AddParagraph(), string.Join(" ", paragraphText));
    paragraphText.Clear();
}
while (i < lines.Length)
{
    var line = lines[i];
    var trimmed = line.Trim();
    if (trimmed.Length == 0) { FlushParagraph(); i++; continue; }
    if (trimmed.StartsWith("```"))
    {
        FlushParagraph();
        var block = new List<string>();
        for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```"); i++) block.Add(lines[i]);
        i++;
        for (var b = 0; b < block.Count; b++)
        {
            var indent = block[b].Length - block[b].TrimStart().Length;
            var p = section.AddParagraph(block[b].Length == 0 ? " " : new string(' ', indent) + block[b].TrimStart(), "Code");
            if (b == 0) p.Format.SpaceBefore = 2;
            if (b == block.Count - 1) p.Format.SpaceAfter = 7;
        }
        continue;
    }
    var heading = Regex.Match(trimmed, @"^(#{1,4})\s+(.*)$");
    if (heading.Success)
    {
        FlushParagraph();
        var level = heading.Groups[1].Value.Length;
        Inline(section.AddParagraph("", level switch { 1 => StyleNames.Heading1, 2 => StyleNames.Heading2, 3 => StyleNames.Heading3, _ => StyleNames.Heading4 }), heading.Groups[2].Value);
        i++;
        continue;
    }
    if (trimmed == "---") { FlushParagraph(); i++; continue; }
    if (trimmed.StartsWith(">"))
    {
        FlushParagraph();
        var quote = new List<string>();
        for (; i < lines.Length && lines[i].Trim().StartsWith(">"); i++) quote.Add(lines[i].Trim().TrimStart('>').Trim());
        Inline(section.AddParagraph("", "Note"), string.Join(" ", quote));
        continue;
    }
    if (trimmed.StartsWith("|"))
    {
        FlushParagraph();
        var rows = new List<string[]>();
        for (; i < lines.Length && lines[i].Trim().StartsWith("|"); i++)
        {
            var cells = lines[i].Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray();
            if (cells.All(x => Regex.IsMatch(x, @"^:?-+:?$"))) continue;
            rows.Add(cells);
        }
        var columns = rows.Max(x => x.Length);
        var table = section.AddTable();
        table.Borders.Width = 0.5; table.Borders.Color = Color.FromRgb(0xDC, 0xE5, 0xE7);
        var width = (section.PageSetup.PageWidth.Point - section.PageSetup.LeftMargin.Point - section.PageSetup.RightMargin.Point);
        // Each column gets width in proportion to its longest cell, but no less than a sixth of its fair share.
        var weights = Enumerable.Range(0, columns).Select(c => Math.Max(rows.Max(r => c < r.Length ? Math.Min(r[c].Length, 90) : 0), 8)).Select(x => (double)x).ToArray();
        for (var c = 0; c < columns; c++) weights[c] = Math.Max(weights[c], weights.Sum() / columns / 6);
        foreach (var weight in weights) table.AddColumn(Unit.FromPoint(width * weight / weights.Sum()));
        for (var r = 0; r < rows.Count; r++)
        {
            var row = table.AddRow();
            row.TopPadding = 2; row.BottomPadding = 2;
            if (r == 0) { row.HeadingFormat = true; row.Shading.Color = Color.FromRgb(0xE8, 0xF0, 0xEF); row.Format.Font.Bold = true; }
            for (var c = 0; c < columns; c++)
            {
                var cell = row.Cells[c].AddParagraph();
                cell.Format.SpaceAfter = 0; cell.Format.Font.Size = 9;
                Inline(cell, c < rows[r].Length ? rows[r][c] : "");
            }
        }
        section.AddParagraph().Format.SpaceAfter = 4;
        continue;
    }
    var bullet = Regex.Match(line, @"^(\s*)([-*]|\d+\.)\s+(.*)$");
    if (bullet.Success)
    {
        FlushParagraph();
        var depth = bullet.Groups[1].Value.Length >= 2 ? 1 : 0;
        var text = bullet.Groups[3].Value;
        // A list item's text can carry on over following indented lines.
        for (i++; i < lines.Length && lines[i].Length > 0 && char.IsWhiteSpace(lines[i][0]) && !Regex.IsMatch(lines[i], @"^\s*([-*]|\d+\.)\s") && !lines[i].Trim().StartsWith("```"); i++)
            text += " " + lines[i].Trim();
        var p = section.AddParagraph();
        p.Format.LeftIndent = Unit.FromMillimeter(6 + depth * 6);
        p.Format.FirstLineIndent = Unit.FromMillimeter(-4);
        p.Format.SpaceAfter = 2.5;
        var marker = bullet.Groups[2].Value;
        p.AddText((marker is "-" or "*" ? (depth == 0 ? "•" : "–") : marker) + "\t");
        p.Format.TabStops.AddTabStop(Unit.FromMillimeter(6 + depth * 6));
        Inline(p, text);
        continue;
    }
    paragraphText.Add(trimmed);
    i++;
}
FlushParagraph();

var renderer = new PdfDocumentRenderer { Document = document };
renderer.RenderDocument();
var pages = renderer.PdfDocument.PageCount;
renderer.PdfDocument.Save(output);
Console.WriteLine($"Wrote {output} ({pages} pages).");

// Body text in Segoe UI (Arial if missing), code in Consolas.
sealed class WindowsFonts : IFontResolver
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
    private static readonly Dictionary<string, string[]> Families = new()
    {
        ["Body"] = File.Exists(Path.Combine(Folder, "segoeui.ttf")) ? ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"] : ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
        ["Mono"] = ["consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"],
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var family = Families.ContainsKey(familyName) ? familyName : "Body";
        var index = (bold ? 1 : 0) + (italic ? 2 : 0);
        return File.Exists(Path.Combine(Folder, Families[family][index])) ? new FontResolverInfo($"{family}|{index}") : new FontResolverInfo($"{family}|0", bold, italic);
    }

    public byte[]? GetFont(string faceName)
    {
        var parts = faceName.Split('|');
        return File.ReadAllBytes(Path.Combine(Folder, Families[parts[0]][int.Parse(parts[1])]));
    }
}
