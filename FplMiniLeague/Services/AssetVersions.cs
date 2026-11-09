using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace FplMiniLeague.Services;

/// <summary>
/// Adds a fingerprint of a stylesheet's or script's content to its URL ("css/theme.css?v=1a2b3c4d5e").
/// GitHub Pages lets browsers keep those files for 10 minutes, so without it a freshly published page
/// could be drawn with the stylesheet from the visitor's previous visit. With it, a changed file has a
/// new URL and is fetched together with the pages that need it.
/// </summary>
public sealed class AssetVersions(IWebHostEnvironment env)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Modified, string Url)> _urls = new();

    /// <param name="path">Relative to wwwroot, as the page links it (e.g. "css/theme.css").</param>
    public string Url(string path)
    {
        var file = env.WebRootFileProvider.GetFileInfo(path);
        if (!file.Exists)
        {
            return path;
        }

        // Hashed again only when the file changes (it can, while the app runs locally).
        if (_urls.TryGetValue(path, out var known) && known.Modified == file.LastModified)
        {
            return known.Url;
        }

        using var stream = file.CreateReadStream();
        var url = $"{path}?v={Convert.ToHexString(SHA256.HashData(stream))[..10].ToLowerInvariant()}";
        _urls[path] = (file.LastModified, url);
        return url;
    }
}
