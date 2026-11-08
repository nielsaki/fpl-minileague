using FplMiniLeague.Components.Charts;

namespace FplMiniLeague.Tests;

public class LineChartTests
{
    private static readonly int[] Gameweeks = [1, 2, 3, 4];

    private static ChartSeries Series(string name, int slot, params (int Gw, double Value)[] values) =>
        new(name + ".csv", name, slot, values.ToDictionary(v => v.Gw, v => v.Value));

    [Fact]
    public void Labels_that_would_overlap_are_pushed_apart_and_keep_their_order()
    {
        var placed = LineChartLayout.SpreadLabels([100, 101, 102, 300], 0, 400, 17);

        Assert.Equal([100, 117, 134, 300], placed);
    }

    [Fact]
    public void Labels_pushed_past_the_bottom_move_back_up()
    {
        var placed = LineChartLayout.SpreadLabels([395, 398], 0, 400, 17);

        Assert.Equal([383, 400], placed);
    }

    [Theory]
    [InlineData(3, 5)]
    [InlineData(32.4, 50)]
    [InlineData(21, 25)]
    [InlineData(0, 1)]
    public void Axis_steps_are_clean_numbers(double raw, double expected) =>
        Assert.Equal(expected, LineChartLayout.NiceStep(raw));

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(21, "21st")]
    public void Positions_read_as_ordinals(int n, string expected) =>
        Assert.Equal(expected, LineChartLayout.Ordinal(n));

    [Fact]
    public void Points_behind_read_as_negative_with_the_leader_at_zero()
    {
        Assert.Equal("0", LineChartLayout.Format(ChartKind.PointsBehindLeader, 0));
        Assert.Equal("−38", LineChartLayout.Format(ChartKind.PointsBehindLeader, -38));
    }

    [Fact]
    public void A_missing_gameweek_breaks_the_line()
    {
        var layout = LineChartLayout.Build(Gameweeks, [Series("Anna", 0, (1, 0), (2, -5), (4, -9))], ChartKind.PointsBehindLeader);

        var path = Assert.Single(layout.Series).Path;
        Assert.Equal(2, path.Count(c => c == 'M'));
        Assert.Equal(1, path.Count(c => c == 'L'));
    }

    [Fact]
    public void The_leader_is_at_the_top_and_first_place_is_at_the_top()
    {
        var behind = LineChartLayout.Build(Gameweeks, [Series("Anna", 0, (1, 0)), Series("Bo", 1, (1, -20))], ChartKind.PointsBehindLeader);
        var position = LineChartLayout.Build(Gameweeks, [Series("Anna", 0, (1, 1)), Series("Bo", 1, (1, 2))], ChartKind.Position);

        Assert.True(behind.Series[0].End.Y < behind.Series[1].End.Y);
        Assert.True(position.Series[0].End.Y < position.Series[1].End.Y);
        Assert.Equal(behind.PlotTop, behind.Series[0].End.Y);
        Assert.Equal(position.PlotTop, position.Series[0].End.Y);
    }

    [Fact]
    public void Each_gameweek_tooltip_lists_everyone_best_first()
    {
        var layout = LineChartLayout.Build(Gameweeks,
            [Series("Bo", 0, (1, -20)), Series("Anna", 1, (1, 0)), Series("Cai", 2, (2, -3))],
            ChartKind.PointsBehindLeader);

        var gw1 = layout.Crosses.Single(c => c.Gameweek == 1);
        Assert.Equal(["Anna", "Bo"], gw1.Rows.Select(r => r.Series.Name));
        Assert.Equal(["0", "−20"], gw1.Rows.Select(r => r.Value));
    }

    [Fact]
    public void Managers_past_eight_reuse_colours_with_a_different_marker()
    {
        var first = Series("A", 0);
        var ninth = Series("I", 8);

        Assert.Equal(first.ColorSlot, ninth.ColorSlot);
        Assert.NotEqual(first.Shape, ninth.Shape);
    }

    [Fact]
    public void Names_are_escaped_in_the_svg()
    {
        var layout = LineChartLayout.Build(Gameweeks, [Series("<script>x</script> & co", 0, (1, 0))], ChartKind.PointsBehindLeader);

        var svg = LineChartSvg.Render(layout, null, "chart");

        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt; &amp; co", svg);
    }
}
