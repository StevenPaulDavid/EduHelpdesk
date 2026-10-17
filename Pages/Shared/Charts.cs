using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

namespace EduHelpdesk.Pages;

// The charts on the dashboard and at the top of the ticket report, drawn on the server: columns and lines over time
// as inline SVG, bar lists and the heatmap as plain HTML. No library and no inline script. Every mark carries its
// numbers in data-tip, which wwwroot/js/charts.js shows as a tooltip; a mark with somewhere to go is a link to the
// matching list; and every chart has the same numbers as a table under it (Table).
//
// Colour is by role, a CSS class t-<role> (site.css, "Charts"): "brand" - the school's colour - for a one-series chart; "c1" to
// "c5" for series told apart by colour, a fixed order checked for colour-blind separation in light and dark, never
// the school's colour, which could clash with them; "p1" to "p4" for priority, light to dark; "critical" and "serious"
// for overdue and past-its-life, and "quiet" for what is left over. Text is always in the page's ink, never a series
// colour.
public static class Charts
{
    public sealed record Series(string Name, string Tone, IReadOnlyList<double> Values);
    // Label is what the axis and the table say ("6 Oct"); Name what the tooltip says ("Mon 6 Oct").
    public sealed record Step(string Label, string Name, string? Href = null);
    public sealed record Key(string Name, string Tone);
    // One row of a bar list. Values line up with the list's keys; Hrefs, when given, with the values. Tone overrides
    // the key's colour for a list whose rows are each a different thing (the due-date chart).
    public sealed record Bar(string Label, IReadOnlyList<double> Values, string? Href = null, string? Sub = null, string? Note = null,
        IReadOnlyList<string?>? Hrefs = null, string? Tone = null, double? Marker = null, string? MarkerTip = null, bool NoteWarn = false);

    private const double Width = 600, Height = 190, Left = 40, Right = 16, Top = 18, Bottom = 26;
    private static readonly CultureInfo Css = CultureInfo.InvariantCulture;

    public static string Count(double value) => value.ToString("#,0", CultureInfo.GetCultureInfo("en-GB"));
    public static string Money(double value) => value.ToString("C0", CultureInfo.GetCultureInfo("en-GB"));
    // £1.2k on an axis, where the whole figure would crowd it.
    public static string MoneyShort(double value) => value >= 1000 ? "£" + (value / 1000).ToString("0.#", Css) + "k" : Money(value);

    // ---- Over time: columns (stacked when there is more than one series) and lines ----

    public static IHtmlContent Columns(IReadOnlyList<Step> steps, IReadOnlyList<Series> series, string description,
        Func<double, string>? format = null, Func<double, string>? axis = null)
    {
        format ??= Count;
        var svg = Open(description);
        if (steps.Count == 0) return Close(svg);
        var totals = steps.Select((_, i) => series.Sum(s => s.Values[i])).ToList();
        var frame = new Frame(steps.Count, NiceMax(totals.Max()));
        Grid(svg, frame, steps, axis ?? format);
        var barWidth = Math.Min(30, frame.Slot * 0.64);
        for (var i = 0; i < steps.Count; i++)
        {
            var x0 = frame.X(i) - barWidth / 2;
            var marks = new StringBuilder();
            var below = 0.0;
            // The topmost segment with something in it gets the rounded end; each sits 2px above the one below it.
            var top = series.Select((s, n) => (s, n)).LastOrDefault(x => x.s.Values[i] > 0).n;
            for (var n = 0; n < series.Count; n++)
            {
                var value = series[n].Values[i];
                if (value <= 0) continue;
                var (yBase, yTop) = (frame.Y(below) - (below > 0 ? 2 : 0), frame.Y(below + value));
                below += value;
                if (yBase - yTop < 0.5) yTop = yBase - 0.5;
                marks.Append(CultureInfo.InvariantCulture, $"<path class=\"mark t-{series[n].Tone}\" d=\"{Rect(x0, yTop, barWidth, yBase - yTop, n == top ? 4 : 0)}\" />");
            }
            var tip = series.Count == 1 ? $"{steps[i].Name}: {format(totals[i])}"
                : $"{steps[i].Name}: {format(totals[i])}|" + string.Join("|", series.Where(s => s.Values[i] > 0).Select(s => $"{s.Name}: {format(s.Values[i])}"));
            Slot(svg, frame, i, steps[i].Href, tip, marks.ToString());
        }
        if (totals[^1] > 0) Label(svg, frame.X(steps.Count - 1), frame.Y(totals[^1]) - 8, format(totals[^1]));
        return Close(svg);
    }

