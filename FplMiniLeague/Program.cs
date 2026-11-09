using FplMiniLeague.Components;
using FplMiniLeague.Services;

// "--fetch" fetches the teams in teams.txt from FPL into data/fpl/ and exits (with --export, it then
// builds the site). It's a plain switch, so it's taken out before the rest is read as configuration.
var fetchOnly = args.Contains("--fetch");
args = args.Where(a => a != "--fetch").ToArray();

var builder = WebApplication.CreateBuilder(args);

// "--export <folder>" builds the read-only copy of the site for publishing instead of serving it.
var exportTo = builder.Configuration["export"];
var oneOff = fetchOnly || exportTo is not null;
var site = SiteMode.FromConfiguration(builder.Configuration, isStatic: exportTo is not null);
builder.Services.AddSingleton(site);
if (oneOff)
{
    // Nothing to watch in a run that exits straight away.
    builder.Configuration["WatchFiles"] = "false";
}

// The published copy is plain HTML, so it gets no live (interactive) connection at all.
var razor = builder.Services.AddRazorComponents();
if (!site.IsStatic)
{
    razor.AddInteractiveServerComponents();
}

builder.Services.AddHttpClient(FplFetcher.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://fantasy.premierleague.com/api/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FplMiniLeague/1.0");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ManagerStore>();
builder.Services.AddSingleton<FplFetcher>();
if (!oneOff)
{
    builder.Services.AddHostedService<FplRefresher>();
}

var app = builder.Build();

// Read teams.txt and the saved data straight away, not on the first page view.
app.Services.GetRequiredService<ManagerStore>();

if (fetchOnly)
{
    var result = await app.Services.GetRequiredService<FplFetcher>().FetchAsync(force: true);
    // In GitHub Actions, show the messages on the run's summary page (and in the failure email).
    var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
    foreach (var warning in result.Warnings)
    {
        Console.WriteLine(annotate ? $"::warning title=FPL::{warning}" : $"Warning: {warning}");
    }

    foreach (var error in result.Errors)
    {
        Console.WriteLine(annotate ? $"::error title=FPL::{error}" : $"Error: {error}");
    }

    Console.WriteLine(result.Changed ? "FPL data changed." : "FPL data unchanged.");
    if (result.Failed || exportTo is null)
    {
        return result.Failed ? 1 : 0;
    }
}

app.UseStaticFiles();
app.UseAntiforgery();
var components = app.MapRazorComponents<App>();
if (!site.IsStatic)
{
    components.AddInteractiveServerRenderMode();
}

if (exportTo is not null)
{
    // Serve on a free local port just long enough to render each page.
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:0");
    return await StaticExporter.RunAsync(app, exportTo);
}

app.Run();
return 0;
