using FplMiniLeague.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FplMiniLeague.Tests;

public sealed class FplEntriesTests : IDisposable
{
    private const int MikPik = 2985395;

    private readonly TempDataFolder _data = new();
    private readonly FakeFpl _fpl = new();

    public FplEntriesTests()
    {
        _fpl.Responses[$"/api/entry/{MikPik}/"] =
            """{"id":2985395,"name":"MikPik","player_first_name":"mikkjal","player_last_name":"jacobsen"}""";
        _fpl.Responses[$"/api/entry/{MikPik}/history/"] = History(
            (1, 893714, 66, 66, 0, 0, 1000),
            (2, 280868, 176, 110, 13, 0, 1002),
            (3, 743639, 214, 42, 13, 4, 1005));
    }

    public void Dispose() => _data.Dispose();

    private FplEntries NewEntries() => new(_fpl, _data.Store, NullLogger<FplEntries>.Instance);

    private static string History(params (int Gw, int Or, int Op, int Gwp, int Pb, int Tc, int Value)[] rows) =>
        "{\"current\":[" + string.Join(",", rows.Select(r =>
            $"{{\"event\":{r.Gw},\"points\":{r.Gwp},\"total_points\":{r.Op},\"rank\":1,\"overall_rank\":{r.Or}," +
            $"\"points_on_bench\":{r.Pb},\"event_transfers_cost\":{r.Tc},\"value\":{r.Value},\"bank\":0}}")) +
        "],\"past\":[],\"chips\":[]}";

    [Theory]
    [InlineData("https://fantasy.premierleague.com/en/entry/2985395/history", new[] { 2985395 })]
    [InlineData("https://fantasy.premierleague.com/entry/2985395/event/10", new[] { 2985395 })]
    [InlineData("2985395", new[] { 2985395 })]
    [InlineData("https://fantasy.premierleague.com/entry/1/history\nhttps://fantasy.premierleague.com/entry/2/history", new[] { 1, 2 })]
    [InlineData("https://fantasy.premierleague.com/entry/1/historyhttps://fantasy.premierleague.com/entry/2/history", new[] { 1, 2 })]
    [InlineData("12, 34; 12", new[] { 12, 34 })]
    public void Finds_team_ids_in_pasted_links(string input, int[] expected)
    {
        var (ids, unrecognised) = FplEntries.ParseInput(input);

        Assert.Equal(expected, ids);
        Assert.Empty(unrecognised);
    }

    [Fact]
    public void Reports_text_that_is_not_a_link_or_id()
    {
        var (ids, unrecognised) = FplEntries.ParseInput("https://example.com/team hello");

        Assert.Empty(ids);
        Assert.Equal(["https://example.com/team", "hello"], unrecognised);
    }

    [Fact]
    public async Task Adding_a_link_fetches_the_history_and_stores_it_as_a_csv()
    {
        var entries = NewEntries();

        var messages = await entries.AddAsync("https://fantasy.premierleague.com/en/entry/2985395/history");

        Assert.False(Assert.Single(messages).IsError);
        var link = Assert.Single(entries.Linked);
        Assert.Equal(("mikkjal jacobsen", "MikPik", "mikkjal jacobsen.csv"), (link.PlayerName, link.TeamName, link.File));

        Assert.Equal(
            "GW,OR,OP,GWP,PB,TC,£m\n1,893714,66,66,0,0,100.0\n2,280868,176,110,13,0,100.2\n3,743639,214,42,13,4,100.5\n",
            File.ReadAllText(Path.Combine(_data.Path, "mikkjal jacobsen.csv")));

        var manager = Assert.Single(_data.Store.Current.Managers);
        Assert.Equal("mikkjal jacobsen", manager.Name);
        Assert.Equal(214, manager.Gameweeks[3].OverallPoints);  // OP straight from FPL, hit already deducted
        Assert.Equal(42, manager.Gameweeks[3].GameweekPoints);
        Assert.Equal(13, manager.Gameweeks[3].PointsOnBench);
    }