    public static IHtmlContent Lines(IReadOnlyList<Step> steps, IReadOnlyList<Series> series, string description, Func<double, string>? format = null)
    {
        format ??= Count;
        var svg = Open(description);
        if (steps.Count == 0) return Close(svg);
        var frame = new Frame(steps.Count, NiceMax(series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max()));
        Grid(svg, frame, steps, format);
        foreach (var s in series)
            svg.Append(CultureInfo.InvariantCulture, $"<polyline class=\"line t-{s.Tone}\" points=\"{string.Join(" ", s.Values.Select((v, i) => $"{N(frame.X(i))},{N(frame.Y(v))}"))}\" />");
        for (var i = 0; i < steps.Count; i++)
        {
            // A guide line and every series' dot, shown while the step is hovered.
            var marks = new StringBuilder();
            marks.Append(CultureInfo.InvariantCulture, $"<line class=\"guide\" x1=\"{N(frame.X(i))}\" x2=\"{N(frame.X(i))}\" y1=\"{N(Top)}\" y2=\"{N(frame.Y(0))}\" />");
            foreach (var s in series)
                marks.Append(CultureInfo.InvariantCulture, $"<circle class=\"dot t-{s.Tone}\" cx=\"{N(frame.X(i))}\" cy=\"{N(frame.Y(s.Values[i]))}\" r=\"4.5\" />");
            var tip = series.Count == 1 ? $"{steps[i].Name}: {format(series[0].Values[i])}"
                : steps[i].Name + "|" + string.Join("|", series.Select(s => $"{s.Name}: {format(s.Values[i])}"));
            Slot(svg, frame, i, steps[i].Href, tip, marks.ToString());
        }
        // The latest value of each series, on the chart: a dot and its number, the higher above and the lower below.
        var last = steps.Count - 1;
        var ordered = series.OrderByDescending(s => s.Values[last]).ToList();
        for (var n = 0; n < ordered.Count; n++)
        {
            var (x, y) = (frame.X(last), frame.Y(ordered[n].Values[last]));
            svg.Append(CultureInfo.InvariantCulture, $"<circle class=\"dot end t-{ordered[n].Tone}\" cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"4.5\" />");
            Label(svg, x, n == 0 ? y - 10 : Math.Min(y + 18, frame.Y(0) - 2), format(ordered[n].Values[last]));
        }
        return Close(svg);
    }

