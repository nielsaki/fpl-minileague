# FPL mini-league

A dashboard for a Fantasy Premier League mini-league. Add managers by pasting their FPL links; their
gameweek history is fetched from the FPL API, remembered and kept up to date.
Blazor Server on .NET 8, styled like the fss-platform apps (Úrslit).

## Run

```sh
cd FplMiniLeague
dotnet run          # http://localhost:5010
```

Tests: `dotnet test` from this folder.

## Published online (GitHub Pages)

A read-only copy of the site (Season, Gameweek and Graphs; no Managers tab or Update button) is
published on GitHub Pages by `.github/workflows/publish.yml`. It fetches fresh numbers from FPL and
republishes every 30 minutes, on every push to `main`, and when run by hand from the Actions tab. If FPL
can't be reached the run fails and the copy already online stays as it was.

- **Adding or removing managers:** run the app locally, change them on the Managers tab, then commit and
  push `FplMiniLeague/data/`. The site rebuilds straight away.
- **Building the copy yourself:** `cd FplMiniLeague && dotnet run --no-launch-profile -- --export ../site`.
  Set `BasePath` to `/<repo-name>/` to match GitHub Pages.
- **Scheduled runs pause** in a repository with no commits for 60 days (GitHub sends an email). Turn the
  workflow back on from the Actions tab, or push any commit.

## Adding managers

On the **Managers** tab, paste one or more links to managers' FPL pages and press **Add**. Any page under
`/entry/<id>/` works, for example `https://fantasy.premierleague.com/en/entry/2985395/history` or a
points page opened from the league table. Bare team IDs work too.

- Every linked manager is fetched again every 30 minutes, and on **Update from FPL**.
- If FPL can't be reached, the last fetched data is used and the Managers tab says the update failed.
- An exported history CSV can still be imported from the Managers tab.

## What is stored

Everything lives in `FplMiniLeague/data/` (change it with the `DataFolder` setting):

- `managers.json`: the linked managers (team ID, name, team name, file, last update).
- One CSV per manager, named after them (`mikkjal jacobsen.csv`), with the columns
  `GW, OR, OP, GWP, PB, TC, £m`. Fetched managers are written in this format, so they're read the same
  way as imported CSVs. Copying a CSV into the folder also adds that manager.

## What it shows

| Statistic | Gameweek view | Season view |
|---|---|---|
| Mini-league rank | by OP after that gameweek (equal points share a place) | by OP after the latest gameweek |
| Overall points | OP after that gameweek | OP after the latest gameweek |
| Points behind leader | leader's OP − OP, that gameweek | same, latest gameweek |
| Bench pain | PB that gameweek, with the total to date | PB added up over the season |
| vs global average | GWP − official FPL average, that gameweek, with the total to date | the season total, and a grid of every gameweek |

The **Graphs** tab shows the season gameweek by gameweek:

- **Points behind the leader**: the leader sits at 0, everyone else below ("−38").
- **League position**: mini-league place after each gameweek, 1st at the top.

Hover or tab to a gameweek for everyone's numbers, hover a name at the end of a line to pick it out, or
click a name in the row above the graphs to highlight that manager in both. Each graph has a "Show as
table" view. Colours follow the order managers were added; past eight managers the colours repeat with
square (then diamond) markers.

- **Global averages** come from the FPL API (`bootstrap-static`, `average_entry_score`) and are used once
  FPL marks a gameweek finished. A gameweek in progress shows "–" until then.
- **Gameweek in progress**: FPL's history lags behind during a live gameweek, so for that gameweek the
  live overall and gameweek points from the manager's FPL profile are used, the same totals FPL's league
  tables show. Bench points have no live source and catch up when FPL updates the history.
- **Transfer hits**: OP already has them deducted, so TC is never subtracted again. GWP is used as FPL
  reports it.
- **Missing gameweeks**: if a manager has no row for a gameweek, their overall points carry over from
  their latest earlier row (marked with a small "GW n" tag), and that gameweek is left out of their bench
  and global-average totals rather than counted as zero. A manager with no rows yet at a gameweek is
  listed last, unranked.
