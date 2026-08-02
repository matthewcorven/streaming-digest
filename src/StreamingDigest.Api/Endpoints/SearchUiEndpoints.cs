using System.Text.Json;
using StreamingDigest.Application;

namespace StreamingDigest.Api.Endpoints;

internal static class SearchUiEndpoints
{
    public static void MapSearchUiEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/api/search-ui/settings", (HttpContext context, SearchUiService searchUiService) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(searchUiService.GetSettings())
                : Results.Unauthorized());

        app.MapPost("/api/search-ui/settings", async (HttpContext context, SearchUiService searchUiService, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            if (!ApiRequestPipeline.HasCsrfToken(context.Request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var request = await JsonSerializer.DeserializeAsync<SearchUiSettings>(context.Request.Body, jsonOptions, cancellationToken);
            if (request is null)
            {
                return Results.BadRequest();
            }

            searchUiService.UpdateSettings(request);
            return Results.Ok(searchUiService.GetSettings());
        });

        app.MapGet("/api/search-ui/recent", async (HttpContext context, SearchUiService searchUiService, CancellationToken cancellationToken) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(await searchUiService.GetRecentSearchesAsync(ApiRequestPipeline.GetSearchUiStateKey(context), cancellationToken))
                : Results.Unauthorized());

        app.MapDelete("/api/search-ui/recent", async (HttpContext context, SearchUiService searchUiService, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            if (!ApiRequestPipeline.HasCsrfToken(context.Request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await searchUiService.ClearRecentSearchesAsync(ApiRequestPipeline.GetSearchUiStateKey(context), cancellationToken);
            return Results.Ok();
        });

        app.MapPost("/api/search-ui/search", async (HttpContext context, SearchUiService searchUiService, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            if (!ApiRequestPipeline.HasCsrfToken(context.Request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var request = await JsonSerializer.DeserializeAsync<SearchRequest>(context.Request.Body, jsonOptions, cancellationToken);
            if (request is null)
            {
                return Results.BadRequest();
            }

            var response = await searchUiService.SearchAsync(request, ApiRequestPipeline.GetSearchUiStateKey(context), cancellationToken);
            return Results.Ok(response);
        });

        app.MapPost("/api/search-ui/interactions", async (HttpContext context, SearchUiService searchUiService, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            if (!ApiRequestPipeline.HasCsrfToken(context.Request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var request = await JsonSerializer.DeserializeAsync<SearchInteractionRequest>(context.Request.Body, jsonOptions, cancellationToken);
            if (request is null)
            {
                return Results.BadRequest();
            }

            await searchUiService.RecordInteractionAsync(request, cancellationToken);
            return Results.Ok();
        });
    }
}