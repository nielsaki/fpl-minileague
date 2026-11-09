using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// Fetches the league from the public FPL API and saves it in data/fpl/ (<see cref="FplCache"/>):
/// bootstrap-static once (gameweeks, global averages, chips, transfer limit), then each team in
/// teams.txt (entry/ID/ and entry/ID/history/). No login or key is needed.
/// <para>
/// FPL is only asked when something could have changed: while a gameweek is being played or checked,
/// when teams.txt has been edited, or when <c>force</c> is set (the Update button, a manual run).
/// Between a gameweek's final check and the next deadline nothing on FPL changes, so nothing is
/// fetched. Everything is fetched before anything is written, so a failed fetch never leaves half the
/// teams updated; when FPL can't be reached the saved data stays as it was.
/// </para>
/// </summary>
public sealed class FplFetcher(IHttpClientFactory httpFactory, ManagerStore store, TimeProvider clock, ILogger<FplFetcher> log)
{
    public const string HttpClientName = "fpl";

    /// <param name="Changed">Something in data/fpl/ was written or removed.</param>
    /// <param name="Errors">The league can't be shown as teams.txt lists it (a bad line, an unknown
    /// team, or a new team while FPL can't be reached).</param>
    /// <param name="Warnings">Worth knowing, but the saved data is still complete.</param>
    public sealed record Result(bool Changed, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public bool Failed => Errors.Count > 0;
    }

    private sealed class TeamNotFoundException : Exception;

    private sealed class FplUnavailableException(string message) : Exception(message);

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Wait before retrying a request FPL didn't answer (doubled on the second retry).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    public bool IsFetching { get; private set; }

    public Result? LastResult { get; private set; }

    /// <summary>Raised when a fetch starts and ends.</summary>
    public event Action? Changed;

