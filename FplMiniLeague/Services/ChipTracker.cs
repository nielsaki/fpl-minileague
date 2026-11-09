using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// Which chips each manager has played and which they still have. The chips and the gameweeks each
/// can be played in come from FPL (bootstrap-static), so the tracker follows the season's own rules:
/// since 2025/26 that's one Wildcard, Free Hit, Triple Captain and Bench Boost for gameweeks 1–19 and
/// another set for 20–38, and first-half chips can't be carried over.
/// </summary>
public static class ChipTracker
{
    public enum State
    {
        Used,
        Available,
        Expired,

        /// <summary>Its gameweeks haven't started yet (second-half chips during the first half).</summary>
        Later,
    }

    /// <summary>One chip of one set, e.g. the first-half Wildcard.</summary>
    public sealed record Column(string Name, string Short, string Label, int StartEvent, int StopEvent);

    /// <summary>A set of chips that share an end gameweek (one half of the season).</summary>
    public sealed record Group(string Label, int StartEvent, int StopEvent, IReadOnlyList<Column> Columns);

    /// <param name="UsedIn">The gameweek it was played in.</param>
    /// <param name="From">For <see cref="State.Later"/>: the first gameweek it can be played in.</param>
    public sealed record Cell(Column Column, State State, int? UsedIn, int? From);

    public sealed record Row(ManagerHistory Manager, IReadOnlyList<Cell> Cells)
    {
        /// <summary>Chips that can be played at the next deadline.</summary>
        public int AvailableNow => Cells.Count(c => c.State == State.Available);
    }

    private static readonly string[] Order = ["wildcard", "freehit", "3xc", "bboost"];

    public static (string Short, string Label) Names(string chip) => chip switch
    {
        "wildcard" => ("WC", "Wildcard"),
        "freehit" => ("FH", "Free Hit"),
        "3xc" => ("TC", "Triple Captain"),
        "bboost" => ("BB", "Bench Boost"),
        "manager" => ("AM", "Assistant Manager"),
        _ => (chip.ToUpperInvariant(), chip),
    };

    /// <summary>The season's chips, grouped into sets by the gameweek they must be used by.</summary>
    public static IReadOnlyList<Group> Groups(GameInfo game)
    {
        var groups = game.Chips
            .GroupBy(c => c.StopEvent)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var columns = g
                    .OrderBy(c => Array.IndexOf(Order, c.Name) is var i and >= 0 ? i : Order.Length)
                    .ThenBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c =>
                    {
                        var (s, label) = Names(c.Name);
                        return new Column(c.Name, s, label, c.StartEvent, c.StopEvent);
                    })
                    .ToList();
                return (Start: g.Min(c => c.StartEvent), Stop: g.Key, Columns: columns);
            })
            .ToList();

        // Two sets of the same chips are the two halves of the season; otherwise (older seasons, where
        // only the Wildcard was split) each set is named by the gameweek it must be used by.
        var halves = groups.Count == 2
                     && groups[0].Columns.Select(c => c.Name).SequenceEqual(groups[1].Columns.Select(c => c.Name));
        return groups
            .Select((g, i) => new Group(
                halves ? (i == 0 ? "First half" : "Second half") : groups.Count == 1 ? "Season" : $"Until GW {g.Stop}",
                g.Start, g.Stop, g.Columns))
            .ToList();
    }

    /// <param name="nextGameweek">The gameweek with the next deadline; null once the season is over.</param>
    public static IReadOnlyList<Row> Rows(IEnumerable<ManagerHistory> managers, GameInfo game, int? nextGameweek)
    {
        var columns = Groups(game).SelectMany(g => g.Columns).ToList();
        return managers.Select(m => new Row(m, CellsFor(m, columns, nextGameweek))).ToList();
    }

    private static List<Cell> CellsFor(ManagerHistory manager, IReadOnlyList<Column> columns, int? next)
    {
        var used = new Dictionary<Column, int>();
        foreach (var play in manager.Chips.OrderBy(p => p.Event))
        {
            // The set whose gameweeks include the play; failing that, the first unused one of its kind.
            var column = columns.FirstOrDefault(c => c.Name == play.Name && !used.ContainsKey(c) && c.StartEvent <= play.Event && play.Event <= c.StopEvent)
                         ?? columns.FirstOrDefault(c => c.Name == play.Name && !used.ContainsKey(c));
            if (column is not null)
            {
                used[column] = play.Event;
            }
        }

        var lastFreeHit = manager.Chips.Where(p => p.Name == "freehit").Select(p => (int?)p.Event).Max();
        return columns.Select(c =>
        {
            if (used.TryGetValue(c, out var gw))
            {
                return new Cell(c, State.Used, gw, null);
            }

            // A Free Hit can't be played the week after another (first-half one in GW19 → not in GW20).
            var from = c.Name == "freehit" && lastFreeHit is int fh ? Math.Max(c.StartEvent, fh + 2) : c.StartEvent;
            if (next is not int n || n > c.StopEvent)
            {
                return new Cell(c, State.Expired, null, null);
            }

            return n < from ? new Cell(c, State.Later, null, from) : new Cell(c, State.Available, null, null);
        }).ToList();
    }
}
