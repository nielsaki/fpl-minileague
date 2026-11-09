using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// The leaderboard statistics, worked out from the managers' FPL histories and the official global
/// gameweek averages.
/// <list type="bullet">
/// <item>Overall points come straight from FPL, which already has transfer hits taken off, so hits are
/// never subtracted again.</item>
/// <item>A missing gameweek row is never counted as zero: overall points carry over from the latest
/// earlier row (and say so), and bench and global-average figures just leave that gameweek out.</item>
/// </list>
/// </summary>
public static class LeagueStatistics
{
    /// <summary>
    /// Every gameweek from the earliest to the latest any manager has a row for, so a gameweek that no
    /// one has a row for still gets its own page.
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

    /// <summary>Bench points added up, after each gameweek the manager has a row for.</summary>
    public static IReadOnlyDictionary<int, double> CumulativeBench(ManagerHistory manager)
    {
        var total = 0;
        var result = new Dictionary<int, double>();
        foreach (var entry in manager.Gameweeks.Values.OrderBy(e => e.Gameweek))
        {
            total += entry.PointsOnBench;
            result[entry.Gameweek] = total;
        }

        return result;
    }

    /// <summary>
    /// GWP minus the global average, added up, after each gameweek that has both a row and a final
    /// average. A gameweek still being played has no point yet.
    /// </summary>
    public static IReadOnlyDictionary<int, double> CumulativeVsAverage(ManagerHistory manager, IReadOnlyDictionary<int, int> averages)
    {
        var total = 0;
        var result = new Dictionary<int, double>();
        foreach (var entry in manager.Gameweeks.Values.OrderBy(e => e.Gameweek))
        {
            if (averages.TryGetValue(entry.Gameweek, out var average))
            {
                total += entry.GameweekPoints - average;
                result[entry.Gameweek] = total;
            }
        }

        return result;
    }

    private static Standing ForManager(ManagerHistory manager, int gameweek, IReadOnlyDictionary<int, int> averages)
    {
        var upTo = manager.Gameweeks.Values.Where(e => e.Gameweek <= gameweek).ToList();
        var latest = upTo.MaxBy(e => e.Gameweek);
        manager.Gameweeks.TryGetValue(gameweek, out var current);
        var compared = upTo.Where(e => averages.ContainsKey(e.Gameweek)).ToList();

        return new Standing
        {
            Manager = manager.Name,
            TeamName = manager.TeamName,
            Key = manager.Key,
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
