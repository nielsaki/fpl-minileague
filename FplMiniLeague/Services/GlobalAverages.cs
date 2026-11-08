using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FplMiniLeague.Services;

/// <summary>
/// The official worldwide average score for each gameweek, from the FPL API's bootstrap-static
/// endpoint (the CSVs don't carry it). A gameweek still being played has only a provisional average,
/// so it's used once FPL marks the gameweek finished.
/// </summary>
public sealed class GlobalAverages(IHttpClientFactory httpFactory, ILogger<GlobalAverages> log)
{
    public const string HttpClientName = "fpl";

    /// <summary>How long to wait before asking FPL again for a gameweek it had no final average for.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<int, int> _averages = new Dictionary<int, int>();
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    /// <summary>Set when the last request to FPL failed.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// The final average for every finished gameweek. FPL is only asked again when one of
    /// <paramref name="wanted"/> has no average yet, and then at most every few minutes.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, int>> GetAsync(IEnumerable<int> wanted, CancellationToken ct = default)
    {
        var needed = wanted.ToList();
        if (needed.All(_averages.ContainsKey))
        {
            return _averages;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (needed.All(_averages.ContainsKey) || DateTimeOffset.UtcNow - _lastAttempt < RetryAfter)
            {
                return _averages;
            }

            _lastAttempt = DateTimeOffset.UtcNow;
            var http = httpFactory.CreateClient(HttpClientName);
            var data = await http.GetFromJsonAsync<BootstrapStatic>("bootstrap-static/", ct);
            _averages = (data?.Events ?? [])
                .Where(e => e.Finished)
                .ToDictionary(e => e.Id, e => e.AverageEntryScore);
            LastError = null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
                                   && ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "Couldn't fetch gameweek averages from the FPL API");
            LastError = "Couldn't reach the FPL API, so global gameweek averages are missing. It will try again in a few minutes.";
        }
        finally
        {
            _lock.Release();
        }

        return _averages;
    }

    private sealed class BootstrapStatic
    {
        [JsonPropertyName("events")]
        public List<FplEvent> Events { get; init; } = [];
    }

    private sealed class FplEvent
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("average_entry_score")]
        public int AverageEntryScore { get; init; }

        [JsonPropertyName("finished")]
        public bool Finished { get; init; }
    }
}
