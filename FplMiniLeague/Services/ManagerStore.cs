using System.Text.Json;
using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// The league as the pages see it: the teams listed in teams.txt, each read from the FPL data saved in
/// data/fpl/ (<see cref="FplCache"/>), in the order of teams.txt. Locally the files are watched, so
/// editing teams.txt or fetching new data redraws open pages (<see cref="Changed"/>).
/// </summary>
public sealed class ManagerStore : IDisposable
{
    public sealed record Snapshot(
        IReadOnlyList<ManagerHistory> Managers,
        GameInfo? Game,
        FetchStatus? Status,
        IReadOnlyList<int> TeamIds,
        IReadOnlyList<string> Problems)
    {
        public static readonly Snapshot Empty = new([], null, null, [], []);

        /// <summary>Final global average per finished gameweek.</summary>
        public IReadOnlyDictionary<int, int> Averages { get; } = Game?.FinalAverages() ?? new Dictionary<int, int>();

        /// <summary>Team IDs in teams.txt with no saved FPL data yet.</summary>
        public IReadOnlyList<int> Missing { get; } = TeamIds.Where(id => Managers.All(m => m.EntryId != id)).ToList();
    }

    private readonly ILogger<ManagerStore> _log;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _debounce;
    private readonly List<FileSystemWatcher> _watchers = [];
    private Snapshot _current = Snapshot.Empty;

    public ManagerStore(IConfiguration config, IWebHostEnvironment env, ILogger<ManagerStore> log)
    {
        _log = log;
        DataFolder = Path.GetFullPath(Path.Combine(env.ContentRootPath, config["DataFolder"] ?? "data"));
        TeamsFile = Path.GetFullPath(Path.Combine(env.ContentRootPath, config["TeamsFile"] ?? "../teams.txt"));
        Cache = new FplCache(Path.Combine(DataFolder, "fpl"));

        // Saving a file raises several events in a row; wait for them to settle before re-reading.
        _debounce = new System.Threading.Timer(_ => Reload());
        Reload();

        if (config.GetValue("WatchFiles", true))
        {
            Watch(Path.GetDirectoryName(TeamsFile)!, Path.GetFileName(TeamsFile), subdirectories: false);
            Directory.CreateDirectory(Cache.Folder);
            Watch(Cache.Folder, "*.json", subdirectories: true);
        }
    }

    public string DataFolder { get; }

    /// <summary>teams.txt: the team IDs in the league.</summary>
    public string TeamsFile { get; }

    public FplCache Cache { get; }

    public Snapshot Current => Volatile.Read(ref _current);

    public event Action? Changed;

    /// <summary>Raised when the list of team IDs in teams.txt has changed.</summary>
    public event Action? TeamsChanged;

    /// <summary>Reads teams.txt and the saved FPL data again and tells open pages.</summary>
    public void Reload()
    {
        bool teamsChanged;
        lock (_gate)
        {
            var problems = new List<string>();
            var teams = TeamList.Read(TeamsFile);
            problems.AddRange(teams.Problems);

            var game = ReadOrNull<GameInfo>(Cache.GamePath, problems);
            var status = ReadOrNull<FetchStatus>(Cache.StatusPath, problems);
            var managers = new List<ManagerHistory>();
            for (var slot = 0; slot < teams.Ids.Count; slot++)
            {
                var id = teams.Ids[slot];
                if (ReadOrNull<TeamData>(Cache.TeamPath(id), problems) is { } team)
                {
                    managers.Add(ToHistory(team, slot));
                }
            }

            teamsChanged = !_current.TeamIds.SequenceEqual(teams.Ids);
            Volatile.Write(ref _current, new Snapshot(managers, game, status, teams.Ids, problems));
        }

        Changed?.Invoke();
        if (teamsChanged)
        {
            TeamsChanged?.Invoke();
        }
    }

    /// <summary>
    /// A team's saved data as the statistics use it. While a gameweek is being played, FPL's history
    /// lags behind: its row for that gameweek keeps the points from whenever it was last refreshed,
    /// while the team's profile has the live totals FPL's own league tables show. So the live overall
    /// and gameweek points replace that row's. (Bench points have no live source; they catch up when
    /// FPL updates the history.)
    /// </summary>
    internal static ManagerHistory ToHistory(TeamData team, int slot)
    {
        var chips = team.Chips.GroupBy(c => c.Event).ToDictionary(g => g.Key, g => g.First().Name);
        var rows = team.History.Select(r =>
        {
            var live = team is { CurrentEvent: int current, SummaryOverallPoints: int total, SummaryEventPoints: int points }
                       && r.Event == current;
            return new GameweekEntry(
                r.Event,
                live ? team.SummaryOverallPoints!.Value : r.TotalPoints,
                live ? team.SummaryEventPoints!.Value : r.Points,
                r.PointsOnBench,
                r.Transfers,
                r.TransfersCost,
                chips.GetValueOrDefault(r.Event));
        });

        var name = team.PlayerName.Trim();
        if (name.Length == 0)
        {
            name = team.TeamName.Trim().Length > 0 ? team.TeamName.Trim() : $"Team {team.Id}";
        }

        return new ManagerHistory
        {
            EntryId = team.Id,
            Name = name,
            TeamName = team.TeamName.Trim(),
            Slot = slot,
            StartedEvent = team.StartedEvent,
            Gameweeks = rows.GroupBy(r => r.Gameweek).ToDictionary(g => g.Key, g => g.Last()),
            Chips = team.Chips,
        };
    }

    private T? ReadOrNull<T>(string path, List<string> problems) where T : class
    {
        try
        {
            return FplCache.Read<T>(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            _log.LogWarning(ex, "Couldn't read {File}", path);
            problems.Add($"{Path.GetRelativePath(DataFolder, path)} couldn't be read; it's replaced on the next fetch from FPL.");
            return null;
        }
    }

    private void Watch(string folder, string filter, bool subdirectories)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var watcher = new FileSystemWatcher(folder, filter)
        {
            IncludeSubdirectories = subdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += OnFilesChanged;
        watcher.Changed += OnFilesChanged;
        watcher.Deleted += OnFilesChanged;
        watcher.Renamed += OnFilesChanged;
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void OnFilesChanged(object sender, FileSystemEventArgs e) => _debounce.Change(400, Timeout.Infinite);

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _debounce.Dispose();
    }
}
