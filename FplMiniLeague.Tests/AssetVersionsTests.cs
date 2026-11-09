using FplMiniLeague.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace FplMiniLeague.Tests;

public sealed class AssetVersionsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fpl-wwwroot-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private AssetVersions NewVersions() => new(new Environment(_root));

    private void WriteCss(string text, DateTime modified)
    {
        var path = Path.Combine(_root, "css", "theme.css");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, modified);
    }

    [Fact]
    public void A_changed_stylesheet_gets_a_new_url()
    {
        var versions = NewVersions();
        WriteCss("body { color: red; }", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = versions.Url("css/theme.css");

        WriteCss("body { color: blue; }", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        var after = versions.Url("css/theme.css");

        Assert.StartsWith("css/theme.css?v=", before);
        Assert.NotEqual(before, after);
        Assert.Equal(after, NewVersions().Url("css/theme.css"));  // same content, same URL
    }

    [Fact]
    public void A_missing_file_keeps_its_plain_url() =>
        Assert.Equal("js/missing.js", NewVersions().Url("js/missing.js"));

    private sealed class Environment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(root);
        public string ApplicationName { get; set; } = "FplMiniLeague";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }
}
