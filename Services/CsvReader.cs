using System.Text;

namespace EduHelpdesk.Services;

// Reads CSV files exported from spreadsheets and other systems: quoted values (with commas, doubled quotes and line breaks),
// comma, semicolon or tab separators, and UTF-8 or older Windows text encodings.
public static class CsvReader
{
    public sealed record CsvFile(char Delimiter, IReadOnlyList<string[]> Rows);

    // Text as UTF-8 (with or without a byte-order mark). Files saved by older Excel versions are not valid UTF-8, so they are read as Windows Latin-1.
    public static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    // Picks the separator that appears most in the first line, ignoring anything inside quotes.
    public static char DetectDelimiter(string text)
    {
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && (c == '\n' || c == '\r')) { if (counts.Values.Any(v => v > 0)) break; }
            else if (!inQuotes && counts.ContainsKey(c)) counts[c]++;
        }
        var best = counts.OrderByDescending(x => x.Value).First();
        return best.Value == 0 ? ',' : best.Key;
    }

    // Splits the text into rows of trimmed cells. Completely blank rows are dropped.
    public static CsvFile Parse(string text, char? delimiter = null)
    {
        var separator = delimiter ?? DetectDelimiter(text);
        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var cellWasQuoted = false;

        void EndCell()
        {
            cells.Add(cellWasQuoted ? cell.ToString() : cell.ToString().Trim());
            cell.Clear();
            cellWasQuoted = false;
        }
        void EndRow()
        {
            EndCell();
            if (cells.Any(x => x.Trim().Length > 0)) rows.Add(cells.Select(x => x.Trim()).ToArray());
            cells = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"') cell.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (c == '"' && cell.ToString().Trim().Length == 0) { inQuotes = true; cellWasQuoted = true; cell.Clear(); }
            else if (c == separator) EndCell();
            else if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
            else if (c == '\n') EndRow();
            else cell.Append(c);
        }
        if (cell.Length > 0 || cells.Count > 0 || cellWasQuoted) EndRow();
        return new CsvFile(separator, rows);
    }
}
