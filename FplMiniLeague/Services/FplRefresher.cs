namespace FplMiniLeague.Services;

/// <summary>
/// Keeps the data fresh while the app runs on your own computer: fetches on start-up, every interval
/// (only when something on FPL can have changed, see <see cref="FplFetcher"/>), and straight away
/// when teams.txt gets a new list of IDs.
/// </summary>
public sealed class FplRefresher(FplFetcher fetcher, ManagerStore store, ILogger<FplRefresher> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _teamsEdited = new(0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        store.TeamsChanged += OnTeamsChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await fetcher.FetchAsync(force: false, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Fetching from FPL failed");
                }

                await _teamsEdited.WaitAsync(Interval, stoppingToken);
                while (_teamsEdited.CurrentCount > 0)
                {
                    await _teamsEdited.WaitAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            store.TeamsChanged -= OnTeamsChanged;
        }
    }

    private void OnTeamsChanged() => _teamsEdited.Release();
}
