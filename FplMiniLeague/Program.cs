using FplMiniLeague.Components;
using FplMiniLeague.Services;

var builder = WebApplication.CreateBuilder(args);

// "--export <folder>" builds the read-only copy of the site for publishing instead of serving it.
var exportTo = builder.Configuration["export"];
var site = SiteMode.FromConfiguration(builder.Configuration, isStatic: exportTo is not null);
builder.Services.AddSingleton(site);

// The published copy is plain HTML, so it gets no live (interactive) connection at all.
var razor = builder.Services.AddRazorComponents();
if (!site.IsStatic)
{
    razor.AddInteractiveServerComponents();
}

builder.Services.AddHttpClient(GlobalAverages.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://fantasy.premierleague.com/api/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FplMiniLeague/1.0");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<GlobalAverages>();
builder.Services.AddSingleton<ManagerStore>();
builder.Services.AddSingleton<FplEntries>();
if (exportTo is null)
{
    builder.Services.AddHostedService<FplRefresher>();
}

var app = builder.Build();

// Read the data folder and start watching it straight away, not on the first page view.
app.Services.GetRequiredService<ManagerStore>();

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
