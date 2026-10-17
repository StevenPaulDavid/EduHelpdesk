using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

namespace EduHelpdesk.Pages;

// Small one-series charts drawn as inline SVG, for a report's summary (the ticket report's trend). No script and no
// library: each step has a hover target wider than its mark carrying a tooltip, the latest value is labelled on the
// chart, the colours are the theme's (site.css .mini-chart), and the page shows the same numbers as a table beside it.
public static class MiniChart
{
    public sealed record Point(string Label, int Value, string Tip);

    private const double Width = 600, Height = 170, Left = 34, Right = 14, Top = 16, Bottom = 26;
    private static readonly CultureInfo Css = CultureInfo.InvariantCulture;

    public static IHtmlContent Bars(IReadOnlyList<Point> points, string description) => Draw(points, description, bars: true);
    public static IHtmlContent Line(IReadOnlyList<Point> points, string description) => Draw(points, description, bars: false);

    private static IHtmlContent Draw(IReadOnlyList<Point> points, string description, bool bars)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg class=\"mini-chart\" viewBox=\"0 0 {Width} {Height}\" role=\"img\" aria-label=\"{Encode(description)}\">");
        if (points.Count == 0) return new HtmlString(svg.Append("</svg>").ToString());

        var max = NiceMax(points.Max(x => x.Value));
        var plotWidth = Width - Left - Right;
        var plotHeight = Height - Top - Bottom;
        var slot = plotWidth / points.Count;
        double Y(int value) => Top + plotHeight - value / (double)max * plotHeight;
        double X(int index) => Left + slot * index + slot / 2;
        string N(double value) => value.ToString("0.#", Css);

        // Recessive grid: the baseline, a half line and the top, each with its value.
        foreach (var value in new[] { 0, max / 2, max }.Distinct())
        {
            svg.Append(CultureInfo.InvariantCulture, $"<line class=\"grid\" x1=\"{N(Left)}\" x2=\"{N(Width - Right)}\" y1=\"{N(Y(value))}\" y2=\"{N(Y(value))}\" />");
            svg.Append(CultureInfo.InvariantCulture, $"<text class=\"axis\" x=\"{N(Left - 6)}\" y=\"{N(Y(value) + 4)}\" text-anchor=\"end\">{value}</text>");
        }
        // Step names under the first, middle and last steps only, so they never collide.
        foreach (var index in new[] { 0, points.Count / 2, points.Count - 1 }.Distinct())
        {
            var anchor = index == 0 ? "start" : index == points.Count - 1 ? "end" : "middle";
            var x = index == 0 ? Left : index == points.Count - 1 ? Width - Right : X(index);
            svg.Append(CultureInfo.InvariantCulture, $"<text class=\"axis\" x=\"{N(x)}\" y=\"{N(Height - 8)}\" text-anchor=\"{anchor}\">{Encode(points[index].Label)}</text>");
        }

        if (bars)
        {
            // Thin bars with a rounded top on a square base; a gap of surface between each.
            var barWidth = Math.Min(28, slot * 0.62);
            for (var i = 0; i < points.Count; i++)
            {
                var (x0, y0, yTop) = (X(i) - barWidth / 2, Y(0), Y(points[i].Value));
                var r = Math.Min(4, Math.Min(barWidth / 2, y0 - yTop));
                svg.Append("<g class=\"slot\">");
                if (points[i].Value > 0)
                    svg.Append(CultureInfo.InvariantCulture, $"<path class=\"mark\" d=\"M{N(x0)},{N(y0)} V{N(yTop + r)} Q{N(x0)},{N(yTop)} {N(x0 + r)},{N(yTop)} H{N(x0 + barWidth - r)} Q{N(x0 + barWidth)},{N(yTop)} {N(x0 + barWidth)},{N(yTop + r)} V{N(y0)} Z\" />");
                Hit(svg, Left + slot * i, slot, points[i].Tip, N);
                svg.Append("</g>");
            }
        }
        else
        {
            svg.Append(CultureInfo.InvariantCulture, $"<polyline class=\"line\" points=\"{string.Join(" ", points.Select((p, i) => $"{N(X(i))},{N(Y(p.Value))}"))}\" />");
            for (var i = 0; i < points.Count; i++)
            {
                svg.Append("<g class=\"slot\">");
                svg.Append(CultureInfo.InvariantCulture, $"<circle class=\"dot\" cx=\"{N(X(i))}\" cy=\"{N(Y(points[i].Value))}\" r=\"4\" />");
                Hit(svg, Left + slot * i, slot, points[i].Tip, N);
                svg.Append("</g>");
            }
        }
        // The latest value, labelled on the chart.
        var last = points.Count - 1;
        svg.Append(CultureInfo.InvariantCulture, $"<text class=\"value\" x=\"{N(X(last))}\" y=\"{N(Y(points[last].Value) - 8)}\" text-anchor=\"middle\">{points[last].Value}</text>");
        return new HtmlString(svg.Append("</svg>").ToString());
    }

    // A full-height target wider than the mark, so hovering anywhere in the step shows its tooltip.
    private static void Hit(StringBuilder svg, double x, double width, string tip, Func<double, string> n) =>
        svg.Append(CultureInfo.InvariantCulture, $"<rect class=\"hit\" x=\"{n(x)}\" y=\"0\" width=\"{n(width)}\" height=\"{n(Height - Bottom)}\"><title>{Encode(tip)}</title></rect>");

    // 1, 2, 5 or 10 times a power of ten, at least the largest value, so the top line is a round number.
    private static int NiceMax(int value)
    {
        if (value <= 4) return Math.Max(value, 2) % 2 == 0 ? Math.Max(value, 2) : value + 1;
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 5, 10 }) if (step * power >= value) return (int)(step * power);
        return value;
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
