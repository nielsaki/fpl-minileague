using FplMiniLeague.Models;
using FplMiniLeague.Services;

namespace FplMiniLeague.Tests;

public class LeagueStatisticsTests
{
    private static readonly Dictionary<int, int> Averages = new() { [1] = 50, [2] = 60, [3] = 40 };

    private static ManagerHistory Manager(string name, params (int Gw, int Op, int Gwp, int Pb)[] rows) => new()
    {
        Name = name,
        SourceFile = name + ".csv",
        Gameweeks = rows.ToDictionary(r => r.Gw, r => new GameweekEntry(r.Gw, r.Op, r.Gwp, r.Pb)),
    };

    [Fact]
    public void Ranks_by_overall_points_with_points_behind_the_leader()
    {
        var managers = new[]
        {
            Manager("Bo", (1, 40, 40, 1), (2, 100, 60, 2)),
            Manager("Anna", (1, 60, 60, 5), (2, 130, 70, 0)),
            Manager("Cai", (1, 55, 55, 9), (2, 90, 35, 4)),
        };

        var standings = LeagueStatistics.Standings(managers, 2, Averages);

        Assert.Equal(["Anna", "Bo", "Cai"], standings.Select(s => s.Manager));
        Assert.Equal([1, 2, 3], standings.Select(s => s.Rank!.Value));
        Assert.Equal([0, 30, 40], standings.Select(s => s.PointsBehindLeader!.Value));
    }

    [Fact]
    public void Positions_and_gap_are_for_the_chosen_gameweek()
    {
        var managers = new[]
        {
            Manager("Bo", (1, 40, 40, 0), (2, 100, 60, 0)),
            Manager("Anna", (1, 60, 60, 0), (2, 90, 30, 0)),
        };

        var afterGw1 = LeagueStatistics.Standings(managers, 1, Averages);

        Assert.Equal("Anna", afterGw1[0].Manager);
        Assert.Equal(20, afterGw1[1].PointsBehindLeader);
    }

    [Fact]
    public void Equal_points_share_a_position()
    {
        var managers = new[]
        {
            Manager("Anna", (1, 60, 60, 0)),
            Manager("Bo", (1, 60, 60, 0)),
            Manager("Cai", (1, 50, 50, 0)),
        };

        var standings = LeagueStatistics.Standings(managers, 1, Averages);

        Assert.Equal([1, 1, 3], standings.Select(s => s.Rank!.Value));
        Assert.Equal([0, 0, 10], standings.Select(s => s.PointsBehindLeader!.Value));
    }

    [Fact]
    public void Bench_pain_and_global_average_difference_add_up_over_the_season()
    {
        var anna = Manager("Anna", (1, 60, 60, 5), (2, 126, 70, 3), (3, 156, 30, 11));

        var gw3 = Assert.Single(LeagueStatistics.Standings([anna], 3, Averages));

        Assert.Equal(11, gw3.BenchPoints);
        Assert.Equal(19, gw3.BenchPointsToDate);
        Assert.Equal(-10, gw3.VsAverage);                 // 30 - 40
        Assert.Equal(10 + 10 - 10, gw3.VsAverageToDate);  // (60-50) + (70-60) + (30-40)
        Assert.Equal(3, gw3.VsAverageGameweeks);
    }

    [Fact]
    public void Transfer_hits_are_not_taken_off_twice()
    {
        // GW2: 70 points with a -4 hit. FPL's OP already has the hit deducted (60 + 70 - 4 = 126).
        const string csv = """
            GW,OR,OP,GWP,PB,TC,£m
            1,100000,60,60,0,0,100.0
            2,90000,126,70,0,4,100.2
            """;
        var anna = CsvHistoryParser.Parse(csv, "Anna", "Anna.csv").Manager!;

        var gw2 = Assert.Single(LeagueStatistics.Standings([anna], 2, Averages));

        Assert.Equal(126, gw2.OverallPoints);
        Assert.Equal(70 - 60, gw2.VsAverage);
    }

    [Fact]
    public void A_missing_gameweek_row_is_not_counted_as_zero()
    {
        // No GW2 row for Anna.
        var anna = Manager("Anna", (1, 60, 60, 5), (3, 160, 45, 2));
        var bo = Manager("Bo", (1, 50, 50, 1), (2, 105, 55, 1), (3, 150, 45, 1));

        var gw2 = LeagueStatistics.Standings([anna, bo], 2, Averages);
        var annaGw2 = gw2.Single(s => s.Manager == "Anna");

        // Her latest known total (after GW1) is shown, and marked as such.
        Assert.Equal(60, annaGw2.OverallPoints);
        Assert.Equal(1, annaGw2.OverallPointsGameweek);
        Assert.Null(annaGw2.BenchPoints);
        Assert.Null(annaGw2.VsAverage);
        Assert.Equal(5, annaGw2.BenchPointsToDate);
        Assert.Equal(10, annaGw2.VsAverageToDate);
        Assert.Equal(1, annaGw2.VsAverageGameweeks);

        // Over the season GW2 is left out of her comparison rather than counted as 0 - 60.
        var annaGw3 = LeagueStatistics.Standings([anna, bo], 3, Averages).Single(s => s.Manager == "Anna");
        Assert.Equal(10 + 5, annaGw3.VsAverageToDate);
        Assert.Equal(2, annaGw3.VsAverageGameweeks);
        Assert.Equal(7, annaGw3.BenchPointsToDate);
    }

    [Fact]
    public void A_manager_without_data_yet_is_listed_last_and_unranked()
    {
        var anna = Manager("Anna", (1, 60, 60, 0), (2, 120, 60, 0));
        var late = Manager("Late", (2, 70, 70, 0));

        var gw1 = LeagueStatistics.Standings([late, anna], 1, Averages);

        Assert.Equal(["Anna", "Late"], gw1.Select(s => s.Manager));
        Assert.Null(gw1[1].Rank);
        Assert.Null(gw1[1].OverallPoints);
        Assert.Equal(0, gw1[0].PointsBehindLeader);
    }

    [Fact]
    public void A_gameweek_without_a_final_average_is_left_out_of_the_comparison()
    {
        var anna = Manager("Anna", (1, 60, 60, 0), (2, 120, 60, 0), (3, 150, 30, 0), (4, 200, 50, 0));

        var gw4 = Assert.Single(LeagueStatistics.Standings([anna], 4, Averages));

        Assert.Null(gw4.VsAverage);
        Assert.Equal(10 + 0 - 10, gw4.VsAverageToDate);
        Assert.Equal(3, gw4.VsAverageGameweeks);
    }

    [Fact]
    public void Gameweeks_cover_gaps_between_the_first_and_last()
    {
        var managers = new[] { Manager("Anna", (2, 1, 1, 0), (5, 1, 1, 0)), Manager("Bo", (3, 1, 1, 0)) };

        Assert.Equal([2, 3, 4, 5], LeagueStatistics.Gameweeks(managers));
    }
}
