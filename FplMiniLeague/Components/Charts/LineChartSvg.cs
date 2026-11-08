using System.Globalization;
using System.Net;
using System.Text;

namespace FplMiniLeague.Components.Charts;

/// <summary>
/// Draws a <see cref="LineChartLayout"/> as inline SVG. The hover layer is CSS only (see theme.css,
/// "Charts"): hovering or tabbing to a gameweek shows a hairline and a tooltip listing every manager;
/// hovering a name at the end of a line fades the others.
/// </summary>
public static class LineChartSvg
{
    public static string Render(LineChartLayout layout, string? focusedKey, string description)
    {
        var svg = new StringBuilder();
        var focusClass = focusedKey is null ? "" : " has-focus";
        Append(svg, $"<svg viewBox=\"0 0 {LineChartLayout.Width:0} {layout.Height:0.#}\" class=\"line-chart{focusClass}\" role=\"img\" aria-label=\"{E(description)}\">");

        // Grid and y axis.
        svg.Append("<g class=\"grid\">");
        foreach (var t in layout.YTicks)
        {
            Append(svg, $"<line x1=\"{layout.PlotLeft:0.#}\" x2=\"{layout.PlotRight:0.#}\" y1=\"{t.Position:0.#}\" y2=\"{t.Position:0.#}\"/>");
        }

        svg.Append("</g><g class=\"axis\">");
        foreach (var t in layout.YTicks)
        {
            Append(svg, $"<text x=\"{layout.PlotLeft - 10:0.#}\" y=\"{t.Position + 4:0.#}\" text-anchor=\"end\">{E(t.Label)}</text>");
        }

        Append(svg, $"<line class=\"base\" x1=\"{layout.PlotLeft:0.#}\" x2=\"{layout.PlotRight:0.#}\" y1=\"{layout.PlotBottom + 8:0.#}\" y2=\"{layout.PlotBottom + 8:0.#}\"/>");
        foreach (var t in layout.XTicks)
        {
            Append(svg, $"<text x=\"{t.Position:0.#}\" y=\"{layout.PlotBottom + 26:0.#}\" text-anchor=\"middle\">{E(t.Label)}</text>");
        }

        Append(svg, $"<text class=\"axis-title\" x=\"{(layout.PlotLeft + layout.PlotRight) / 2:0.#}\" y=\"{layout.PlotBottom + 44:0.#}\" text-anchor=\"middle\">Gameweek</text>");
        svg.Append("</g>");

        // One group per manager: line, markers, end label. The end label is inside the group so
        // hovering it highlights the whole line. A highlighted manager is drawn last, on top.
        foreach (var s in layout.Series.OrderBy(s => s.Series.Key == focusedKey))
        {
            var focused = s.Series.Key == focusedKey ? " focused" : "";
            Append(svg, $"<g class=\"series{focused}\" data-key=\"{E(s.Series.Key)}\" style=\"--c: var(--series-{s.Series.ColorSlot})\">");
            Append(svg, $"<path class=\"line\" d=\"{s.Path}\"/>");
            foreach (var p in s.Points)
            {
                Marker(svg, s.Series.Shape, p.X, p.Y, 4);
            }

            var keyX = layout.LabelX;
            if (s.NeedsLeader)
            {
                Append(svg, $"<line class=\"leader\" x1=\"{s.End.X + 6:0.#}\" y1=\"{s.End.Y:0.#}\" x2=\"{keyX - 3:0.#}\" y2=\"{s.LabelY:0.#}\"/>");
            }

            svg.Append("<g class=\"end-label\">");
            Append(svg, $"<line class=\"key\" x1=\"{keyX:0.#}\" x2=\"{keyX + 14:0.#}\" y1=\"{s.LabelY:0.#}\" y2=\"{s.LabelY:0.#}\"/>");
            Marker(svg, s.Series.Shape, keyX + 7, s.LabelY, 3.5);
            Append(svg, $"<text x=\"{keyX + 20:0.#}\" y=\"{s.LabelY + 4:0.#}\"><tspan class=\"name\">{E(s.Series.Name)}</tspan><tspan class=\"value\" dx=\"6\">{E(s.EndValue)}</tspan></text>");
            svg.Append("</g></g>");
        }

        // Hover / focus bands, drawn last so they sit on top of the lines.
        foreach (var c in layout.Crosses)
        {
            var aria = $"Gameweek {c.Gameweek}: " + string.Join(", ", c.Rows.Select(r => $"{r.Series.Name} {r.Value}"));
            Append(svg, $"<g class=\"cross\" tabindex=\"0\" role=\"img\" aria-label=\"{E(aria)}\">");
            Append(svg, $"<rect class=\"hit\" x=\"{c.HitLeft:0.#}\" y=\"{layout.PlotTop - 8:0.#}\" width=\"{c.HitWidth:0.#}\" height=\"{layout.PlotBottom - layout.PlotTop + 40:0.#}\"/>");
            Append(svg, $"<line class=\"hair\" x1=\"{c.X:0.#}\" x2=\"{c.X:0.#}\" y1=\"{layout.PlotTop - 4:0.#}\" y2=\"{layout.PlotBottom + 8:0.#}\"/>");
            svg.Append("<g class=\"tip\">");
            Append(svg, $"<rect class=\"tip-bg\" x=\"{c.TipX:0.#}\" y=\"{c.TipY:0.#}\" width=\"{c.TipWidth:0.#}\" height=\"{c.TipHeight:0.#}\" rx=\"10\"/>");
            Append(svg, $"<text class=\"tip-title\" x=\"{c.TipX + 12:0.#}\" y=\"{c.TipY + 21:0.#}\">Gameweek {c.Gameweek}</text>");
            for (var i = 0; i < c.Rows.Count; i++)
            {
                var row = c.Rows[i];
                var y = c.TipY + 40 + i * 18;
                Append(svg, $"<g style=\"--c: var(--series-{row.Series.ColorSlot})\"><line class=\"key\" x1=\"{c.TipX + 12:0.#}\" x2=\"{c.TipX + 28:0.#}\" y1=\"{y - 4:0.#}\" y2=\"{y - 4:0.#}\"/>");
                Marker(svg, row.Series.Shape, c.TipX + 20, y - 4, 3.5);
                Append(svg, $"<text x=\"{c.TipX + 66:0.#}\" y=\"{y:0.#}\" text-anchor=\"end\" class=\"tip-value\">{E(row.Value)}</text>");
                Append(svg, $"<text x=\"{c.TipX + 74:0.#}\" y=\"{y:0.#}\" class=\"tip-name\">{E(row.Series.Name)}</text></g>");
            }

            svg.Append("</g></g>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>A marker with a 2px ring in the surface colour (set in CSS), so it stays legible where lines cross.</summary>
    private static void Marker(StringBuilder svg, SeriesShape shape, double x, double y, double r)
    {
        switch (shape)
        {
            case SeriesShape.Square:
                Append(svg, $"<rect class=\"mark\" x=\"{x - r:0.#}\" y=\"{y - r:0.#}\" width=\"{2 * r:0.#}\" height=\"{2 * r:0.#}\" rx=\"1\"/>");
                break;
            case SeriesShape.Diamond:
                var d = r * 1.3;
                Append(svg, $"<path class=\"mark\" d=\"M{x:0.#},{y - d:0.#}L{x + d:0.#},{y:0.#}L{x:0.#},{y + d:0.#}L{x - d:0.#},{y:0.#}Z\"/>");
                break;
            default:
                Append(svg, $"<circle class=\"mark\" cx=\"{x:0.#}\" cy=\"{y:0.#}\" r=\"{r:0.#}\"/>");
                break;
        }
    }

    private static void Append(StringBuilder svg, FormattableString text) => svg.Append(text.ToString(CultureInfo.InvariantCulture));

    /// <summary>Manager names come from FPL or file names: always escaped.</summary>
    private static string E(string text) => WebUtility.HtmlEncode(text);
}
