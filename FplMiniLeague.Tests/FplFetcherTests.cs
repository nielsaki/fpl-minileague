using System.Text.Json;
using FplMiniLeague.Models;
using FplMiniLeague.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FplMiniLeague.Tests;

public sealed class FplFetcherTests : IDisposable
{
    private const int MikPik = 2985395;
    private const int Roi = 3792988;

    private readonly TempDataFolder _data = new($"{MikPik}  # mikkjal\n{Roi}\n");
    private readonly FakeFpl _fpl = new();
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    public FplFetcherTests()
    {
        // GW3 being played; GW4's deadline in the future.
        _fpl.Responses["/api/bootstrap-static/"] = Bootstrap(current: 3, finished: false);
        Team(MikPik, "mikkjal", "jacobsen", "MikPik", (1, 66, 66, 0, 0, 0), (2, 110, 176, 13, 0, 0), (3, 42, 218, 13, 3, 4));
        Team(Roi, "Rói", "Nielsen", "Stolurin", (1, 69, 69, 0, 0, 0), (2, 109, 178, 5, 0, 0), (3, 53, 231, 19, 2, 0));
    }

    public void Dispose() => _data.Dispose();

    private FplFetcher NewFetcher() =>
        new(_fpl, _data.Store, _time, NullLogger<FplFetcher>.Instance) { RetryDelay = TimeSpan.Zero };

