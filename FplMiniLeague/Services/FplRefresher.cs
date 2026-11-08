namespace FplMiniLeague.Services;

/// <summary>
/// Keeps the linked managers' data fresh: on start-up it fetches anyone not updated in the last
/// interval, then fetches everyone again every interval.
/// </summary>
public sealed class FplRefresher(FplEntries entries, ILogger<FplRefresher> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        var staleAfter = (TimeSpan?)Interval;
        do
        {
            try
            {
                await entries.UpdateAllAsync(staleAfter, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Updating managers from FPL failed");
            }

            staleAfter = null;
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
