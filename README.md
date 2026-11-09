# FPL mini-league

A dashboard for a Fantasy Premier League mini-league. The league is a list of FPL team IDs in
[`teams.txt`](teams.txt); names, points, history, chips and transfers are fetched from the FPL API
automatically. Blazor on .NET 8, styled like the fss-platform apps (Úrslit), published on GitHub Pages.

## Adding or removing a team

Edit [`teams.txt`](teams.txt) (on GitHub, or locally) and commit it:

```
2985395   # mikkjal jacobsen
1125906   # Niels Áki Mørk
```

- One FPL team ID per line: the number in the team's FPL address, e.g. `2985395` in
  `https://fantasy.premierleague.com/entry/2985395/history`. (A pasted address works too.)
- Anything after `#` is a note for yourself.
- The order of the file is the order colours are handed out in on the graphs, so adding a team never
  repaints anyone else.
- After the commit, the site is rebuilt with the new team within a few minutes (Actions tab → "Publish
  site"). If a line isn't an ID or FPL has no such team, that run fails with the reason on its summary
  page (GitHub emails you), and the site stays as it was until the file is fixed.
- **Team IDs are new every season.** When a new FPL season starts, replace all the IDs. Until you do,
  the site keeps showing the last season (it won't fetch strangers' teams under last season's IDs).

## Published online (GitHub Pages)

The FPL API can't be called from a browser on another site: it sends no CORS headers, so the browser
blocks the answer. So `.github/workflows/publish.yml` fetches the data on GitHub's servers instead,
saves it as JSON in the repository (`FplMiniLeague/data/fpl/`) and builds the site from it as plain HTML.
No login, key or secret is involved: the FPL endpoints used are public.

- **Every 30 minutes** it checks whether anything on FPL can have changed. While a gameweek is being
  played (or until FPL has confirmed its points) it fetches; between a gameweek's final check and the
  next deadline it stops after a few seconds without asking FPL anything. When the data changed, it's
  committed ("Update FPL data") and the site is republished.
- **On every push to `main`** it runs the tests, fetches and publishes.
- **By hand:** Actions tab → "Publish site" → "Run workflow" fetches and publishes straight away.
- If FPL can't be reached (it answers 503 for a while around deadlines), the saved data is kept and the
  next run tries again.
- **Scheduled runs pause** in a repository with no commits for 60 days (GitHub sends an email), e.g. over
  the summer. Turn the workflow back on from the Actions tab, or push any commit.

Each fetch asks for `bootstrap-static/` once, then `entry/<id>/` and `entry/<id>/history/` per team.
Only the fields the dashboard uses are saved, and files are only rewritten when their content changes.

## Run it on your own computer

```sh
cd FplMiniLeague
dotnet run          # http://localhost:5010
```

Locally there's an **Update from FPL** button and a **Managers** tab showing the teams in `teams.txt`
and how the last fetch went. Saving `teams.txt` fetches a new team straight away.

- Fetch only: `dotnet run --no-launch-profile -- --fetch`
- Build the published copy yourself: `dotnet run --no-launch-profile -- --export ../site` (set
  `BasePath` to `/<repo-name>/` to match GitHub Pages). It makes no FPL requests; run `--fetch` first.
- Tests: `dotnet test` from the repository root.

## What is stored

- [`teams.txt`](teams.txt): the team IDs (the only file you edit).
- `FplMiniLeague/data/fpl/game.json`: gameweeks (deadlines, finished, global averages), the season's
  chips, the free-transfer limit.
- `FplMiniLeague/data/fpl/teams/<id>.json`: each team's names, gameweek history and chips played.
- `FplMiniLeague/data/fpl/status.json`: when the data last changed and when FPL next needs asking.

## What it shows

**Season / Gameweek:** the leaderboard, now or as it stood after any earlier gameweek.

| Statistic | |
|---|---|
| Mini-league rank | by overall points after that gameweek (equal points share a place) |
| Overall points | FPL's total, transfer hits already deducted |
| Points behind leader | that gameweek's leader's total − the manager's |
| Bench pain | bench points added up to that gameweek (the gameweek's own figure underneath) |
| vs global average | gameweek points − FPL's official average, added up (the gameweek's own figure underneath) |

**Graphs:** points behind the leader (the leader of each gameweek at 0), league position (1st at the
top), bench pain added up, points vs the global average (weekly columns per manager, or the running
total around a zero line), and free transfers. Click names above the graphs to compare managers in all
of them; hover or tab to a gameweek for everyone's numbers; each graph has a table view.

**Chips:** a manager × chip matrix for each set of chips (since 2025/26 one Wildcard, Free Hit, Triple
Captain and Bench Boost for GW 1–19 and another for GW 20–38), with the gameweek each was played in.
The chips and their gameweeks come from FPL, so the tracker follows each season's own allocation.

### Rules and edge cases

- **Free transfers** aren't published by FPL for other managers, so they're worked out from each
  manager's history with the season's rules: unlimited before the first deadline, one more each
  gameweek, saved up to FPL's maximum (5), extra transfers cost 4 points and leave none saved, and a
  Wildcard or Free Hit keeps the saved ones without adding one. Where FPL's numbers pin the count down
  they win: a gameweek with a hit had exactly transfers − hit ÷ 4 free. One-off top-ups FPL announces
  outside the rules (2025/26: everyone topped up to 5 for GW16) are listed in `Services/FreeTransfers.cs`.
  Transfers for the next deadline don't show until it has passed.
- **Gameweek in progress:** FPL's history lags behind during a live gameweek, so for that gameweek the
  live totals from the manager's FPL profile are used, as FPL's league tables do. It's marked "Live".
  Bench points have no live source and catch up when FPL updates the history.
- **Global averages** are used once FPL marks a gameweek finished; until then it shows "–" and has no
  column or point in the average graphs. Postponed fixtures don't need special handling: FPL's own
  gameweek totals are used as they are.
- **Missing gameweeks:** if a manager has no row for a gameweek, their overall points carry over from
  their latest earlier row (marked with a small "GW n" tag), and that gameweek is left out of their bench
  and global-average totals rather than counted as zero. A manager with no rows yet is listed last,
  unranked.
