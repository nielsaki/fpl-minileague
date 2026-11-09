using System.Text.Json.Serialization;

namespace FplMiniLeague.Models;

// What the FPL API is trimmed down to and saved as in data/fpl/ (see FplCache). Only the fields the
// dashboard uses are kept, so the files change only when something on the site would.

/// <summary>Season-wide facts from FPL's bootstrap-static (data/fpl/game.json).</summary>
public sealed record GameInfo
{
    /// <summary>"2026/27", worked out from the gameweek 1 deadline.</summary>
    public required string Season { get; init; }

    /// <summary>Most free transfers a manager can hold: 1 + FPL's <c>max_extra_free_transfers</c>.</summary>
    public int MaxFreeTransfers { get; init; } = 5;

    public IReadOnlyList<GameEvent> Events { get; init; } = [];

    /// <summary>The chips of this season and the gameweeks each can be played in.</summary>
    public IReadOnlyList<ChipWindow> Chips { get; init; } = [];

    /// <summary>The gameweek whose deadline has passed most recently; null before the season starts.</summary>
    [JsonIgnore]
    public GameEvent? Current => Events.FirstOrDefault(e => e.IsCurrent);

    /// <summary>The gameweek with the next deadline; null once the last deadline has passed.</summary>
    [JsonIgnore]
    public GameEvent? Next => Events.FirstOrDefault(e => e.IsNext);

    /// <summary>The official average for every gameweek FPL has marked finished (a gameweek in
    /// progress only has a provisional one).</summary>
    public IReadOnlyDictionary<int, int> FinalAverages() =>
        Events.Where(e => e.Finished).ToDictionary(e => e.Id, e => e.Average);

    public bool IsFinished(int gameweek) => Events.Any(e => e.Id == gameweek && e.Finished);
}

/// <param name="Average">FPL's <c>average_entry_score</c>; provisional until <paramref name="Finished"/>.</param>
/// <param name="DataChecked">FPL has confirmed the gameweek's points; nothing changes after this.</param>
public sealed record GameEvent(
    int Id, DateTimeOffset Deadline, bool Finished, bool DataChecked, int Average, bool IsCurrent, bool IsNext);

/// <param name="Name">FPL's chip name: wildcard, freehit, 3xc or bboost.</param>
public sealed record ChipWindow(string Name, int StartEvent, int StopEvent);

/// <summary>One team's profile and gameweek history (data/fpl/teams/&lt;id&gt;.json).</summary>
public sealed record TeamData
{
    public int Id { get; init; }

    public string PlayerName { get; init; } = "";

    public string TeamName { get; init; } = "";

    /// <summary>The first gameweek the team played; transfers before its deadline were unlimited.</summary>
    public int? StartedEvent { get; init; }

    /// <summary>The gameweek being played (or last played).</summary>
    public int? CurrentEvent { get; init; }

    /// <summary>Live season total, transfer hits already deducted, as FPL's league tables show it.</summary>
    public int? SummaryOverallPoints { get; init; }

    /// <summary>Live points for <see cref="CurrentEvent"/>.</summary>
    public int? SummaryEventPoints { get; init; }

    public IReadOnlyList<TeamGameweek> History { get; init; } = [];

    public IReadOnlyList<ChipPlay> Chips { get; init; } = [];
}

/// <param name="TotalPoints">Season total after this gameweek, transfer hits already deducted.</param>
/// <param name="Points">Gameweek points as FPL reports them.</param>
/// <param name="Transfers">Transfers made for this gameweek. FPL reports 0 for a Wildcard or Free Hit week.</param>
/// <param name="TransfersCost">Points paid for extra transfers (4 each).</param>
public sealed record TeamGameweek(int Event, int Points, int TotalPoints, int PointsOnBench, int Transfers, int TransfersCost);

public sealed record ChipPlay(string Name, int Event);

/// <summary>When the saved data last changed, and when FPL next needs asking (data/fpl/status.json).</summary>
public sealed record FetchStatus
{
    public string? Season { get; init; }

    /// <summary>When anything in the saved data last changed. Only changes with the data, so an
    /// unchanged fetch leaves nothing to commit.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Nothing on FPL can change before this (the next deadline); null when the season is
    /// over. A time in the past means a gameweek is being played and every run should fetch.</summary>
    public DateTimeOffset? RefreshAfter { get; init; }

    /// <summary>SHA-256 of teams.txt when it was last fetched, so an edit is noticed.</summary>
    public string? TeamsFileSha256 { get; init; }
}
