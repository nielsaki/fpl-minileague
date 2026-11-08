using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// Every manager's gameweek history, read from the CSV files in the data folder: one file per manager,
/// named after them ("Anna Jensen.csv" or "Anna_Jensen.csv" → Anna Jensen). Managers added by FPL link
/// are stored here too (<see cref="FplEntries"/> writes their CSVs). The folder is read again whenever
/// a file is fetched, imported, dropped in, replaced or removed by hand, and <see cref="Changed"/>
/// tells open pages to redraw.
/// </summary>
public sealed class ManagerStore : IDisposable
{
    public const long MaxFileBytes = 1024 * 1024;

    public sealed record Snapshot(IReadOnlyList<ManagerHistory> Managers, IReadOnlyList<string> Problems)
    {
        public static readonly Snapshot Empty = new([], []);
    }

    private readonly ILogger<ManagerStore> _log;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _debounce;
    private readonly FileSystemWatcher _watcher;
    private Snapshot _current = Snapshot.Empty;

    public ManagerStore(IConfiguration config, IWebHostEnvironment env, ILogger<ManagerStore> log)
    {
        _log = log;
        DataFolder = Path.GetFullPath(Path.Combine(env.ContentRootPath, config["DataFolder"] ?? "data"));
        Directory.CreateDirectory(DataFolder);

        // Saving a file raises several events in a row; wait for them to settle before re-reading.
        _debounce = new System.Threading.Timer(_ => Reload());
        Reload();

        _watcher = new FileSystemWatcher(DataFolder, "*.csv")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += OnFolderChanged;
        _watcher.Changed += OnFolderChanged;
        _watcher.Deleted += OnFolderChanged;
        _watcher.Renamed += OnFolderChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public string DataFolder { get; }

    public Snapshot Current => Volatile.Read(ref _current);

    public event Action? Changed;

    /// <summary>
    /// Saves an uploaded CSV into the data folder (replacing the manager's previous file of the same
    /// name) and reloads. Returns an error message instead if the file can't be read.
    /// </summary>
    public async Task<string?> ImportAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        var name = SafeFileName(fileName);
        if (name is null)
        {
            return $"{fileName}: only .csv files can be imported.";
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        using (var reader = new StreamReader(new MemoryStream(bytes)))
        {
            var check = CsvHistoryParser.Parse(await reader.ReadToEndAsync(ct), ManagerName(name), name);
            if (check.Error is not null)
            {
                return $"{name}: {check.Error}";
            }
        }

        await File.WriteAllBytesAsync(Path.Combine(DataFolder, name), bytes, ct);
        Reload();
        return null;
    }

    /// <summary>Deletes a manager's CSV from the data folder and reloads.</summary>
    public void Delete(string fileName)
    {
        if (SafeFileName(fileName) is { } name)
        {
            File.Delete(Path.Combine(DataFolder, name));
        }

        Reload();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e) => _debounce.Change(400, Timeout.Infinite);

    /// <summary>Reads the data folder again and tells open pages.</summary>
    public void Reload()
    {
        lock (_gate)
        {
            var managers = new List<ManagerHistory>();
            var problems = new List<string>();
            var retry = false;

            foreach (var path in Directory.EnumerateFiles(DataFolder, "*.csv").Order(StringComparer.OrdinalIgnoreCase))
            {
                var file = Path.GetFileName(path);
                try
                {
                    if (new FileInfo(path).Length > MaxFileBytes)
                    {
                        problems.Add($"{file}: larger than 1 MB, skipped.");
                        continue;
                    }

                    var result = CsvHistoryParser.Parse(File.ReadAllText(path), ManagerName(file), file);
                    if (result.Manager is not null)
                    {
                        managers.Add(result.Manager);
                    }
                    else
                    {
                        problems.Add($"{file}: {result.Error}");
                    }
                }
                catch (IOException ex)
                {
                    // Most likely still being copied in; look again shortly.
                    _log.LogDebug(ex, "Couldn't read {File} yet", file);
                    problems.Add($"{file}: couldn't be read yet, trying again.");
                    retry = true;
                }
            }

            Volatile.Write(ref _current, new Snapshot(managers, problems));
            if (retry)
            {
                _debounce.Change(1000, Timeout.Infinite);
            }
        }

        Changed?.Invoke();
    }

    private static string ManagerName(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();

    /// <summary>The bare file name, if it is a usable .csv name; null otherwise.</summary>
    private static string? SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (!name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || Path.GetFileNameWithoutExtension(name).Trim().Length == 0)
        {
            return null;
        }

        return name;
    }
}
