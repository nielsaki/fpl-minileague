using FplMiniLeague.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace FplMiniLeague.Services;

/// <summary>
/// Builds the read-only copy of the site that is published online (<c>dotnet run -- --export &lt;folder&gt;</c>):
/// fetches every linked manager and the global averages from FPL, renders each public page (season,
/// every gameweek, graphs) to an index.html, and copies the stylesheet and script beside them.
/// If FPL can't be reached it stops with an error, so the copy already online isn't replaced by
/// stale numbers.
/// </summary>
public static class StaticExporter
{
    /// <summary>Marks a folder as made by the export, so it's safe to clear on the next run.</summary>
    private const string Marker = ".fpl-export";

    public static async Task<int> RunAsync(WebApplication app, string outDir)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StaticExporter));
        var root = Path.GetFullPath(outDir);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !File.Exists(Path.Combine(root, Marker)))
        {
            log.LogError("{Folder} isn't empty and wasn't made by an earlier export; not touching it", root);
            return 2;
        }

        await app.StartAsync();
        try
        {
            var entries = app.Services.GetRequiredService<FplEntries>();
            var store = app.Services.GetRequiredService<ManagerStore>();
            var averages = app.Services.GetRequiredService<GlobalAverages>();

            await entries.UpdateAllAsync();
            var failed = entries.Linked.Where(l => l.LastError is not null).ToList();
            foreach (var link in failed)
            {
                log.LogError("{Manager}: {Error}", link.PlayerName, link.LastError);
            }

            var gameweeks = LeagueStatistics.Gameweeks(store.Current.Managers);
            await averages.GetAsync(gameweeks);
            if (averages.LastError is not null)
            {
                log.LogError("Global averages: {Error}", averages.LastError);
            }

            if (failed.Count > 0 || averages.LastError is not null)
            {
                return 1;
            }

            foreach (var problem in store.Current.Problems)
            {
                log.LogWarning("{Problem}", problem);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address.TrimEnd('/') + "/") };

            string[] routes = ["", "graphs", "gw", .. gameweeks.Select(gw => $"gw/{gw}")];
            foreach (var route in routes)
            {
                var html = await http.GetStringAsync(route);
                var file = Path.Combine(root, route, "index.html");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, html);
            }

            CopyDirectory(app.Environment.WebRootPath, root);
            await File.WriteAllTextAsync(Path.Combine(root, ".nojekyll"), "");
            await File.WriteAllTextAsync(Path.Combine(root, Marker), "");

            log.LogInformation("Exported {Pages} pages for {Managers} managers to {Folder}",
                routes.Length, store.Current.Managers.Count, root);
            return 0;
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
