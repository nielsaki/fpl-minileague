using System.Globalization;

namespace FplMiniLeague.Services;

/// <summary>
/// How the site is being served: the full app on your own computer (managers can be added, data
/// updated), or the read-only static copy published online, which has no Managers tab or Update
/// button and says when it was last updated.
/// </summary>
/// <param name="BasePath">Where the site lives on its host: "/" locally, "/repo-name/" on GitHub Pages.</param>
/// <param name="TimeZone">For "last updated" times; the published copy is built on a UTC server.</param>
public sealed record SiteMode(bool IsStatic, string BasePath, TimeZoneInfo TimeZone)
{
    public string FormatTime(DateTimeOffset time) =>
        TimeZoneInfo.ConvertTime(time, TimeZone).ToString("d MMM HH:mm", CultureInfo.InvariantCulture);

    public static SiteMode FromConfiguration(IConfiguration config, bool isStatic)
    {
        var basePath = (config["BasePath"] ?? "/").Trim();
        basePath = "/" + basePath.Trim('/') + "/";
        if (basePath == "//")
        {
            basePath = "/";
        }

        var zone = TimeZoneInfo.Local;
        if (config["TimeZone"] is { Length: > 0 } id)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall back to the machine's own zone.
            }
        }

        return new SiteMode(isStatic || config.GetValue<bool>("StaticSite"), basePath, zone);
    }
}
