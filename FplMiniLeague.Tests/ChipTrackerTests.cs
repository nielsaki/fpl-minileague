using FplMiniLeague.Models;
using FplMiniLeague.Services;
using static FplMiniLeague.Services.ChipTracker;

namespace FplMiniLeague.Tests;

public class ChipTrackerTests
{
    private static ManagerHistory Manager(params (string Chip, int Gw)[] plays) => new()
    {
        EntryId = 1,
        Name = "Anna",
        Gameweeks = new Dictionary<int, GameweekEntry>(),
        Chips = plays.Select(p => new ChipPlay(p.Chip, p.Gw)).ToList(),
    };

    private static Cell CellFor(Row row, string chip, int half) =>
        row.Cells.Where(c => c.Column.Name == chip).OrderBy(c => c.Column.StartEvent).ElementAt(half - 1);

    [Fact]
    public void The_seasons_chips_are_grouped_into_halves_in_a_fixed_order()
    {
        var groups = Groups(Build.Game());

        Assert.Equal(["First half", "Second half"], groups.Select(g => g.Label));
        Assert.Equal((1, 19), (groups[0].StartEvent, groups[0].StopEvent));
        Assert.Equal((20, 38), (groups[1].StartEvent, groups[1].StopEvent));
        Assert.Equal(["WC", "FH", "TC", "BB"], groups[0].Columns.Select(c => c.Short));
        Assert.Equal(["WC", "FH", "TC", "BB"], groups[1].Columns.Select(c => c.Short));
    }

    [Fact]
    public void Played_chips_show_their_gameweek_in_the_right_half()
    {
        var row = Assert.Single(Rows([Manager(("bboost", 1), ("wildcard", 6), ("wildcard", 24))], Build.Game(current: 24), 25));

        Assert.Equal((State.Used, 1), (CellFor(row, "bboost", 1).State, CellFor(row, "bboost", 1).UsedIn!.Value));
        Assert.Equal((State.Used, 6), (CellFor(row, "wildcard", 1).State, CellFor(row, "wildcard", 1).UsedIn!.Value));
        Assert.Equal((State.Used, 24), (CellFor(row, "wildcard", 2).State, CellFor(row, "wildcard", 2).UsedIn!.Value));
    }

    [Fact]
    public void In_the_first_half_its_unplayed_chips_are_available_and_the_second_set_comes_later()
    {
        var row = Assert.Single(Rows([Manager(("wildcard", 6))], Build.Game(current: 10), 11));

        Assert.Equal(State.Available, CellFor(row, "freehit", 1).State);
        Assert.Equal(State.Available, CellFor(row, "3xc", 1).State);
        Assert.Equal((State.Later, 20), (CellFor(row, "wildcard", 2).State, CellFor(row, "wildcard", 2).From!.Value));
        Assert.Equal(3, row.AvailableNow);
    }

    [Fact]
    public void First_half_chips_not_played_by_gameweek_19_expire()
    {
        var row = Assert.Single(Rows([Manager(("3xc", 5))], Build.Game(current: 19), 20));

        Assert.Equal(State.Expired, CellFor(row, "bboost", 1).State);
        Assert.Equal(State.Used, CellFor(row, "3xc", 1).State);
        Assert.Equal(State.Available, CellFor(row, "bboost", 2).State);
        Assert.Equal(4, row.AvailableNow);
    }

    [Fact]
    public void A_free_hit_in_gameweek_19_rules_out_the_second_one_in_gameweek_20()
    {
        var row = Assert.Single(Rows([Manager(("freehit", 19))], Build.Game(current: 19), 20));

        var second = CellFor(row, "freehit", 2);
        Assert.Equal((State.Later, 21), (second.State, second.From!.Value));
        Assert.Equal(State.Available, CellFor(row, "wildcard", 2).State);
    }

    [Fact]
    public void Wildcard_and_free_hit_cannot_be_played_in_gameweek_1()
    {
        var row = Assert.Single(Rows([Manager()], Build.Game(), 1));

        Assert.Equal((State.Later, 2), (CellFor(row, "wildcard", 1).State, CellFor(row, "wildcard", 1).From!.Value));
        Assert.Equal(State.Available, CellFor(row, "bboost", 1).State);
    }

    [Fact]
    public void Once_the_season_is_over_unplayed_chips_have_expired()
    {
        var row = Assert.Single(Rows([Manager(("bboost", 30))], Build.Game(current: 38, currentFinished: true), null));

        Assert.All(row.Cells.Where(c => c.State != State.Used), c => Assert.Equal(State.Expired, c.State));
        Assert.Equal(0, row.AvailableNow);
    }

    [Fact]
    public void An_older_season_groups_chips_by_the_gameweek_they_must_be_used_by()
    {
        // Before 2025/26: two Wildcards, one of each other chip.
        var game = Build.Game(chips: [new("wildcard", 2, 19), new("wildcard", 20, 38), new("freehit", 2, 38), new("bboost", 1, 38), new("3xc", 1, 38)]);

        var groups = Groups(game);

        Assert.Equal(["Until GW 19", "Until GW 38"], groups.Select(g => g.Label));
        Assert.Equal(["WC"], groups[0].Columns.Select(c => c.Short));
        Assert.Equal(["WC", "FH", "TC", "BB"], groups[1].Columns.Select(c => c.Short));
    }
}