    [Fact]
    public async Task The_gameweek_being_played_uses_FPLs_live_totals()
    {
        // FPL's history still has GW3 at 214 / 42 while the profile shows the live 260 / 88.
        _fpl.Responses[$"/api/entry/{MikPik}/"] =
            """{"name":"MikPik","player_first_name":"mikkjal","player_last_name":"jacobsen","current_event":3,"summary_overall_points":260,"summary_event_points":88}""";

        await NewEntries().AddAsync(MikPik.ToString());

        var manager = Assert.Single(_data.Store.Current.Managers);
        Assert.Equal((260, 88, 13), (manager.Gameweeks[3].OverallPoints, manager.Gameweeks[3].GameweekPoints, manager.Gameweeks[3].PointsOnBench));
        Assert.Equal((176, 110), (manager.Gameweeks[2].OverallPoints, manager.Gameweeks[2].GameweekPoints));
    }

    [Fact]
    public async Task Links_are_remembered_after_a_restart()
    {
        await NewEntries().AddAsync(MikPik.ToString());

        var afterRestart = NewEntries();

        var link = Assert.Single(afterRestart.Linked);
        Assert.Equal(MikPik, link.EntryId);
        Assert.True(File.Exists(Path.Combine(_data.Path, FplEntries.RegistryFileName)));
    }

    [Fact]
    public async Task Adding_the_same_manager_twice_keeps_one()
    {
        var entries = NewEntries();
        await entries.AddAsync(MikPik.ToString());

        var messages = await entries.AddAsync($"https://fantasy.premierleague.com/entry/{MikPik}/event/3");

        Assert.Single(entries.Linked);
        Assert.Contains("already", Assert.Single(messages).Text);
    }

    [Fact]
    public async Task An_unknown_team_is_reported_and_not_saved()
    {
        var entries = NewEntries();

        var messages = await entries.AddAsync("123");

        Assert.True(Assert.Single(messages).IsError);
        Assert.Contains("no team with ID 123", messages[0].Text);
        Assert.Empty(entries.Linked);
        Assert.Empty(_data.Store.Current.Managers);
    }

    [Fact]
    public async Task Updating_picks_up_new_gameweeks()
    {
        var entries = NewEntries();
        await entries.AddAsync(MikPik.ToString());
        _fpl.Responses[$"/api/entry/{MikPik}/history/"] = History(
            (1, 893714, 66, 66, 0, 0, 1000), (2, 280868, 176, 110, 13, 0, 1002),
            (3, 743639, 214, 42, 13, 4, 1005), (4, 789492, 295, 81, 21, 0, 1005));

        await entries.UpdateAllAsync();

        Assert.Equal(295, Assert.Single(_data.Store.Current.Managers).Gameweeks[4].OverallPoints);
    }

    [Fact]
    public async Task When_FPL_is_down_the_last_fetched_data_is_kept()
    {
        var entries = NewEntries();
        await entries.AddAsync(MikPik.ToString());
        _fpl.Down = true;

        await entries.UpdateAllAsync();

        Assert.NotNull(Assert.Single(entries.Linked).LastError);
        Assert.Equal(3, Assert.Single(_data.Store.Current.Managers).Gameweeks.Count);
    }

    [Fact]
    public async Task Only_stale_managers_are_fetched_when_asked()
    {
        var entries = NewEntries();
        await entries.AddAsync(MikPik.ToString());
        _fpl.Down = true;

        await entries.UpdateAllAsync(staleAfter: TimeSpan.FromMinutes(30));

        Assert.Null(Assert.Single(entries.Linked).LastError);
    }

    [Fact]
    public async Task Removing_a_manager_forgets_the_link_and_deletes_the_data()
    {
        var entries = NewEntries();
        await entries.AddAsync(MikPik.ToString());

        await entries.RemoveAsync(MikPik);

        Assert.Empty(entries.Linked);
        Assert.Empty(_data.Store.Current.Managers);
        Assert.Empty(NewEntries().Linked);
        Assert.False(File.Exists(Path.Combine(_data.Path, "mikkjal jacobsen.csv")));
    }

    [Fact]
    public async Task A_name_already_taken_by_an_imported_csv_gets_the_team_id_added()
    {
        File.WriteAllText(Path.Combine(_data.Path, "mikkjal jacobsen.csv"), "GW,OR,OP,GWP,PB,TC,£m\n1,1,50,50,0,0,100.0\n");
        var entries = NewEntries();

        await entries.AddAsync(MikPik.ToString());

        Assert.Equal($"mikkjal jacobsen ({MikPik}).csv", Assert.Single(entries.Linked).File);
    }
}