    /// <summary>A 5-gameweek season starting 21 Aug 2026 (deadlines a week apart, GW4 on 11 Sep...
    /// shifted so GW4's deadline is after "now").</summary>
    private static string Bootstrap(int current, bool finished, bool dataChecked = false, int firstDeadlineYear = 2026)
    {
        var events = Enumerable.Range(1, 5).Select(id => new
        {
            id,
            deadline_time = id <= current
                ? new DateTimeOffset(firstDeadlineYear, 8, 21, 17, 30, 0, TimeSpan.Zero).AddDays(7 * (id - 1))
                : new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.Zero).AddDays(7 * (id - current - 1)),
            average_entry_score = 50 + id,
            finished = id < current || (id == current && finished),
            data_checked = id < current || (id == current && dataChecked),
            is_current = id == current,
            is_next = id == current + 1,
        });
        return JsonSerializer.Serialize(new
        {
            events,
            chips = new[]
            {
                new { id = 1, name = "wildcard", start_event = 2, stop_event = 3 },
                new { id = 2, name = "wildcard", start_event = 4, stop_event = 5 },
            },
            game_settings = new { max_extra_free_transfers = 4 },
            elements = Array.Empty<object>(),
        });
    }

    private void Team(int id, string first, string last, string team,
        params (int Gw, int Gwp, int Op, int Pb, int Transfers, int Cost)[] rows)
    {
        _fpl.Responses[$"/api/entry/{id}/"] = JsonSerializer.Serialize(new
        {
            id, name = team, player_first_name = first, player_last_name = last,
            started_event = 1, current_event = rows.Max(r => r.Gw),
        });
        _fpl.Responses[$"/api/entry/{id}/history/"] = JsonSerializer.Serialize(new
        {
            current = rows.Select(r => new
            {
                @event = r.Gw, points = r.Gwp, total_points = r.Op, points_on_bench = r.Pb,
                event_transfers = r.Transfers, event_transfers_cost = r.Cost, rank = 1, overall_rank = 1000, value = 1000, bank = 0,
            }),
            past = Array.Empty<object>(),
            chips = new[] { new { name = "wildcard", time = "2026-09-04T10:00:00Z", @event = 2 } },
        });
    }

    [Fact]
    public async Task Fetches_the_teams_in_teams_txt_and_saves_them()
    {
        var result = await NewFetcher().FetchAsync(force: true);

        Assert.True(result.Changed);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(_data.Store.Cache.GamePath));
        Assert.True(File.Exists(_data.Store.Cache.TeamPath(MikPik)));
        Assert.True(File.Exists(_data.Store.Cache.TeamPath(Roi)));

        var snapshot = _data.Store.Current;
        Assert.Equal(["mikkjal jacobsen", "Rói Nielsen"], snapshot.Managers.Select(m => m.Name));
        Assert.Equal(["MikPik", "Stolurin"], snapshot.Managers.Select(m => m.TeamName));
        Assert.Equal([0, 1], snapshot.Managers.Select(m => m.Slot));
        Assert.Equal("2026/27", snapshot.Game!.Season);
        Assert.Equal(5, snapshot.Game.MaxFreeTransfers);
        Assert.Equal(new Dictionary<int, int> { [1] = 51, [2] = 52 }, snapshot.Averages);

        var mik = snapshot.Managers[0];
        Assert.Equal(new GameweekEntry(3, 218, 42, 13, 3, 4, null), mik.Gameweeks[3]);
        Assert.Equal("wildcard", mik.Gameweeks[2].Chip);
        Assert.Equal(_time.Now, snapshot.Status!.UpdatedAt);
    }

    [Fact]
    public async Task Fetching_again_with_nothing_new_changes_no_file()
    {
        await NewFetcher().FetchAsync(force: true);
        var before = Directory.GetFiles(_data.Store.Cache.Folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllText);
        _time.Now = _time.Now.AddMinutes(30);

        var result = await NewFetcher().FetchAsync(force: true);

        Assert.False(result.Changed);
        foreach (var (file, content) in before)
        {
            Assert.Equal(content, File.ReadAllText(file));
        }
    }

    [Fact]
    public async Task While_a_gameweek_is_being_played_every_check_fetches()
    {
        await NewFetcher().FetchAsync(force: true);
        _fpl.Requests.Clear();

        await NewFetcher().FetchAsync(force: false);

        Assert.Contains("/api/bootstrap-static/", _fpl.Requests);
    }

    [Fact]
    public async Task Once_the_gameweek_is_checked_nothing_is_fetched_before_the_next_deadline()
    {
        _fpl.Responses["/api/bootstrap-static/"] = Bootstrap(current: 3, finished: true, dataChecked: true);
        await NewFetcher().FetchAsync(force: true);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.Zero), _data.Store.Current.Status!.RefreshAfter);
        _fpl.Requests.Clear();

        await NewFetcher().FetchAsync(force: false);
        Assert.Empty(_fpl.Requests);

        // The deadline passes: fetch again.
        _time.Now = new DateTimeOffset(2026, 11, 21, 11, 5, 0, TimeSpan.Zero);
        await NewFetcher().FetchAsync(force: false);
        Assert.NotEmpty(_fpl.Requests);
    }

    [Fact]
    public async Task Adding_a_team_to_teams_txt_fetches_it_even_in_a_quiet_week()
    {
        _fpl.Responses["/api/bootstrap-static/"] = Bootstrap(current: 3, finished: true, dataChecked: true);
        _data.WriteTeams($"{MikPik}\n");
        await NewFetcher().FetchAsync(force: true);
        Assert.Single(_data.Store.Current.Managers);

        _data.WriteTeams($"{MikPik}\n{Roi}\n");
        var result = await NewFetcher().FetchAsync(force: false);

        Assert.True(result.Changed);
        Assert.Equal(2, _data.Store.Current.Managers.Count);
    }

    [Fact]
    public async Task Removing_a_team_from_teams_txt_removes_its_data()
    {
        await NewFetcher().FetchAsync(force: true);

        _data.WriteTeams($"{Roi}\n");
        await NewFetcher().FetchAsync(force: false);

        Assert.False(File.Exists(_data.Store.Cache.TeamPath(MikPik)));
        Assert.Equal(["Rói Nielsen"], _data.Store.Current.Managers.Select(m => m.Name));
    }

    [Fact]
    public async Task An_unknown_team_id_fails_and_nothing_is_written()
    {
        _data.WriteTeams($"{MikPik}\n123\n");

        var result = await NewFetcher().FetchAsync(force: true);

        Assert.True(result.Failed);
        Assert.Contains("no team with ID 123", Assert.Single(result.Errors));
        Assert.False(File.Exists(_data.Store.Cache.GamePath));
        Assert.False(File.Exists(_data.Store.Cache.TeamPath(MikPik)));
    }

    [Fact]
    public async Task A_line_that_is_not_an_id_fails_without_asking_fpl()
    {
        _data.WriteTeams("mikkjal\n");

        var result = await NewFetcher().FetchAsync(force: true);

        Assert.True(result.Failed);
        Assert.Empty(_fpl.Requests);
    }

    [Fact]
    public async Task When_fpl_is_down_the_saved_data_is_kept()
    {
        await NewFetcher().FetchAsync(force: true);
        var saved = File.ReadAllText(_data.Store.Cache.TeamPath(MikPik));
        _fpl.Down = true;
        _fpl.Requests.Clear();

        var result = await NewFetcher().FetchAsync(force: true);

        Assert.False(result.Failed);
        Assert.False(result.Changed);
        Assert.Contains("503", Assert.Single(result.Warnings));
        Assert.Equal(saved, File.ReadAllText(_data.Store.Cache.TeamPath(MikPik)));
        Assert.Equal(2, _data.Store.Current.Managers.Count);
        Assert.Equal(3, _fpl.Requests.Count);  // bootstrap-static tried three times
    }

    [Fact]
    public async Task When_fpl_is_down_a_team_without_saved_data_is_an_error()
    {
        _fpl.Down = true;

        var result = await NewFetcher().FetchAsync(force: true);

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task A_new_fpl_season_is_not_fetched_with_last_seasons_team_ids()
    {
        await NewFetcher().FetchAsync(force: true);
        _fpl.Responses["/api/bootstrap-static/"] = Bootstrap(current: 1, finished: false, firstDeadlineYear: 2027);

        var result = await NewFetcher().FetchAsync(force: true);

        Assert.False(result.Changed);
        Assert.Contains("2027/28", Assert.Single(result.Warnings));
        Assert.Equal("2026/27", _data.Store.Current.Game!.Season);

        // Once teams.txt has been edited for the new season, it's fetched.
        _data.WriteTeams($"{Roi}\n");
        Assert.True((await NewFetcher().FetchAsync(force: true)).Changed);
        Assert.Equal("2027/28", _data.Store.Current.Game!.Season);
    }

    [Fact]
    public async Task The_gameweek_being_played_uses_fpls_live_totals()
    {
        // FPL's history still has GW3 at 218 / 42 while the profile shows the live 260 / 84.
        _fpl.Responses[$"/api/entry/{MikPik}/"] =
            """{"name":"MikPik","player_first_name":"mikkjal","player_last_name":"jacobsen","started_event":1,"current_event":3,"summary_overall_points":260,"summary_event_points":84}""";

        await NewFetcher().FetchAsync(force: true);

        var mik = _data.Store.Current.Managers[0];
        Assert.Equal((260, 84, 13), (mik.Gameweeks[3].OverallPoints, mik.Gameweeks[3].GameweekPoints, mik.Gameweeks[3].PointsOnBench));
        Assert.Equal((176, 110), (mik.Gameweeks[2].OverallPoints, mik.Gameweeks[2].GameweekPoints));
    }

    [Theory]
    [InlineData(null, false, false, "first deadline")]
    [InlineData(3, false, false, "current deadline")]
    [InlineData(3, true, false, "current deadline")]
    [InlineData(3, true, true, "next deadline")]
    [InlineData(38, true, true, "never")]
    public void When_fpl_next_needs_asking(int? current, bool finished, bool dataChecked, string expected)
    {
        var game = Build.Game(current: current, currentFinished: finished, dataChecked: dataChecked);

        var after = FplFetcher.RefreshAfter(game);

        var wanted = expected switch
        {
            "first deadline" => game.Events[0].Deadline,
            "current deadline" => game.Current!.Deadline,
            "next deadline" => game.Next!.Deadline,
            _ => (DateTimeOffset?)null,
        };
        Assert.Equal(wanted, after);
    }

    [Theory]
    [InlineData(2026, 8, "2026/27")]
    [InlineData(2027, 8, "2027/28")]
    [InlineData(2099, 8, "2099/00")]
    public void The_season_is_named_after_its_first_deadline(int year, int month, string expected) =>
        Assert.Equal(expected, FplFetcher.SeasonOf(new DateTimeOffset(year, month, 15, 0, 0, 0, TimeSpan.Zero)));
}
