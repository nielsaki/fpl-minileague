using System.Globalization;
using System.Net;
using System.Text;

namespace FplMiniLeague.Components.Charts;

/// <summary>
/// One manager's small column chart of gameweek points minus the global average: green above the
/// zero line, red below. Every manager's chart shares one scale (<see cref="Limit"/>) so the bars
/// compare across the grid. Hovering a column shows its numbers in the chart's top row (CSS only).
/// </summary>
public static class DiffBarsSvg
{
    public const double Width = 320;
    public const double Height = 136;
    private const double PlotLeft = 34;
    private const double PlotRight = Width - 6;
    private const double PlotTop = 24;
    private const double PlotBottom = Height - 22;
    private const double MaxBar = 12;

    /// <summary>One column: the difference, with the manager's points and the average for the tooltip.</summary>
    public sealed record Bar(int Gameweek, int Difference, int Points, int Average);

    /// <summary>The shared scale: a clean number at least as big as the largest difference.</summary>
    public static double Limit(IEnumerable<int> differences)
    {
        var max = differences.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var step = LineChartLayout.NiceStep(max / 2.0);
        return Math.Max(step, Math.Ceiling(max / step) * step);
    }

    public static string Render(IReadOnlyList<int> gameweeks, IReadOnlyList<Bar> bars, double limit, string description)
    {
        var svg = new StringBuilder();
        Append(svg, $"<svg viewBox=\"0 0 {Width:0} {Height:0}\" class=\"bars-chart\" font-size=\"11\" role=\"img\" aria-label=\"{E(description)}\">");
        if (gameweeks.Count == 0)
        {
            return svg.Append("</svg>").ToString();
        }

        var zeroY = (PlotTop + PlotBottom) / 2;
        var half = (PlotBottom - PlotTop) / 2;
        double Y(double v) => zeroY - v / limit * half;
        var slot = (PlotRight - PlotLeft) / gameweeks.Count;
        var barWidth = Math.Clamp(slot - 2, 1, MaxBar);
        double Center(int gw) => PlotLeft + slot * (gw - gameweeks[0] + 0.5);

        // Grid: the two limits recessive, the zero line stronger.
        svg.Append("<g class=\"grid\">");
        foreach (var v in new[] { limit, -limit })
        {
            Append(svg, $"<line x1=\"{PlotLeft:0.#}\" x2=\"{PlotRight:0.#}\" y1=\"{Y(v):0.#}\" y2=\"{Y(v):0.#}\"/>");
        }

        svg.Append("</g><g class=\"axis\">");
        foreach (var v in new[] { limit, 0, -limit })
        {
            Append(svg, $"<text x=\"{PlotLeft - 6:0.#}\" y=\"{Y(v) + 4:0.#}\" text-anchor=\"end\">{E(Signed((int)v))}</text>");
        }

        int[] labelled = gameweeks.Count > 1 ? [gameweeks[0], gameweeks[^1]] : [gameweeks[0]];
        foreach (var gw in labelled)
        {
            Append(svg, $"<text x=\"{Center(gw):0.#}\" y=\"{Height - 6:0.#}\" text-anchor=\"middle\">GW {gw}</text>");
        }

        Append(svg, $"<line class=\"zero\" x1=\"{PlotLeft:0.#}\" x2=\"{PlotRight:0.#}\" y1=\"{zeroY:0.#}\" y2=\"{zeroY:0.#}\"/>");
        svg.Append("</g>");

        foreach (var bar in bars)
        {
            var x = Center(bar.Gameweek);
            var x0 = x - barWidth / 2;
            var x1 = x + barWidth / 2;
            var end = Y(bar.Difference);
            var kind = bar.Difference switch { > 0 => "pos", < 0 => "neg", _ => "zero" };
            var tip = $"GW {bar.Gameweek}: {Signed(bar.Difference)} ({bar.Points} pts, average {bar.Average})";

            svg.Append("<g class=\"bar\">");
            Append(svg, $"<rect class=\"hit\" fill=\"transparent\" x=\"{x - slot / 2:0.##}\" y=\"{PlotTop - 4:0.#}\" width=\"{slot:0.##}\" height=\"{PlotBottom - PlotTop + 8:0.#}\"/>");
            Append(svg, $"<path class=\"mark {kind}\" d=\"{Column(x0, x1, zeroY, end)}\"/>");
            Append(svg, $"<text class=\"bar-tip\" visibility=\"hidden\" x=\"{PlotLeft:0.#}\" y=\"14\">{E(tip)}</text>");
            svg.Append("</g>");
        }

        return svg.Append("</svg>").ToString();
    }

    public static string Signed(int value) => value switch
    {
        > 0 => $"+{value.ToString(CultureInfo.InvariantCulture)}",
        < 0 => $"−{(-value).ToString(CultureInfo.InvariantCulture)}",
        _ => "0",
    };

    /// <summary>A column from the zero line to <paramref name="end"/>, its data end rounded and its
    /// baseline end square. A zero difference gets a thin sliver on the line so it still shows.</summary>
    internal static string Column(double x0, double x1, double zeroY, double end)
    {
        if (Math.Abs(end - zeroY) < 1.5)
        {
            return string.Create(CultureInfo.InvariantCulture, $"M{x0:0.##},{zeroY - 0.75:0.##}H{x1:0.##}V{zeroY + 0.75:0.##}H{x0:0.##}Z");
        }

        var up = end < zeroY;
        var r = Math.Min(Math.Min(2, (x1 - x0) / 2), Math.Abs(end - zeroY));
        var inner = up ? end + r : end - r;
        return string.Create(CultureInfo.InvariantCulture,
            $"M{x0:0.##},{zeroY:0.##}V{inner:0.##}Q{x0:0.##},{end:0.##} {x0 + r:0.##},{end:0.##}H{x1 - r:0.##}Q{x1:0.##},{end:0.##} {x1:0.##},{inner:0.##}V{zeroY:0.##}Z");
    }

    private static void Append(StringBuilder svg, FormattableString text) => svg.Append(text.ToString(CultureInfo.InvariantCulture));

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
