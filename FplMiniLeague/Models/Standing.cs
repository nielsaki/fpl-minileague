namespace FplMiniLeague.Models;

/// <summary>
/// One manager's line in the leaderboard, as of a chosen gameweek. The "to date" figures add up every
/// gameweek up to and including that one; the season view is simply the standings as of the latest
/// gameweek, showing the "to date" figures.
/// </summary>
public sealed record Standing
{
    public required string Manager { get; init; }

    /// <summary>The CSV file the manager's numbers come from.</summary>
    public required string SourceFile { get; init; }

    /// <summary>Mini-league position by overall points; equal points share a position. Null when the
    /// manager's CSV has nothing up to this gameweek (they hadn't started yet).</summary>
    public int? Rank { get; init; }

    public int? OverallPoints { get; init; }

    /// <summary>The gameweek <see cref="OverallPoints"/> comes from. Differs from the chosen gameweek
    /// when that gameweek's row is missing from the CSV and the latest earlier total is shown instead.</summary>
    public int? OverallPointsGameweek { get; init; }

    public int? PointsBehindLeader { get; init; }

    /// <summary>PB for the chosen gameweek; null when the CSV has no row for it.</summary>
    public int? BenchPoints { get; init; }

    /// <summary>Bench pain: PB added up over every gameweek up to the chosen one.</summary>
    public int BenchPointsToDate { get; init; }

    /// <summary>GWP minus the global average for the chosen gameweek; null when the CSV has no row for
    /// it or FPL has no final average for it yet.</summary>
    public int? VsAverage { get; init; }

    /// <summary>GWP minus global average, added up over the gameweeks up to the chosen one that have
    /// both a CSV row and a final average.</summary>
    public int VsAverageToDate { get; init; }

    /// <summary>How many gameweeks <see cref="VsAverageToDate"/> covers.</summary>
    public int VsAverageGameweeks { get; init; }
}
