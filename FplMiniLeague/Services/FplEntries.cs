using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FplMiniLeague.Services;

/// <summary>
/// The managers added by pasting their FPL link. The links are remembered in data/managers.json, and
/// each manager's gameweek history is fetched from the FPL API and kept as a CSV beside it (same
/// columns as an exported history: GW, OR, OP, GWP, PB, TC, £m). The dashboard reads those CSVs like
/// any imported one, so it opens instantly and still works when FPL can't be reached.
/// </summary>
public sealed partial class FplEntries
{
    public const string RegistryFileName = "managers.json";

    public sealed record LinkedManager
    {
        public int EntryId { get; init; }

        public string PlayerName { get; init; } = "";

        public string TeamName { get; init; } = "";

        /// <summary>The CSV in the data folder holding this manager's history.</summary>
        public string File { get; init; } = "";

        public DateTimeOffset AddedAt { get; init; }

        public DateTimeOffset? LastUpdated { get; init; }

        /// <summary>Why the last fetch failed; the CSV from the last good fetch is still used.</summary>
        public string? LastError { get; init; }

        [JsonIgnore]
        public string Url => $"https://fantasy.premierleague.com/entry/{EntryId}/history";
    }

    public sealed record Message(bool IsError, string Text);

    private sealed record Registry(List<LinkedManager> Managers);

    private sealed record Fetched(string PlayerName, string TeamName, string Csv, int Gameweeks);

    private sealed class FetchException(string message) : Exception(message);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ManagerStore _store;
    private readonly ILogger<FplEntries> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _registryPath;
    private IReadOnlyList<LinkedManager> _linked;

    public FplEntries(IHttpClientFactory httpFactory, ManagerStore store, ILogger<FplEntries> log)
    {
        _httpFactory = httpFactory;
        _store = store;
        _log = log;
        _registryPath = Path.Combine(store.DataFolder, RegistryFileName);
        _linked = LoadRegistry();
    }

    public IReadOnlyList<LinkedManager> Linked => _linked;

    public bool IsUpdating { get; private set; }

    /// <summary>Raised when a manager is added or removed, and when an update starts or ends.</summary>
    public event Action? Changed;

    /// <summary>
    /// Finds FPL team IDs in pasted text: links such as https://fantasy.premierleague.com/entry/2985395/history
    /// (any page under /entry/ID/ works) or bare IDs, separated by spaces, commas or new lines.
    /// </summary>
    public static (IReadOnlyList<int> Ids, IReadOnlyList<string> Unrecognised) ParseInput(string input)
    {
        var ids = new List<int>();
        var unrecognised = new List<string>();
        foreach (var token in input.Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            // Several links pasted into one line can arrive glued together, so take every match.
            var matches = EntryInLink().Matches(token);
            if (matches.Count > 0)
            {
                ids.AddRange(matches.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
            }
            else if (token.All(char.IsAsciiDigit) && int.TryParse(token, out var id) && id > 0)
            {
                ids.Add(id);
            }
            else
            {
                unrecognised.Add(token);
            }
        }

        return (ids.Distinct().ToList(), unrecognised);
    }

    /// <summary>Adds every manager found in <paramref name="input"/>, fetching their history straight away.</summary>
    public async Task<IReadOnlyList<Message>> AddAsync(string input, CancellationToken ct = default)
    {
        var (ids, unrecognised) = ParseInput(input);
        var messages = unrecognised
            .Select(t => new Message(true, $"\"{(t.Length > 60 ? t[..60] + "…" : t)}\" isn't an FPL team link or ID."))
            .ToList();
        if (ids.Count == 0)
        {
            return messages;
        }

        await _lock.WaitAsync(ct);
        try
        {
            foreach (var id in ids)
            {
                if (_linked.FirstOrDefault(l => l.EntryId == id) is { } existing)
                {
                    messages.Add(new(false, $"{existing.PlayerName} is already in the league."));
                    continue;
                }

                try
                {
                    var fetched = await FetchAsync(id, ct);
                    var file = ChooseFileName(fetched.PlayerName, id);
                    await WriteCsvAsync(file, fetched, ct);
                    var now = DateTimeOffset.Now;
                    _linked = [.. _linked, new LinkedManager
                    {
                        EntryId = id,
                        PlayerName = fetched.PlayerName,
                        TeamName = fetched.TeamName,
                        File = file,
                        AddedAt = now,
                        LastUpdated = now,
                    }];
                    messages.Add(new(false, $"Added {fetched.PlayerName} ({fetched.TeamName})."));
                }
                catch (FetchException ex)
                {
                    messages.Add(new(true, ex.Message));
                }
            }

            await SaveRegistryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }

        _store.Reload();
        Changed?.Invoke();
        return messages;
    }

    /// <summary>
    /// Fetches every linked manager's history again. A manager FPL can't be reached for keeps their
    /// last fetched data, and the error is shown on the Managers page.
    /// </summary>
    /// <param name="staleAfter">Only fetch managers last updated longer ago than this; null fetches all.</param>
    public async Task UpdateAllAsync(TimeSpan? staleAfter = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var due = _linked
                .Where(l => staleAfter is null || l.LastUpdated is null || DateTimeOffset.Now - l.LastUpdated > staleAfter)
                .ToList();
            if (due.Count == 0)
            {
                return;
            }

            IsUpdating = true;
            Changed?.Invoke();

            var updated = new List<LinkedManager>(_linked.Count);
            foreach (var link in _linked)
            {
                if (!due.Contains(link))
                {
                    updated.Add(link);
                    continue;
                }

                try
                {
                    var fetched = await FetchAsync(link.EntryId, ct);
                    await WriteCsvAsync(link.File, fetched, ct);
                    updated.Add(link with
                    {
                        PlayerName = fetched.PlayerName,
                        TeamName = fetched.TeamName,
                        LastUpdated = DateTimeOffset.Now,
                        LastError = null,
                    });
                }
                catch (FetchException ex)
                {
                    updated.Add(link with { LastError = ex.Message });
                }
            }

            _linked = updated;
            await SaveRegistryAsync(ct);
        }
        finally
        {
            IsUpdating = false;
            _lock.Release();
        }

        _store.Reload();
        Changed?.Invoke();
    }

