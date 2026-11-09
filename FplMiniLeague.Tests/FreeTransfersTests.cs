using FplMiniLeague.Services;

namespace FplMiniLeague.Tests;

public class FreeTransfersTests
{
    private static int?[] Available(IEnumerable<FreeTransfers.Week> weeks) => weeks.Select(w => w.Available).ToArray();

    [Fact]
    public void Unlimited_before_the_first_deadline_then_one_more_each_gameweek()
    {
        var anna = Build.Transfers(1, (1, 0, 0, null), (2, 0, 0, null), (3, 0, 0, null));

        var weeks = FreeTransfers.Calculate(anna, Build.Game(current: 3), 4);

        Assert.Equal([null, 1, 2, 3], Available(weeks));
        Assert.Equal([true, true, true, false], weeks.Select(w => w.Played));
    }

    [Fact]
    public void Saved_transfers_stop_at_the_seasons_maximum()
    {
        var anna = Build.Transfers(1, Enumerable.Range(1, 8).Select(gw => (gw, 0, 0, (string?)null)).ToArray());

        var weeks = FreeTransfers.Calculate(anna, Build.Game(current: 8), 9);

        Assert.Equal([null, 1, 2, 3, 4, 5, 5, 5, 5], Available(weeks));
    }

    [Fact]
    public void Transfers_use_up_saved_ones_and_a_hit_leaves_none()
    {
        // GW2: 1 free, makes 1. GW3: 1 free, makes 3 (-8). GW4: back to 1.
        var anna = Build.Transfers(1, (1, 0, 0, null), (2, 1, 0, null), (3, 3, 8, null), (4, 0, 0, null));

        var weeks = FreeTransfers.Calculate(anna, Build.Game(current: 4), 5);

        Assert.Equal([null, 1, 1, 1, 2], Available(weeks));
    }

    [Fact]
    public void Wildcard_and_free_hit_keep_the_saved_transfers_without_adding_one()
    {
        // Leon (2026/27): 2 saved going into his GW7 Wildcard, then 8 transfers for -24 in GW8,
        // which FPL only charges if he had exactly 2 free: kept, not 3.
        var leon = Build.Transfers(1,
            (1, 0, 0, null), (2, 0, 0, null), (3, 0, 0, null), (4, 3, 0, null), (5, 1, 0, null),
            (6, 0, 0, null), (7, 0, 0, "wildcard"), (8, 8, 24, null), (9, 1, 0, null), (10, 0, 0, null));

        var weeks = FreeTransfers.Calculate(leon, Build.Game(current: 10), 11);

        Assert.Equal([null, 1, 2, 3, 1, 1, 2, 2, 1, 1, 2], Available(weeks));
    }

    [Fact]
    public void A_free_hit_week_keeps_the_saved_transfers()
    {
        // Niels (2026/27): Wildcard GW4, Free Hit GW9.
        var niels = Build.Transfers(1,
            (1, 0, 0, "bboost"), (2, 0, 0, null), (3, 2, 0, null), (4, 0, 0, "wildcard"), (5, 1, 0, null),
            (6, 0, 0, null), (7, 1, 0, null), (8, 0, 0, null), (9, 0, 0, "freehit"), (10, 1, 0, null));

        var weeks = FreeTransfers.Calculate(niels, Build.Game(current: 10), 11);

        Assert.Equal([null, 1, 2, 1, 1, 1, 2, 2, 3, 3, 3], Available(weeks));
    }

    [Fact]
    public void Fpls_hit_cost_corrects_the_count()
    {
        // The calculation says 4 free in GW5, but FPL charged -8 for 5 transfers: it had 3. FPL wins.
        var anna = Build.Transfers(1, (1, 0, 0, null), (2, 0, 0, null), (3, 0, 0, null), (4, 0, 0, null), (5, 5, 8, null));

        var weeks = FreeTransfers.Calculate(anna, Build.Game(current: 5), 6);

        Assert.Equal(3, weeks.Single(w => w.Gameweek == 5).Available);
        Assert.Equal(1, weeks.Single(w => w.Gameweek == 6).Available);
    }

    [Fact]
    public void Free_transfers_made_beyond_the_calculation_raise_it()
    {
        // 3 transfers with no hit means at least 3 were free.
        var anna = Build.Transfers(1, (1, 0, 0, null), (2, 0, 0, null), (3, 3, 0, null));

        var weeks = FreeTransfers.Calculate(anna, Build.Game(current: 3), 4);

        Assert.Equal(3, weeks.Single(w => w.Gameweek == 3).Available);
        Assert.Equal(1, weeks.Single(w => w.Gameweek == 4).Available);
    }

    [Fact]
    public void A_team_started_mid_season_has_unlimited_transfers_before_its_first_deadline()
    {
        var late = Build.Transfers(6, (6, 0, 0, null), (7, 0, 0, null));

        var weeks = FreeTransfers.Calculate(late, Build.Game(current: 7), 8);

        Assert.Equal([6, 7, 8], weeks.Select(w => w.Gameweek));
        Assert.Equal([null, 1, 2], Available(weeks));
    }

    [Fact]
    public void A_one_off_top_up_applies_in_its_season_only()
    {
        // 5 saved, 3 used in GW15: 3 for GW16. In 2025/26 everyone was topped up to 5 for GW16 (AFCON).
        var rows = Enumerable.Range(1, 16).Select(gw => (gw, gw == 15 ? 3 : 0, 0, (string?)null)).ToArray();
        var anna = Build.Transfers(1, rows);

        var then = FreeTransfers.Calculate(anna, Build.Game(current: 16, season: "2025/26"), 16);
        var now = FreeTransfers.Calculate(anna, Build.Game(current: 16, season: "2026/27"), 16);

        Assert.Equal(5, then.Single(w => w.Gameweek == 16).Available);
        Assert.Equal(3, now.Single(w => w.Gameweek == 16).Available);
    }

    [Fact]
    public void The_maximum_comes_from_the_season_settings()
    {
        var anna = Build.Transfers(1, (1, 0, 0, null), (2, 0, 0, null), (3, 0, 0, null), (4, 0, 0, null));

        var weeks = FreeTransfers.Calculate(anna, Build.Game(current: 4, maxFreeTransfers: 2), 5);

        Assert.Equal([null, 1, 2, 2, 2], Available(weeks));
    }
}