    // The numbers behind a chart over time.
    public static IHtmlContent Table(string firstColumn, IReadOnlyList<Step> steps, IReadOnlyList<Series> series, Func<double, string>? format = null)
    {
        format ??= Count;
        var html = new StringBuilder("<details class=\"chart-table\"><summary>Show as table</summary><div class=\"table-wrap\"><table><thead><tr>");
        html.Append(CultureInfo.InvariantCulture, $"<th>{E(firstColumn)}</th>");
        foreach (var s in series) html.Append(CultureInfo.InvariantCulture, $"<th>{E(s.Name)}</th>");
        if (series.Count > 1) html.Append("<th>Total</th>");
        html.Append("</tr></thead><tbody>");
        for (var i = 0; i < steps.Count; i++)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{(steps[i].Href is { } href ? $"<a href=\"{E(href)}\">{E(steps[i].Name)}</a>" : E(steps[i].Name))}</td>");
            foreach (var s in series) html.Append(CultureInfo.InvariantCulture, $"<td>{E(format(s.Values[i]))}</td>");
            if (series.Count > 1) html.Append(CultureInfo.InvariantCulture, $"<td>{E(format(series.Sum(s => s.Values[i])))}</td>");
            html.Append("</tr>");
        }
        return new HtmlString(html.Append("</tbody></table></div></details>").ToString());
    }

    // ---- Bar lists: one horizontal bar per row, split by the keys when there are several ----

    public static IHtmlContent BarList(IReadOnlyList<Bar> bars, IReadOnlyList<Key> keys, string description, Func<double, string>? format = null)
    {
        format ??= Count;
        var max = NiceMax(bars.Select(b => Math.Max(b.Values.Sum(), b.Marker ?? 0)).DefaultIfEmpty(0).Max());
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"<ul class=\"bar-list\" aria-label=\"{E(description)}\">");
        foreach (var bar in bars)
        {
            var total = bar.Values.Sum();
            html.Append("<li class=\"bar-row\"><span class=\"bar-label\">");
            html.Append(bar.Href is { } href ? $"<a href=\"{E(href)}\">{E(bar.Label)}</a>" : E(bar.Label));
            if (bar.Sub is { } sub) html.Append(CultureInfo.InvariantCulture, $" <small>{E(sub)}</small>");
            html.Append("</span><span class=\"bar-track\">");
            var lastShown = bar.Values.Select((v, n) => (v, n)).LastOrDefault(x => x.v > 0).n;
            for (var n = 0; n < bar.Values.Count; n++)
            {
                var value = bar.Values[n];
                if (value <= 0) continue;
                var tone = "t-" + (bar.Tone ?? keys[n].Tone);
                var tip = keys.Count == 1 ? $"{bar.Label}: {format(value)}" : $"{bar.Label}|{keys[n].Name}: {format(value)}";
                var style = string.Create(Css, $"width:{value / max * 100:0.##}%");
                var end = n == lastShown ? " end" : "";
                if (bar.Hrefs?.ElementAtOrDefault(n) is { } segmentHref)
                    html.Append(CultureInfo.InvariantCulture, $"<a class=\"bar-seg {tone}{end}\" style=\"{style}\" href=\"{E(segmentHref)}\" data-tip=\"{E(tip)}\" aria-label=\"{E(tip.Replace('|', ' '))}\"></a>");
                else
                    html.Append(CultureInfo.InvariantCulture, $"<span class=\"bar-seg {tone}{end}\" style=\"{style}\" data-tip=\"{E(tip)}\"></span>");
            }
            if (bar.Marker is { } marker)
                html.Append(CultureInfo.InvariantCulture, $"<i class=\"bar-marker\" style=\"{string.Create(Css, $"left:{marker / max * 100:0.##}%")}\" data-tip=\"{E(bar.MarkerTip ?? "")}\"></i>");
            html.Append(CultureInfo.InvariantCulture, $"</span><span class=\"bar-value\">{E(format(total))}");
            if (bar.Note is { } note) html.Append(CultureInfo.InvariantCulture, $" <small class=\"{(bar.NoteWarn ? "bar-note-warn" : "")}\">{E(note)}</small>");
            html.Append("</span></li>");
        }
        return new HtmlString(html.Append("</ul>").ToString());
    }

    public static IHtmlContent Table(string firstColumn, IReadOnlyList<Bar> bars, IReadOnlyList<Key> keys, Func<double, string>? format = null, string? noteColumn = null)
    {
        format ??= Count;
        var html = new StringBuilder("<details class=\"chart-table\"><summary>Show as table</summary><div class=\"table-wrap\"><table><thead><tr>");
        html.Append(CultureInfo.InvariantCulture, $"<th>{E(firstColumn)}</th>");
        foreach (var key in keys) html.Append(CultureInfo.InvariantCulture, $"<th>{E(key.Name)}</th>");
        if (keys.Count > 1) html.Append("<th>Total</th>");
        if (noteColumn is not null) html.Append(CultureInfo.InvariantCulture, $"<th>{E(noteColumn)}</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var bar in bars)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{(bar.Href is { } href ? $"<a href=\"{E(href)}\">{E(bar.Label)}</a>" : E(bar.Label))}{(bar.Sub is { } sub ? $" <span class=\"muted\">{E(sub)}</span>" : "")}</td>");
            foreach (var value in bar.Values) html.Append(CultureInfo.InvariantCulture, $"<td>{E(format(value))}</td>");
            if (keys.Count > 1) html.Append(CultureInfo.InvariantCulture, $"<td>{E(format(bar.Values.Sum()))}</td>");
            if (noteColumn is not null) html.Append(CultureInfo.InvariantCulture, $"<td>{E(bar.Note ?? "")}</td>");
            html.Append("</tr>");
        }
        return new HtmlString(html.Append("</tbody></table></div></details>").ToString());
    }

    // ---- Keys, and the heatmap ----

    // The legend, for a chart with more than one colour. A one-series chart has none: its heading names it.
    public static IHtmlContent Legend(IEnumerable<Key> keys) =>
        new HtmlString("<ul class=\"chart-legend\">" + string.Concat(keys.Select(k => $"<li><i class=\"swatch t-{k.Tone}\"></i>{E(k.Name)}</li>")) + "</ul>");

    // A grid of counts - days down, hours across - shaded in six steps of the school's colour from nothing to the most.
    public static IHtmlContent Heatmap(IReadOnlyList<string> rows, IReadOnlyList<string> columns, int[,] values, string description, Func<int, int, string> tip)
    {
        var max = 0;
        foreach (var value in values) max = Math.Max(max, value);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"table-wrap heatmap-wrap\"><table class=\"heatmap\" aria-label=\"{E(description)}\"><thead><tr><td></td>");
        foreach (var column in columns) html.Append(CultureInfo.InvariantCulture, $"<th scope=\"col\">{E(column)}</th>");
        html.Append("</tr></thead><tbody>");
        for (var r = 0; r < rows.Count; r++)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><th scope=\"row\">{E(rows[r])}</th>");
            for (var c = 0; c < columns.Count; c++)
            {
                var value = values[r, c];
                var step = value == 0 || max == 0 ? 0 : 1 + (int)Math.Min(4, Math.Floor((value - 1) * 5.0 / max));
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"heat-{step}\" data-tip=\"{E(tip(r, c))}\"><span>{(value == 0 ? "" : value.ToString(Css))}</span></td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table></div><p class=\"heat-key\" aria-hidden=\"true\">Fewer");
        for (var step = 0; step <= 5; step++) html.Append(CultureInfo.InvariantCulture, $"<i class=\"heat-{step}\"></i>");
        return new HtmlString(html.Append("More</p>").ToString());
    }

    // ---- Drawing ----

    private sealed record Frame(int Steps, double Max)
    {
        public double Slot => (Width - Left - Right) / Steps;
        public double X(int index) => Left + Slot * index + Slot / 2;
        public double Y(double value) => Top + (Height - Top - Bottom) * (1 - value / Max);
    }

    private static StringBuilder Open(string description) =>
        new StringBuilder().Append(CultureInfo.InvariantCulture, $"<svg class=\"chart\" viewBox=\"0 0 {Width} {Height}\" role=\"img\" aria-label=\"{E(description)}\">");

    private static HtmlString Close(StringBuilder svg) => new(svg.Append("</svg>").ToString());

    // A recessive grid - the baseline, a half line and the top, each with its value - and the names of the first,
    // middle and last steps, so they never collide.
    private static void Grid(StringBuilder svg, Frame frame, IReadOnlyList<Step> steps, Func<double, string> format)
    {
        foreach (var value in new[] { 0, frame.Max / 2, frame.Max }.Distinct())
        {
            svg.Append(CultureInfo.InvariantCulture, $"<line class=\"{(value == 0 ? "baseline" : "grid")}\" x1=\"{N(Left)}\" x2=\"{N(Width - Right)}\" y1=\"{N(frame.Y(value))}\" y2=\"{N(frame.Y(value))}\" />");
            svg.Append(CultureInfo.InvariantCulture, $"<text class=\"axis\" x=\"{N(Left - 6)}\" y=\"{N(frame.Y(value) + 4)}\" text-anchor=\"end\">{E(format(value))}</text>");
        }
        foreach (var index in new[] { 0, steps.Count / 2, steps.Count - 1 }.Distinct())
        {
            var anchor = steps.Count == 1 ? "middle" : index == 0 ? "start" : index == steps.Count - 1 ? "end" : "middle";
            var x = steps.Count == 1 ? frame.X(0) : index == 0 ? Left : index == steps.Count - 1 ? Width - Right : frame.X(index);
            svg.Append(CultureInfo.InvariantCulture, $"<text class=\"axis\" x=\"{N(x)}\" y=\"{N(Height - 7)}\" text-anchor=\"{anchor}\">{E(steps[index].Label)}</text>");
        }
    }

    // One step: its marks, and a full-height target wider than them carrying the tooltip - a link when the step opens a list.
    private static void Slot(StringBuilder svg, Frame frame, int index, string? href, string tip, string marks)
    {
        var hit = string.Create(Css, $"<rect class=\"hit\" x=\"{N(Left + frame.Slot * index)}\" y=\"0\" width=\"{N(frame.Slot)}\" height=\"{N(Height - Bottom)}\" />");
        svg.Append(href is null
            ? $"<g class=\"slot\" data-tip=\"{E(tip)}\">{marks}{hit}</g>"
            : $"<a class=\"slot\" href=\"{E(href)}\" data-tip=\"{E(tip)}\" aria-label=\"{E(tip.Replace('|', ' '))}\">{marks}{hit}</a>");
    }

    private static void Label(StringBuilder svg, double x, double y, string text) =>
        svg.Append(CultureInfo.InvariantCulture, $"<text class=\"value\" x=\"{N(Math.Min(x, Width - Right))}\" y=\"{N(Math.Max(y, 11))}\" text-anchor=\"{(x > Width - Right - 20 ? "end" : "middle")}\">{E(text)}</text>");

    // A bar with a rounded top on a square base.
    private static string Rect(double x, double y, double width, double height, double radius)
    {
        var r = Math.Min(radius, Math.Min(width / 2, height));
        return $"M{N(x)},{N(y + height)} V{N(y + r)} Q{N(x)},{N(y)} {N(x + r)},{N(y)} H{N(x + width - r)} Q{N(x + width)},{N(y)} {N(x + width)},{N(y + r)} V{N(y + height)} Z";
    }

    // A round number at least the largest value, whose half is a whole number too, for the top and middle lines.
    private static double NiceMax(double value)
    {
        if (value <= 2) return 2;
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 4, 6, 8, 10 }) if (step * power >= value) return step * power;
        return value;
    }

    private static string N(double value) => value.ToString("0.#", Css);
    private static string E(string text) => WebUtility.HtmlEncode(text);
}
