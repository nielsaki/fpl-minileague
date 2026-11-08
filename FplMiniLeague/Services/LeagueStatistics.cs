using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// The dashboard's five statistics, worked out from the managers' CSV histories and the official
/// global gameweek averages.
/// <list type="bullet">
/// <item>Overall points come straight from OP, which already has transfer hits taken off, so TC is
/// never subtracted again.</item>
/// <item>A missing gameweek row is never counted as zero: overall points carry over from the latest
/// earlier row (and say so), and bench and global-average figures just leave that gameweek out.</item>
/// </list>
/// </summary>
public static class LeagueStatistics
{
    /// <summary>
    /// Every gameweek from the earliest to the latest found in any CSV, so a gameweek that no file has a
    /// row for still gets its own page.
    /// </summary>
    public static IReadOnlyList<int> Gameweeks(IEnumerable<ManagerHistory> managers)
    {
        var all = managers.SelectMany(m => m.Gameweeks.Keys).ToList();
        if (all.Count == 0)
        {
            return [];
        }

        var first = all.Min();
        return Enumerable.Range(first, all.Max() - first + 1).ToList();
    }

    /// <summary>
    /// The mini-league as it stood after <paramref name="gameweek"/>: ranked by overall points (equal
    /// points share a position), with managers who have no data yet at the bottom, unranked.
    /// </summary>
    /// <param name="averages">Final global average per gameweek; gameweeks without one are left out of
    /// the comparison.</param>
    public static IReadOnlyList<Standing> Standings(
        IEnumerable<ManagerHistory> managers, int gameweek, IReadOnlyDictionary<int, int> averages)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        var all = managers.Select(m => ForManager(m, gameweek, averages)).ToList();

        var ranked = all
            .Where(s => s.OverallPoints is not null)
            .OrderByDescending(s => s.OverallPoints)
            .ThenBy(s => s.Manager, byName)
            .ToList();
        var leaderPoints = ranked.Count > 0 ? ranked[0].OverallPoints!.Value : 0;

        var result = new List<Standing>(all.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var s = ranked[i];
            var rank = i > 0 && ranked[i - 1].OverallPoints == s.OverallPoints ? result[i - 1].Rank : i + 1;
            result.Add(s with { Rank = rank, PointsBehindLeader = leaderPoints - s.OverallPoints!.Value });
        }

        result.AddRange(all.Where(s => s.OverallPoints is null).OrderBy(s => s.Manager, byName));
        return result;
    }

    /// <summary>GWP minus the global average for one gameweek, or null if either is missing.</summary>
    public static int? VsAverage(ManagerHistory manager, int gameweek, IReadOnlyDictionary<int, int> averages) =>
        manager.Gameweeks.TryGetValue(gameweek, out var entry) && averages.TryGetValue(gameweek, out var average)
            ? entry.GameweekPoints - average
            : null;

    private static Standing ForManager(ManagerHistory manager, int gameweek, IReadOnlyDictionary<int, int> averages)
    {
        var upTo = manager.Gameweeks.Values.Where(e => e.Gameweek <= gameweek).ToList();
        var latest = upTo.MaxBy(e => e.Gameweek);
        manager.Gameweeks.TryGetValue(gameweek, out var current);
        var compared = upTo.Where(e => averages.ContainsKey(e.Gameweek)).ToList();

        return new Standing
        {
            Manager = manager.Name,
            SourceFile = manager.SourceFile,
            OverallPoints = latest?.OverallPoints,
            OverallPointsGameweek = latest?.Gameweek,
            BenchPoints = current?.PointsOnBench,
            BenchPointsToDate = upTo.Sum(e => e.PointsOnBench),
            VsAverage = VsAverage(manager, gameweek, averages),
            VsAverageToDate = compared.Sum(e => e.GameweekPoints - averages[e.Gameweek]),
            VsAverageGameweeks = compared.Count,
        };
    }
}
