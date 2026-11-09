using System.Globalization;

namespace FplMiniLeague.Models;

/// <summary>One manager's numbers for a single gameweek.</summary>
/// <param name="OverallPoints">OP: season total after this gameweek. FPL has already taken transfer
/// hits off it, so nothing here subtracts them again.</param>
/// <param name="GameweekPoints">GWP, used as-is for the comparison with the global average.</param>
/// <param name="PointsOnBench">PB.</param>
/// <param name="Transfers">Transfers made for this gameweek (0 in a Wildcard or Free Hit week).</param>
/// <param name="TransferCost">Points paid for extra transfers.</param>
/// <param name="Chip">The chip played this gameweek (wildcard, freehit, 3xc, bboost), if any.</param>
public sealed record GameweekEntry(
    int Gameweek, int OverallPoints, int GameweekPoints, int PointsOnBench,
    int Transfers = 0, int TransferCost = 0, string? Chip = null);

/// <summary>Everything known about one manager in the league, keyed by gameweek.</summary>
public sealed class ManagerHistory
{
    public required int EntryId { get; init; }

    public required string Name { get; init; }

    public string TeamName { get; init; } = "";

    /// <summary>The manager's place in teams.txt, which fixes their colour in the graphs.</summary>
    public int Slot { get; init; }

    /// <summary>The first gameweek they played; null if unknown (then their first row is used).</summary>
    public int? StartedEvent { get; init; }

    public required IReadOnlyDictionary<int, GameweekEntry> Gameweeks { get; init; }

    public IReadOnlyList<ChipPlay> Chips { get; init; } = [];

    /// <summary>Identifies the manager in the page markup (graph highlighting).</summary>
    public string Key => EntryId.ToString(CultureInfo.InvariantCulture);

    public string FplUrl => $"https://fantasy.premierleague.com/entry/{EntryId}/history";
}