    /// <summary>Forgets a linked manager and deletes their stored history.</summary>
    public async Task RemoveAsync(int entryId)
    {
        await _lock.WaitAsync();
        try
        {
            if (_linked.FirstOrDefault(l => l.EntryId == entryId) is not { } link)
            {
                return;
            }

            _linked = _linked.Where(l => l.EntryId != entryId).ToList();
            await SaveRegistryAsync(CancellationToken.None);
            _store.Delete(link.File);
        }
        finally
        {
            _lock.Release();
        }

        Changed?.Invoke();
    }

    private async Task<Fetched> FetchAsync(int entryId, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(GlobalAverages.HttpClientName);
        try
        {
            var entry = await http.GetFromJsonAsync<EntryDto>($"entry/{entryId}/", ct)
                        ?? throw new FetchException($"FPL sent nothing back for team {entryId}.");
            var history = await http.GetFromJsonAsync<HistoryDto>($"entry/{entryId}/history/", ct)
                          ?? throw new FetchException($"FPL sent no history for team {entryId}.");

            var team = entry.Name.Trim();
            var player = $"{entry.FirstName} {entry.LastName}".Trim();
            if (player.Length == 0)
            {
                player = team.Length > 0 ? team : $"Team {entryId}";
            }

            var rows = WithLiveTotals(history.Current, entry);
            return new Fetched(player, team, ToCsv(rows), rows.Count);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FetchException($"FPL has no team with ID {entryId}.");
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Fetching FPL team {EntryId} failed", entryId);
            throw new FetchException(ex.StatusCode is { } status
                ? $"FPL answered {(int)status} for team {entryId}. Try again later."
                : $"Couldn't reach FPL for team {entryId}.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FetchException($"FPL didn't answer in time for team {entryId}.");
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Unexpected FPL response for team {EntryId}", entryId);
            throw new FetchException($"FPL sent something unexpected for team {entryId}.");
        }
    }

    /// <summary>
    /// While a gameweek is being played, FPL's history lags behind: its row for that gameweek keeps the
    /// points from whenever it was last refreshed, while the team's profile has the live totals that
    /// FPL's own league tables show. So the live overall and gameweek points replace that row's. (Bench
    /// points have no live source, so they stay as the history has them until FPL catches up.)
    /// </summary>
    internal static List<HistoryRowDto> WithLiveTotals(IReadOnlyList<HistoryRowDto> rows, EntryDto entry) =>
        rows.Select(r => entry is { CurrentEvent: int current, SummaryOverallPoints: int total, SummaryEventPoints: int points }
                         && r.Event == current
                ? r with { TotalPoints = total, Points = points }
                : r)
            .ToList();

    /// <summary>The history in the same CSV layout as an exported one, so the same reader handles both.</summary>
    internal static string ToCsv(IEnumerable<HistoryRowDto> rows)
    {
        var csv = new StringBuilder("GW,OR,OP,GWP,PB,TC,£m\n");
        foreach (var r in rows.OrderBy(r => r.Event))
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{r.Event},{r.OverallRank},{r.TotalPoints},{r.Points},{r.PointsOnBench},{r.TransferCost},{r.Value / 10.0:0.0}\n");
        }

        return csv.ToString();
    }

