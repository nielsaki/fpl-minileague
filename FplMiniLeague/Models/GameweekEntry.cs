namespace FplMiniLeague.Models;

/// <summary>One row of a manager's CSV: their numbers for a single gameweek.</summary>
/// <param name="OverallPoints">OP: season total after this gameweek. FPL has already taken transfer
/// hits off it, so nothing here subtracts TC again.</param>
/// <param name="GameweekPoints">GWP, used as-is for the comparison with the global average.</param>
/// <param name="PointsOnBench">PB.</param>
public sealed record GameweekEntry(int Gameweek, int OverallPoints, int GameweekPoints, int PointsOnBench);

/// <summary>Everything one CSV file holds about one manager, keyed by gameweek.</summary>
public sealed class ManagerHistory
{
    public required string Name { get; init; }

    public required string SourceFile { get; init; }

    public required IReadOnlyDictionary<int, GameweekEntry> Gameweeks { get; init; }
}