    /// <param name="force">Fetch even if nothing on FPL can have changed since the last fetch.</param>
    public async Task<Result> FetchAsync(bool force, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            IsFetching = true;
            Changed?.Invoke();
            LastResult = await FetchCoreAsync(force, ct);
            foreach (var warning in LastResult.Warnings)
            {
                log.LogWarning("{Warning}", warning);
            }

            foreach (var error in LastResult.Errors)
            {
                log.LogError("{Error}", error);
            }

            return LastResult;
        }
        finally
        {
            IsFetching = false;
            _lock.Release();
            store.Reload();
            Changed?.Invoke();
        }
    }

    private async Task<Result> FetchCoreAsync(bool force, CancellationToken ct)
    {
        var teams = TeamList.Read(store.TeamsFile);
        if (teams.Problems.Count > 0)
        {
            return new(false, teams.Problems, []);
        }

        var cache = store.Cache;
        var hash = TeamList.Hash(store.TeamsFile);
        var status = TryRead<FetchStatus>(cache.StatusPath);
        var saved = cache.SavedTeams();
        var complete = TryRead<GameInfo>(cache.GamePath) is not null && teams.Ids.All(saved.Contains);
        var now = clock.GetUtcNow();

        if (!force && complete && status?.TeamsFileSha256 == hash && (status.RefreshAfter is null || status.RefreshAfter > now))
        {
            log.LogInformation("Nothing on FPL can have changed yet (next check after {Time}); not fetching", status.RefreshAfter);
            return new(false, [], []);
        }

        GameInfo game;
        var fetched = new List<TeamData>();
        try
        {
            var http = httpFactory.CreateClient(HttpClientName);
            game = ToGame(await GetAsync<BootstrapDto>(http, "bootstrap-static/", ct));

            // Team IDs are per season: last season's IDs belong to other people now.
            if (status?.Season is { } savedSeason && savedSeason != game.Season && status.TeamsFileSha256 == hash && complete)
            {
                return new(false, [], [
                    $"FPL has started the {game.Season} season. Team IDs change every season, so put this season's IDs in teams.txt; until then the site keeps showing {savedSeason}.",
                ]);
            }

            var unknown = new List<string>();
            foreach (var id in teams.Ids)
            {
                try
                {
                    fetched.Add(await FetchTeamAsync(http, id, ct));
                }
                catch (TeamNotFoundException)
                {
                    unknown.Add($"FPL has no team with ID {id} this season. Check the ID in teams.txt.");
                }
            }

            if (unknown.Count > 0)
            {
                return new(false, unknown, []);
            }
        }
        catch (FplUnavailableException ex)
        {
            return complete
                ? new(false, [], [$"{ex.Message} The saved data{(status?.UpdatedAt is { } at ? $" from {at:yyyy-MM-dd HH:mm} UTC" : "")} is kept."])
                : new(false, [$"{ex.Message} Teams without saved data can't be shown until FPL answers."], []);
        }

        var changed = FplCache.Write(cache.GamePath, game);
        foreach (var team in fetched)
        {
            changed |= FplCache.Write(cache.TeamPath(team.Id), team);
        }

        foreach (var removed in saved.Except(teams.Ids))
        {
            File.Delete(cache.TeamPath(removed));
            changed = true;
        }

        changed |= FplCache.Write(cache.StatusPath, new FetchStatus
        {
            Season = game.Season,
            UpdatedAt = changed || status?.UpdatedAt is null ? now : status.UpdatedAt,
            RefreshAfter = RefreshAfter(game),
            TeamsFileSha256 = hash,
        });

        log.LogInformation("Fetched {Teams} teams from FPL: {Outcome}", fetched.Count, changed ? "data changed" : "nothing new");
        return new(changed, [], []);
    }

    /// <summary>
    /// When FPL next needs asking. While a gameweek is being played or checked (not yet finished and
    /// data-checked), every run fetches: the gameweek's deadline is returned, which is already past.
    /// Once it's checked, nothing changes until the next deadline. Null once the season is over.
    /// </summary>
    internal static DateTimeOffset? RefreshAfter(GameInfo game)
    {
        if (game.Current is not { } current)
        {
            return (game.Next ?? game.Events.FirstOrDefault())?.Deadline;
        }

        return current is { Finished: true, DataChecked: true } ? game.Next?.Deadline : current.Deadline;
    }

    /// <summary>"2026/27" for a season whose first deadline is in August 2026.</summary>
    internal static string SeasonOf(DateTimeOffset firstDeadline)
    {
        var start = firstDeadline.Month >= 7 ? firstDeadline.Year : firstDeadline.Year - 1;
        return $"{start}/{(start + 1) % 100:00}";
    }

    private static GameInfo ToGame(BootstrapDto dto)
    {
        var events = dto.Events
            .OrderBy(e => e.Id)
            .Select(e => new GameEvent(e.Id, e.DeadlineTime, e.Finished, e.DataChecked, e.AverageEntryScore, e.IsCurrent, e.IsNext))
            .ToList();
        return new GameInfo
        {
            Season = events.Count > 0 ? SeasonOf(events[0].Deadline) : "",
            MaxFreeTransfers = 1 + (dto.GameSettings?.MaxExtraFreeTransfers ?? 4),
            Events = events,
            Chips = dto.Chips.OrderBy(c => c.Id).Select(c => new ChipWindow(c.Name, c.StartEvent, c.StopEvent)).ToList(),
        };
    }

    private async Task<TeamData> FetchTeamAsync(HttpClient http, int id, CancellationToken ct)
    {
        var entry = await GetAsync<EntryDto>(http, $"entry/{id}/", ct);
        var history = await GetAsync<HistoryDto>(http, $"entry/{id}/history/", ct);
        return new TeamData
        {
            Id = id,
            PlayerName = $"{entry.FirstName} {entry.LastName}".Trim(),
            TeamName = entry.Name.Trim(),
            StartedEvent = entry.StartedEvent,
            CurrentEvent = entry.CurrentEvent,
            SummaryOverallPoints = entry.SummaryOverallPoints,
            SummaryEventPoints = entry.SummaryEventPoints,
            History = history.Current
                .OrderBy(r => r.Event)
                .Select(r => new TeamGameweek(r.Event, r.Points, r.TotalPoints, r.PointsOnBench, r.Transfers, r.TransferCost))
                .ToList(),
            Chips = history.Chips.OrderBy(c => c.Event).Select(c => new ChipPlay(c.Name, c.Event)).ToList(),
        };
    }

    /// <summary>One request, tried up to three times: FPL often answers 503 for a while around deadlines
    /// ("the game is being updated").</summary>
    private async Task<T> GetAsync<T>(HttpClient http, string path, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(path, ct);
                if (response.StatusCode == HttpStatusCode.NotFound && path.StartsWith("entry/", StringComparison.Ordinal))
                {
                    throw new TeamNotFoundException();
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>(ct)
                       ?? throw new JsonException($"FPL sent an empty answer for {path}.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                if (attempt >= 3)
                {
                    throw new FplUnavailableException(Describe(ex, path));
                }

                log.LogWarning(ex, "FPL request {Path} failed (attempt {Attempt}); trying again", path, attempt);
                await Task.Delay(RetryDelay * attempt, ct);
            }
        }
    }

    private static string Describe(Exception ex, string path) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable } =>
            "FPL answered 503 (it's usually being updated around a deadline).",
        HttpRequestException { StatusCode: { } status } => $"FPL answered {(int)status} for {path}.",
        HttpRequestException => "Couldn't reach the FPL API.",
        JsonException => $"FPL sent something unexpected for {path}.",
        _ => $"FPL didn't answer in time for {path}.",
    };

    private T? TryRead<T>(string path) where T : class
    {
        try
        {
            return FplCache.Read<T>(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            log.LogWarning(ex, "Couldn't read {File}; it will be replaced", path);
            return null;
        }
    }

    // ---- FPL API shapes (only the fields used) ----

    private sealed class BootstrapDto
    {
        [JsonPropertyName("events")]
        public List<EventDto> Events { get; init; } = [];

        [JsonPropertyName("chips")]
        public List<ChipDto> Chips { get; init; } = [];

        [JsonPropertyName("game_settings")]
        public GameSettingsDto? GameSettings { get; init; }
    }

    private sealed class EventDto
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("deadline_time")]
        public DateTimeOffset DeadlineTime { get; init; }

        [JsonPropertyName("average_entry_score")]
        public int AverageEntryScore { get; init; }

        [JsonPropertyName("finished")]
        public bool Finished { get; init; }

        [JsonPropertyName("data_checked")]
        public bool DataChecked { get; init; }

        [JsonPropertyName("is_current")]
        public bool IsCurrent { get; init; }

        [JsonPropertyName("is_next")]
        public bool IsNext { get; init; }
    }

    private sealed class ChipDto
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("start_event")]
        public int StartEvent { get; init; }

        [JsonPropertyName("stop_event")]
        public int StopEvent { get; init; }
    }

    private sealed class GameSettingsDto
    {
        [JsonPropertyName("max_extra_free_transfers")]
        public int? MaxExtraFreeTransfers { get; init; }
    }

    private sealed class EntryDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("player_first_name")]
        public string FirstName { get; init; } = "";

        [JsonPropertyName("player_last_name")]
        public string LastName { get; init; } = "";

        [JsonPropertyName("started_event")]
        public int? StartedEvent { get; init; }

        [JsonPropertyName("current_event")]
        public int? CurrentEvent { get; init; }

        [JsonPropertyName("summary_overall_points")]
        public int? SummaryOverallPoints { get; init; }

        [JsonPropertyName("summary_event_points")]
        public int? SummaryEventPoints { get; init; }
    }

    private sealed class HistoryDto
    {
        [JsonPropertyName("current")]
        public List<HistoryRowDto> Current { get; init; } = [];

        [JsonPropertyName("chips")]
        public List<ChipPlayDto> Chips { get; init; } = [];
    }

    private sealed class HistoryRowDto
    {
        [JsonPropertyName("event")]
        public int Event { get; init; }

        [JsonPropertyName("points")]
        public int Points { get; init; }

        [JsonPropertyName("total_points")]
        public int TotalPoints { get; init; }

        [JsonPropertyName("points_on_bench")]
        public int PointsOnBench { get; init; }

        [JsonPropertyName("event_transfers")]
        public int Transfers { get; init; }

        [JsonPropertyName("event_transfers_cost")]
        public int TransferCost { get; init; }
    }

    private sealed class ChipPlayDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("event")]
        public int Event { get; init; }
    }
}
