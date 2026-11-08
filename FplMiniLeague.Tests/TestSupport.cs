using System.Net;
using System.Text;
using FplMiniLeague.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace FplMiniLeague.Tests;

/// <summary>A throwaway data folder with a <see cref="ManagerStore"/> on it.</summary>
public sealed class TempDataFolder : IDisposable
{
    public TempDataFolder()
    {
        Path = Directory.CreateTempSubdirectory("fpl-test-").FullName;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataFolder"] = Path })
            .Build();
        Store = new ManagerStore(config, new FakeEnvironment(), NullLogger<ManagerStore>.Instance);
    }

    public string Path { get; }

    public ManagerStore Store { get; }

    public void Dispose()
    {
        Store.Dispose();
        Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "FplMiniLeague";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = System.IO.Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }
}

/// <summary>Stands in for the FPL API: answers each path from a dictionary, 404 for anything else.</summary>
public sealed class FakeFpl : IHttpClientFactory
{
    public Dictionary<string, string> Responses { get; } = [];

    public bool Down { get; set; }

    public HttpClient CreateClient(string name) =>
        new(new Handler(this)) { BaseAddress = new Uri("https://fantasy.premierleague.com/api/") };

    private sealed class Handler(FakeFpl fpl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (fpl.Down)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(fpl.Responses.TryGetValue(path, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
