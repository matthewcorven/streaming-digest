using Microsoft.AspNetCore.Components.Web;
using StreamingDigest.Web;
using StreamingDigest.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var apiBaseAddress = builder.Configuration["services:api:http:0"]
    ?? builder.Configuration["services__api__http__0"]
    ?? builder.Configuration["ApiBaseUrl"]
    ?? "http://localhost:8080";

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBaseAddress);
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("api");
});

// IIngestionRunDetailFixtureService is kept for the ?fixture= demo path (IngestionRunDetail page)
// but is NOT registered as a singleton — the real run-detail endpoint is the runtime path.
builder.Services.AddScoped<UpgradeMaintenanceSnapshotService>();
builder.Services.AddSingleton<DashboardSummaryService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<SearchUiSessionService>();
builder.Services.AddScoped<CorpusReadinessService>();
builder.Services.AddScoped<ModelStatusService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.Map("/api/{**catchall}", async (HttpContext context, IHttpClientFactory httpClientFactory) =>
{
    var apiClient = httpClientFactory.CreateClient("api");
    var requestUri = new Uri(apiClient.BaseAddress!, context.Request.Path.Value ?? "/");
    var targetUriBuilder = new UriBuilder(requestUri)
    {
        Query = context.Request.QueryString.Value?.TrimStart('?') ?? string.Empty
    };

    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUriBuilder.Uri);

    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content is null)
        {
            continue;
        }

        if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("X-Requested-With", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("X-Csrf-Token", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    if (context.Request.ContentLength > 0 || context.Request.Body.CanSeek)
    {
        request.Content = new StreamContent(context.Request.Body);
        if (context.Request.ContentType is not null)
        {
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
        }
    }

    using var response = await apiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

    context.Response.StatusCode = (int)response.StatusCode;
    foreach (var header in response.Headers)
    {
        if (!context.Response.Headers.ContainsKey(header.Key))
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
    }

    if (response.Content is not null)
    {
        foreach (var header in response.Content.Headers)
        {
            if (!context.Response.Headers.ContainsKey(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapFallbackToFile("index.html");

app.Logger.LogInformation("Web app is serving the host UI with API base address {ApiBaseAddress}", apiBaseAddress);

await app.RunAsync();
