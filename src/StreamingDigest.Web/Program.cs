using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StreamingDigest.Web;
using StreamingDigest.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
// IIngestionRunDetailFixtureService is kept for the ?fixture= demo path (IngestionRunDetail page)
// but is NOT registered as a singleton — the real run-detail endpoint is the runtime path.
builder.Services.AddScoped<UpgradeMaintenanceSnapshotService>();
builder.Services.AddSingleton<DashboardSummaryService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<SearchUiSessionService>();
builder.Services.AddScoped<CorpusReadinessService>();
builder.Services.AddScoped<ModelStatusService>();

await builder.Build().RunAsync();
