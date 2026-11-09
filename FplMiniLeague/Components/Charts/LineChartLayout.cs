using System.Globalization;

namespace FplMiniLeague.Components.Charts;

public enum ChartKind
{
    /// <summary>0 at the top, falling as a manager loses ground ("−38").</summary>
    PointsBehindLeader,

    /// <summary>Mini-league position, 1st at the top.</summary>
    Position,

    /// <summary>A running total from 0 at the bottom (bench points added up).</summary>
    Total,

    /// <summary>Above or below a zero baseline ("+12", "−8"), the baseline drawn stronger.</summary>
    Difference,
}

public enum SeriesShape
{
    Circle,
    Square,
    Diamond,
}

/// <summary>One manager's line: a value for each gameweek they have data for.</summary>
/// <param name="Slot">The manager's place in the fixed colour order. The palette has eight hues;
/// managers 9–16 reuse them with square markers, 17–24 with diamonds, so colour is never the only key.</param>
public sealed record ChartSeries(string Key, string Name, int Slot, IReadOnlyDictionary<int, double> Values)
{
    public int ColorSlot => Slot % 8 + 1;

    public SeriesShape Shape => (SeriesShape)(Slot / 8 % 3);
}

/// <param name="IsBaseline">The zero line of a <see cref="ChartKind.Difference"/> chart.</param>
public sealed record Tick(double Position, string Label, bool IsBaseline = false);

public sealed record ChartPoint(int Gameweek, double X, double Y, double Value);

public sealed record SeriesLayout(
    ChartSeries Series, string Path, IReadOnlyList<ChartPoint> Points, ChartPoint End,
    double LabelY, bool NeedsLeader, string EndValue);

public sealed record TipRow(ChartSeries Series, string Value);

/// <summary>The hover target for one gameweek: a band across the plot, its hairline and its tooltip.</summary>
public sealed record CrossLayout(
    int Gameweek, double X, double HitLeft, double HitWidth,
    double TipX, double TipY, double TipWidth, double TipHeight, IReadOnlyList<TipRow> Rows);

/// <summary>
/// Positions for a gameweek line chart: scales, ticks, each manager's path and end label, and the
/// per-gameweek hover bands. Pure geometry, drawn by <see cref="LineChartSvg"/>.
/// </summary>
public sealed class LineChartLayout
{
    public const double Width = 1000;
    public const double LabelRowHeight = 17;
    private const double Left = 64;
    private const double Top = 16;
    private const double Bottom = 50;
    private const double LabelGap = 16;
    private const double CharWidth = 7;
    private const double InnerPad = 10;

    private LineChartLayout()
    {
    }

    public ChartKind Kind { get; private init; }

    public double Height { get; private init; }

    public double PlotLeft { get; private init; }

    public double PlotRight { get; private init; }

    public double PlotTop { get; private init; }

    public double PlotBottom { get; private init; }

    public double LabelX => PlotRight + LabelGap;

    public IReadOnlyList<Tick> XTicks { get; private init; } = [];

    public IReadOnlyList<Tick> YTicks { get; private init; } = [];

    public IReadOnlyList<SeriesLayout> Series { get; private init; } = [];

    public IReadOnlyList<CrossLayout> Crosses { get; private init; } = [];

