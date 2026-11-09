using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// The FPL data saved in the repository (data/fpl/), which the site is built from:
/// <list type="bullet">
/// <item>game.json: gameweeks (deadlines, finished, global averages), chips, free-transfer limit.</item>
/// <item>teams/&lt;id&gt;.json: one per team in teams.txt (names, gameweek history, chips played).</item>
/// <item>status.json: when the data last changed and when FPL next needs asking.</item>
/// </list>
/// Files are written in a fixed layout and only when their content changes, so a fetch that finds
/// nothing new leaves nothing to commit.
/// </summary>
public sealed class FplCache(string folder)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep names such as "Áki" readable in the committed files.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string Folder { get; } = folder;

    public string GamePath => Path.Combine(Folder, "game.json");

    public string StatusPath => Path.Combine(Folder, "status.json");

    public string TeamsFolder => Path.Combine(Folder, "teams");

    public string TeamPath(int id) => Path.Combine(TeamsFolder, $"{id}.json");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions) + "\n";

    /// <summary>The file's content, or default when it's missing. Throws <see cref="JsonException"/>
    /// when it can't be read as <typeparamref name="T"/>.</summary>
    public static T? Read<T>(string path) where T : class =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null;

    /// <summary>Writes <paramref name="value"/> unless the file already holds exactly that. Returns
    /// whether the file changed. Written beside and swapped in, so a reader never sees half a file.</summary>
    public static bool Write<T>(string path, T value)
    {
        var json = Serialize(value);
        if (File.Exists(path) && File.ReadAllText(path) == json)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, Utf8NoBom);
        File.Move(temp, path, overwrite: true);
        return true;
    }

    /// <summary>The IDs of the teams with a saved file.</summary>
    public IReadOnlyList<int> SavedTeams() =>
        Directory.Exists(TeamsFolder)
            ? Directory.EnumerateFiles(TeamsFolder, "*.json")
                .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList()
            : [];
}
