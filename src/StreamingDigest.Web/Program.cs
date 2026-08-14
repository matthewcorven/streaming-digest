using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StreamingDigest.Web;
using StreamingDigest.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseAddress = builder.Configuration["services:api:http:0"]
    ?? builder.Configuration["services__api__http__0"]
    ?? builder.Configuration["Api:BaseUrl"]
    ?? builder.Configuration["ApiBaseUrl"]
    ?? builder.HostEnvironment.BaseAddress;

var resolvedApiBaseAddress = Uri.TryCreate(apiBaseAddress, UriKind.Absolute, out var absoluteApiBaseAddress)
    ? absoluteApiBaseAddress
    : new Uri(new Uri(builder.HostEnvironment.BaseAddress, UriKind.Absolute), apiBaseAddress);

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = resolvedApiBaseAddress
});

builder.Services.AddScoped<UpgradeMaintenanceSnapshotService>();
builder.Services.AddSingleton<DashboardSummaryService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<SearchUiSessionService>();
builder.Services.AddScoped<CorpusReadinessService>();
builder.Services.AddScoped<ModelStatusService>();

await builder.Build().RunAsync();