    public static LineChartLayout Build(IReadOnlyList<int> gameweeks, IReadOnlyList<ChartSeries> series, ChartKind kind)
    {
        var drawn = series.Where(s => s.Values.Count > 0).ToList();
        var values = drawn.SelectMany(s => s.Values.Values).ToList();

        // Leave room on the right for the widest end label: key, name and value.
        var labelWidth = drawn.Count == 0
            ? 120
            : drawn.Max(s => (s.Name.Length + Format(kind, s.Values[s.Values.Keys.Max()]).Length + 2) * CharWidth + 22);
        var plotLeft = Left;
        var plotRight = Width - Math.Max(labelWidth, 120) - LabelGap - 4;
        var plotTop = Top;

        // Position: one row per place. Points behind: a fixed height.
        var places = kind == ChartKind.Position ? Math.Max(1, (int)(values.Count > 0 ? values.Max() : 1)) : 0;
        double plotHeight = kind == ChartKind.Position
            ? Math.Clamp(places * 32, 180, 640)
            : 360;
        plotHeight = Math.Max(plotHeight, drawn.Count * LabelRowHeight);
        var plotBottom = plotTop + plotHeight;

        // ---- x: one position per gameweek ----
        var first = gameweeks.Count > 0 ? gameweeks[0] : 1;
        var last = gameweeks.Count > 0 ? gameweeks[^1] : 1;
        double X(int gw) => first == last
            ? (plotLeft + plotRight) / 2
            : plotLeft + InnerPad + (gw - first) / (double)(last - first) * (plotRight - plotLeft - 2 * InnerPad);
        var xStep = gameweeks.Count <= 20 ? 1 : gameweeks.Count <= 40 ? 2 : 5;
        var xTicks = gameweeks
            .Where(gw => (gw - first) % xStep == 0)
            .Select(gw => new Tick(X(gw), gw.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        // ---- y ----
        Func<double, double> y;
        List<Tick> yTicks;
        if (kind == ChartKind.Position)
        {
            y = v => places == 1 ? (plotTop + plotBottom) / 2 : plotTop + (v - 1) / (places - 1) * plotHeight;
            var step = places <= 20 ? 1 : 2;
            yTicks = Enumerable.Range(1, places)
                .Where(p => (p - 1) % step == 0)
                .Select(p => new Tick(y(p), Ordinal(p)))
                .ToList();
        }
        else
        {
            // A clean range that always includes 0: [floor, 0] for points behind, [0, top] for totals,
            // and both sides of 0 for differences.
            var low = values.Count > 0 ? Math.Min(0, values.Min()) : 0;
            var high = values.Count > 0 ? Math.Max(0, values.Max()) : 0;
            var step = NiceStep((high - low) / (kind == ChartKind.Difference ? 6 : 5));
            var floor = Math.Floor(low / step) * step;
            var top = Math.Ceiling(high / step) * step;
            if (top - floor < step)
            {
                // All zero: still show one step of room.
                if (kind == ChartKind.PointsBehindLeader)
                {
                    floor = -step;
                }
                else
                {
                    top = step;
                }
            }

            y = v => plotTop + (top - v) / (top - floor) * plotHeight;
            yTicks = [];
            for (var t = top; t >= floor - 0.001; t -= step)
            {
                yTicks.Add(new Tick(y(t), Format(kind, t), kind == ChartKind.Difference && Math.Abs(t) < 0.001));
            }
        }

        // ---- each manager's line ----
        var layouts = new List<SeriesLayout>();
        foreach (var s in drawn)
        {
            var points = new List<ChartPoint>();
            var path = new System.Text.StringBuilder();
            var previousDrawn = false;
            foreach (var gw in gameweeks)
            {
                if (!s.Values.TryGetValue(gw, out var v))
                {
                    // A gameweek without data breaks the line rather than bridging it.
                    previousDrawn = false;
                    continue;
                }

                var point = new ChartPoint(gw, X(gw), y(v), v);
                points.Add(point);
                path.Append(CultureInfo.InvariantCulture, $"{(previousDrawn ? "L" : "M")}{point.X:0.#},{point.Y:0.#} ");
                previousDrawn = true;
            }

            var end = points[^1];
            layouts.Add(new SeriesLayout(s, path.ToString().TrimEnd(), points, end, end.Y, false, Format(kind, end.Value)));
        }

        // End labels sit in a column on the right at their line's height, pushed apart where they'd
        // overlap; a hairline leads back to the line from any label that had to move.
        var labelYs = SpreadLabels(layouts.Select(l => l.End.Y).ToList(), plotTop - 4, plotBottom + 4, LabelRowHeight);
        layouts = layouts
            .Select((l, i) => l with
            {
                LabelY = labelYs[i],
                NeedsLeader = Math.Abs(labelYs[i] - l.End.Y) > 1.5 || l.End.Gameweek != last,
            })
            .ToList();

        // ---- hover bands, one per gameweek ----
        var xs = gameweeks.Select(X).ToList();
        var crosses = new List<CrossLayout>();
        for (var i = 0; i < gameweeks.Count; i++)
        {
            var gw = gameweeks[i];
            var rows = drawn
                .Where(s => s.Values.ContainsKey(gw))
                // Best first: 1st place, the leader, the most points (or the most bench pain).
                .OrderBy(s => kind == ChartKind.Position ? s.Values[gw] : -s.Values[gw])
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(s => new TipRow(s, Format(kind, s.Values[gw])))
                .ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var hitLeft = i == 0 ? plotLeft - 6 : (xs[i - 1] + xs[i]) / 2;
            var hitRight = i == gameweeks.Count - 1 ? plotRight + 6 : (xs[i] + xs[i + 1]) / 2;
            var tipWidth = 84 + rows.Max(r => r.Series.Name.Length) * CharWidth;
            var tipHeight = 34 + rows.Count * 18;
            // Open towards the middle of the plot, so the tooltip never covers the end labels.
            var tipX = xs[i] > (plotLeft + plotRight) / 2 ? xs[i] - 14 - tipWidth : xs[i] + 14;
            crosses.Add(new CrossLayout(gw, xs[i], hitLeft, hitRight - hitLeft, tipX, plotTop, tipWidth, tipHeight, rows));
        }

        return new LineChartLayout
        {
            Kind = kind,
            Height = plotBottom + Bottom,
            PlotLeft = plotLeft,
            PlotRight = plotRight,
            PlotTop = plotTop,
            PlotBottom = plotBottom,
            XTicks = xTicks,
            YTicks = yTicks,
            Series = layouts,
            Crosses = crosses,
        };
    }

    public static string Format(ChartKind kind, double value) => kind switch
    {
        ChartKind.Position => Ordinal((int)value),
        _ when value == 0 => "0",
        ChartKind.Total => value.ToString("0", CultureInfo.InvariantCulture),
        _ when value > 0 => $"+{value.ToString("0", CultureInfo.InvariantCulture)}",
        _ => $"−{(-value).ToString("0", CultureInfo.InvariantCulture)}",
    };

    public static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" },
    };

    /// <summary>The smallest clean axis step (1, 2, 5, 10, 20, 25, 50, …) at least <paramref name="raw"/>.</summary>
    public static double NiceStep(double raw)
    {
        double[] steps = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000];
        return steps.FirstOrDefault(s => s >= raw, 10000);
    }

    /// <summary>
    /// Moves label positions apart so no two are closer than <paramref name="gap"/>, keeping each as
    /// near its own line as possible and the whole column within [<paramref name="min"/>, <paramref name="max"/>]
    /// when it fits. Returns the positions in the input order.
    /// </summary>
    public static IReadOnlyList<double> SpreadLabels(IReadOnlyList<double> targets, double min, double max, double gap)
    {
        var order = Enumerable.Range(0, targets.Count).OrderBy(i => targets[i]).ToList();
        var placed = new double[targets.Count];
        for (var k = 0; k < order.Count; k++)
        {
            var wanted = Math.Max(targets[order[k]], min);
            placed[order[k]] = k == 0 ? wanted : Math.Max(wanted, placed[order[k - 1]] + gap);
        }

        for (var k = order.Count - 1; k >= 0; k--)
        {
            var limit = k == order.Count - 1 ? max : placed[order[k + 1]] - gap;
            placed[order[k]] = Math.Min(placed[order[k]], limit);
        }

        return placed;
    }
}
