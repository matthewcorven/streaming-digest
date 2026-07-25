using System.Net.Http.Json;
using System.Text.Json;

namespace StreamingDigest.Worker.Scraping;

public sealed class ScraperClient(HttpClient httpClient)
{
    public async Task<ScrapeFirstPageResponse> ScrapeFirstPageAsync(ScrapeFirstPageRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/internal/scrape/first-page", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ScrapeFirstPageResponse>(cancellationToken: cancellationToken);
        return payload ?? throw new InvalidOperationException("The scraper returned an empty payload.");
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record ScrapeFirstPageRequest(
    string Url,
    bool RespectRobotsTxt = true,
    bool DebugCaptureRawHtml = false,
    int TimeoutSeconds = 30);

public sealed record ScrapeFirstPageResponse(
    string FinalUrl,
    string? Title,
    string? Description,
    JsonElement OpenGraph,
    string VisibleText,
    bool RobotsAllowed,
    int HttpStatus,
    string? ContentType,
    string ContentHash,
    string? RawHtmlDebugPath);
