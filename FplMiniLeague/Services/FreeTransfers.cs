using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// How many free transfers each manager had at every deadline. FPL only shows this to the manager
/// themselves, so it's worked out from their public history with the season's rules:
/// <list type="bullet">
/// <item>Transfers before a team's first deadline are unlimited. One free transfer for the gameweek after.</item>
/// <item>Each gameweek adds one; unused ones are saved, up to the season's maximum (5 since 2024/25,
/// from FPL's <c>max_extra_free_transfers</c>). Transfers beyond the free ones cost 4 points each and
/// leave none saved.</item>
/// <item>Wildcard and Free Hit: transfers that week are free and don't use the saved ones, which are
/// kept as they were, with no extra one added for that week (rules since 2024/25).</item>
/// <item>One-off top-ups FPL announced outside these rules (not in the API) are listed in <see cref="TopUps"/>.</item>
/// </list>
/// Where FPL's own numbers pin the count down, they win over the calculation: a gameweek with a points
/// hit had exactly (transfers − hit ÷ 4) free transfers, and one with no hit had at least as many as
/// were made.
/// </summary>
public static class FreeTransfers
{
    public const int HitCost = 4;

    /// <summary>
    /// Free-transfer top-ups by season: before that gameweek's deadline every manager was topped up to
    /// the given number. 2025/26: everyone got 5 for gameweek 16 because of the Africa Cup of Nations.
    /// None announced for 2026/27. Add a line here if FPL announces one.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> TopUps =
        new Dictionary<string, IReadOnlyDictionary<int, int>>
        {
            ["2025/26"] = new Dictionary<int, int> { [16] = 5 },
        };

    /// <param name="Available">Free transfers at this gameweek's deadline; null when unlimited
    /// (before the team's first deadline).</param>
    /// <param name="Played">The deadline has passed, so the transfers and chip are known.</param>
    public sealed record Week(int Gameweek, int? Available, int Transfers, int TransferCost, string? Chip, bool Played);

    /// <summary>
    /// The manager's free transfers at every deadline from their first gameweek up to
    /// <paramref name="lastGameweek"/>, normally the next deadline (what they have now).
    /// </summary>
    public static IReadOnlyList<Week> Calculate(ManagerHistory manager, GameInfo game, int lastGameweek)
    {
        var cap = Math.Max(1, game.MaxFreeTransfers);
        var first = manager.StartedEvent ?? (manager.Gameweeks.Count > 0 ? manager.Gameweeks.Keys.Min() : lastGameweek);
        var topUps = TopUps.GetValueOrDefault(game.Season) ?? new Dictionary<int, int>();

        var weeks = new List<Week>();
        int? saved = null;
        for (var gw = first; gw <= lastGameweek; gw++)
        {
            manager.Gameweeks.TryGetValue(gw, out var row);
            var available = saved;
            if (available is int before && topUps.TryGetValue(gw, out var topUp))
            {
                available = Math.Min(cap, Math.Max(before, topUp));
            }

            var chip = row?.Chip;
            var unlimitedChip = chip is "wildcard" or "freehit";
            if (row is not null && available is int calculated && !unlimitedChip)
            {
                if (row.TransferCost > 0 && row.TransferCost % HitCost == 0)
                {
                    available = Math.Clamp(row.Transfers - row.TransferCost / HitCost, 0, cap);
                }
                else if (row.TransferCost == 0 && row.Transfers > calculated)
                {
                    available = Math.Min(row.Transfers, cap);
                }
            }

            weeks.Add(new Week(gw, available, row?.Transfers ?? 0, row?.TransferCost ?? 0, chip, row is not null));

            var made = row?.Transfers ?? 0;
            saved = available switch
            {
                null => 1,
                int a when unlimitedChip => a,
                int a => Math.Min(cap, Math.Max(0, a - made) + 1),
            };
        }

        return weeks;
    }
}
