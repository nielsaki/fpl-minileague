using System.Net;
using System.Text;
using FplMiniLeague.Models;
using FplMiniLeague.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace FplMiniLeague.Tests;

/// <summary>A throwaway folder holding teams.txt and data/, with a <see cref="ManagerStore"/> on it.</summary>
public sealed class TempDataFolder : IDisposable
{
    public TempDataFolder(string teams = "")
    {
        Path = Directory.CreateTempSubdirectory("fpl-test-").FullName;
        TeamsFile = System.IO.Path.Combine(Path, "teams.txt");
        File.WriteAllText(TeamsFile, teams);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataFolder"] = System.IO.Path.Combine(Path, "data"),
                ["TeamsFile"] = TeamsFile,
                ["WatchFiles"] = "false",
            })
            .Build();
        Store = new ManagerStore(config, new FakeEnvironment(), NullLogger<ManagerStore>.Instance);
    }

    public string Path { get; }

    public string TeamsFile { get; }

    public ManagerStore Store { get; }

    public void WriteTeams(string text)
    {
        File.WriteAllText(TeamsFile, text);
        Store.Reload();
    }

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

    /// <summary>Every path asked for, in order.</summary>
    public List<string> Requests { get; } = [];

    public HttpClient CreateClient(string name) =>
        new(new Handler(this)) { BaseAddress = new Uri("https://fantasy.premierleague.com/api/") };

    private sealed class Handler(FakeFpl fpl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            fpl.Requests.Add(path);
            if (fpl.Down)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(fpl.Responses.TryGetValue(path, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

public sealed class FakeTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Builders for the shapes the tests need.</summary>
public static class Build
{
    /// <summary>A season of <paramref name="events"/> weekly gameweeks starting 21 Aug 2026, with
    /// <paramref name="current"/> as the gameweek being played (null: before the season).</summary>
    public static GameInfo Game(int events = 38, int? current = null, bool currentFinished = false, bool dataChecked = false,
        int maxFreeTransfers = 5, string season = "2026/27", IReadOnlyList<ChipWindow>? chips = null)
    {
        var start = new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero);
        return new GameInfo
        {
            Season = season,
            MaxFreeTransfers = maxFreeTransfers,
            Events = Enumerable.Range(1, events).Select(id => new GameEvent(
                id,
                start.AddDays(7 * (id - 1)),
                Finished: current is int c && (id < c || (id == c && currentFinished)),
                DataChecked: current is int d && (id < d || (id == d && dataChecked)),
                Average: 50,
                IsCurrent: id == current,
                IsNext: id == (current ?? 0) + 1)).ToList(),
            Chips = chips ?? TwoHalves,
        };
    }

    /// <summary>The 2025/26-onwards chips: one set for GW1–19 (WC and FH from GW2), one for GW20–38.</summary>
    public static readonly IReadOnlyList<ChipWindow> TwoHalves =
    [
        new("wildcard", 2, 19), new("wildcard", 20, 38),
        new("freehit", 2, 19), new("bboost", 1, 19), new("3xc", 1, 19),
        new("freehit", 20, 38), new("bboost", 20, 38), new("3xc", 20, 38),
    ];

    /// <summary>A manager whose rows are (gameweek, transfers, hit cost, chip).</summary>
    public static ManagerHistory Transfers(int? started, params (int Gw, int Transfers, int Cost, string? Chip)[] rows) => new()
    {
        EntryId = 1,
        Name = "Anna",
        StartedEvent = started,
        Gameweeks = rows.ToDictionary(r => r.Gw, r => new GameweekEntry(r.Gw, 0, 0, 0, r.Transfers, r.Cost, r.Chip)),
        Chips = rows.Where(r => r.Chip is not null).Select(r => new ChipPlay(r.Chip!, r.Gw)).ToList(),
    };
}
