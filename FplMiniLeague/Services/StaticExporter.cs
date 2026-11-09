using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace FplMiniLeague.Services;

/// <summary>
/// Builds the read-only copy of the site that is published online (<c>dotnet run -- --export &lt;folder&gt;</c>):
/// renders each public page (season, every gameweek, graphs, chips) to an index.html from the FPL data
/// saved in data/fpl/, and copies the stylesheet and script beside them. It makes no FPL requests
/// itself (that's <c>--fetch</c>), and stops with an error if a team in teams.txt has no saved data,
/// so the copy already online isn't replaced by an incomplete league.
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

        var snapshot = app.Services.GetRequiredService<ManagerStore>().Current;
        foreach (var problem in snapshot.Problems)
        {
            log.LogError("{Problem}", problem);
        }

        foreach (var id in snapshot.Missing)
        {
            log.LogError("Team {Id} in teams.txt has no saved FPL data; run with --fetch first", id);
        }

        if (snapshot.Problems.Count > 0 || snapshot.Missing.Count > 0 || snapshot.Game is null)
        {
            if (snapshot.Game is null)
            {
                log.LogError("There is no saved FPL data (data/fpl/game.json); run with --fetch first");
            }

            return 1;
        }

        await app.StartAsync();
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address.TrimEnd('/') + "/") };

            var gameweeks = LeagueStatistics.Gameweeks(snapshot.Managers);
            string[] routes = ["", "graphs", "chips", "gw", .. gameweeks.Select(gw => $"gw/{gw}")];
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
                routes.Length, snapshot.Managers.Count, root);
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