    private async Task WriteCsvAsync(string file, Fetched fetched, CancellationToken ct)
    {
        // A team that hasn't played a gameweek yet has nothing to show; it's fetched again next time.
        if (fetched.Gameweeks == 0)
        {
            return;
        }

        var path = Path.Combine(_store.DataFolder, file);
        try
        {
            if (File.Exists(path) && await File.ReadAllTextAsync(path, ct) == fetched.Csv)
            {
                return;
            }

            // Write beside it and swap in, so the folder watcher never reads a half-written file.
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, fetched.Csv, new UTF8Encoding(false), ct);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException ex)
        {
            _log.LogError(ex, "Couldn't save {File}", file);
            throw new FetchException($"Couldn't save {file}: {ex.Message}");
        }
    }

    /// <summary>"mikkjal jacobsen.csv", or with the team ID added if that name is already taken.</summary>
    private string ChooseFileName(string playerName, int entryId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(playerName.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimStart('.');
        if (name.Length == 0)
        {
            name = $"Team {entryId}";
        }

        var file = name + ".csv";
        var taken = File.Exists(Path.Combine(_store.DataFolder, file))
                    || _linked.Any(l => string.Equals(l.File, file, StringComparison.OrdinalIgnoreCase));
        return taken ? $"{name} ({entryId}).csv" : file;
    }

    private List<LinkedManager> LoadRegistry()
    {
        if (!File.Exists(_registryPath))
        {
            return [];
        }

        try
        {
            var registry = JsonSerializer.Deserialize<Registry>(File.ReadAllText(_registryPath), JsonOptions);
            return registry?.Managers ?? [];
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file rather than overwrite it on the next save.
            var backup = $"{_registryPath}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_registryPath, backup);
            _log.LogError(ex, "{Registry} couldn't be read; copied it to {Backup} and started with no links", _registryPath, backup);
            return [];
        }
    }

    private async Task SaveRegistryAsync(CancellationToken ct)
    {
        var temp = _registryPath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new Registry([.. _linked]), JsonOptions), ct);
        File.Move(temp, _registryPath, overwrite: true);
    }

    [GeneratedRegex(@"entry/(\d{1,9})", RegexOptions.IgnoreCase)]
    private static partial Regex EntryInLink();

    internal sealed class EntryDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("player_first_name")]
        public string FirstName { get; init; } = "";

        [JsonPropertyName("player_last_name")]
        public string LastName { get; init; } = "";

        /// <summary>The gameweek being played (or last played).</summary>
        [JsonPropertyName("current_event")]
        public int? CurrentEvent { get; init; }

        /// <summary>Live season total, transfer hits already deducted, as FPL's league tables show it.</summary>
        [JsonPropertyName("summary_overall_points")]
        public int? SummaryOverallPoints { get; init; }

        /// <summary>Live points for the current gameweek.</summary>
        [JsonPropertyName("summary_event_points")]
        public int? SummaryEventPoints { get; init; }
    }

    private sealed class HistoryDto
    {
        [JsonPropertyName("current")]
        public List<HistoryRowDto> Current { get; init; } = [];
    }

    internal sealed record HistoryRowDto
    {
        [JsonPropertyName("event")]
        public int Event { get; init; }

        [JsonPropertyName("points")]
        public int Points { get; init; }

        [JsonPropertyName("total_points")]
        public int TotalPoints { get; init; }

        [JsonPropertyName("overall_rank")]
        public int? OverallRank { get; init; }

        [JsonPropertyName("points_on_bench")]
        public int PointsOnBench { get; init; }

        [JsonPropertyName("event_transfers_cost")]
        public int TransferCost { get; init; }

        /// <summary>Squad value in tenths of a million (1016 = £101.6m).</summary>
        [JsonPropertyName("value")]
        public int Value { get; init; }
    }
}
