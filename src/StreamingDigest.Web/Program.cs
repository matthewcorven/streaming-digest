using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StreamingDigest.Web;
using StreamingDigest.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var serviceApiBaseAddress = builder.Configuration["services:api:http:0"]
    ?? builder.Configuration["services__api__http__0"];
var configuredApiBaseAddress = builder.Configuration["Api:BaseUrl"]
    ?? builder.Configuration["ApiBaseUrl"];

builder.Services.AddScoped(_ => new HttpClient());

builder.Services.AddScoped<UpgradeMaintenanceSnapshotService>();
builder.Services.AddSingleton<DashboardSummaryService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<SearchUiSessionService>();
builder.Services.AddScoped<CorpusReadinessService>();
builder.Services.AddScoped<ModelStatusService>();

var host = builder.Build();
var httpClient = host.Services.GetRequiredService<HttpClient>();
httpClient.BaseAddress = await ApiBaseAddressResolver.ResolveRuntimeBaseAddressAsync(
    builder.HostEnvironment.BaseAddress,
    serviceApiBaseAddress,
    configuredApiBaseAddress,
    ApiBaseAddressResolver.ProbeSameOriginApiAsync);

await host.RunAsync();
