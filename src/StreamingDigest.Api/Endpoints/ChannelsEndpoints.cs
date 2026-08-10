using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Api.Endpoints;

internal static class ChannelsEndpoints
{
    public static void MapChannelEndpoints(this WebApplication app)
    {
        app.MapGet("/api/channels", async (HttpContext context, StreamingDigestDbContext dbContext, IEffectiveValueService effectiveValueService) =>
        {
            var query = context.Request.Query;
            var includePaused = bool.TryParse(query["includePaused"], out var includePausedValue) && includePausedValue;
            var page = int.TryParse(query["page"], out var pageValue) && pageValue > 0 ? pageValue : 1;
            var pageSize = int.TryParse(query["pageSize"], out var pageSizeValue) && pageSizeValue > 0 ? Math.Min(pageSizeValue, 100) : 25;

            var channelsQuery = dbContext.Channels.AsNoTracking().Where(channel => includePaused || !channel.IsPaused);
            var totalCount = await channelsQuery.CountAsync();

            var channels = await channelsQuery
                .OrderBy(channel => channel.NameOverride ?? channel.NameOriginal ?? channel.YoutubeChannelId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = channels.Select(channel => MapChannelListItem(effectiveValueService, channel)).ToList();
            return Results.Ok(new { items, page, pageSize, totalCount });
        });

        app.MapPost("/api/channels", async (CreateChannelRequest request, IChannelRepository channelRepository, IEffectiveValueService effectiveValueService) =>
        {
            if (string.IsNullOrWhiteSpace(request.SourceUrl))
            {
                return Results.BadRequest(new { error = "sourceUrl is required." });
            }

            var youtubeChannelId = ResolveYoutubeChannelId(request.SourceUrl);
            if (string.IsNullOrWhiteSpace(youtubeChannelId))
            {
                youtubeChannelId = ResolveChannelFallbackId(request.SourceUrl);
            }

            var existingChannel = await channelRepository.GetByYoutubeChannelIdAsync(youtubeChannelId);
            if (existingChannel is not null)
            {
                return Results.Conflict(new { error = "A channel with the supplied YouTube channel id already exists." });
            }

            var resolvedSourceUrl = ResolveChannelSourceUrl(request.SourceUrl);
            var channel = new Channel
            {
                YoutubeChannelId = youtubeChannelId,
                NameOriginal = ResolveChannelName(youtubeChannelId, resolvedSourceUrl),
                ProfileUrl = ResolveChannelProfileUrl(youtubeChannelId, resolvedSourceUrl),
                SourceUrl = resolvedSourceUrl,
                DefaultMaxAgeDays = request.DefaultMaxAgeDays,
                DefaultBackfillMaxVideos = request.DefaultBackfillMaxVideos
            };

            await channelRepository.AddAsync(channel);
            return Results.Created($"/api/channels/{channel.Id}", MapChannelDetail(effectiveValueService, channel));
        });

        app.MapGet("/api/channels/{channelId:guid}", async (Guid channelId, IChannelRepository channelRepository, IEffectiveValueService effectiveValueService) =>
        {
            var channel = await channelRepository.GetByIdAsync(channelId);
            return channel is null ? Results.NotFound() : Results.Ok(MapChannelDetail(effectiveValueService, channel));
        });

        app.MapPut("/api/channels/{channelId:guid}", async (Guid channelId, UpdateChannelRequest request, IChannelRepository channelRepository, IEffectiveValueService effectiveValueService) =>
        {
            var channel = await channelRepository.GetByIdAsync(channelId);
            if (channel is null)
            {
                return Results.NotFound();
            }

            if (request.NameOverride is not null)
            {
                channel.NameOverride = request.NameOverride;
            }

            if (request.DescriptionOverride is not null)
            {
                channel.DescriptionOverride = request.DescriptionOverride;
            }

            if (request.IsPaused is not null)
            {
                channel.IsPaused = request.IsPaused.Value;
            }

            if (request.DefaultMaxAgeDays is not null)
            {
                channel.DefaultMaxAgeDays = request.DefaultMaxAgeDays.Value;
            }

            if (request.DefaultBackfillMaxVideos is not null)
            {
                channel.DefaultBackfillMaxVideos = request.DefaultBackfillMaxVideos.Value;
            }

            await channelRepository.UpdateAsync(channel);
            return Results.Ok(new { status = "updated", entityType = "channel", entityId = channel.Id, resource = MapChannelDetail(effectiveValueService, channel) });
        });

        app.MapDelete("/api/channels/{channelId:guid}", async (Guid channelId, HttpContext context, IChannelRepository channelRepository) =>
        {
            var deleteRelatedData = bool.TryParse(context.Request.Query["deleteRelatedData"], out var deleteRelatedValue) && deleteRelatedValue;
            var confirm = context.Request.Query["confirm"].ToString();

            var channel = await channelRepository.GetByIdAsync(channelId);
            if (channel is null)
            {
                return Results.NotFound();
            }

            if (deleteRelatedData && !string.Equals(confirm, "true", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "confirm=true is required when deleteRelatedData is requested." });
            }

            await channelRepository.DeleteAsync(channelId, purgeMedia: deleteRelatedData);
            return Results.Ok(new { status = "deleted", entityType = "channel", entityId = channelId });
        });
    }

    private static string ResolveChannelName(string youtubeChannelId, string? sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            {
                var lastSegment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (!string.IsNullOrWhiteSpace(lastSegment) && !lastSegment.Equals("channel", StringComparison.OrdinalIgnoreCase))
                {
                    return lastSegment;
                }
            }
        }

        return string.IsNullOrWhiteSpace(youtubeChannelId) ? "Channel" : youtubeChannelId;
    }

    private static string ResolveChannelProfileUrl(string youtubeChannelId, string? sourceUrl)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) && uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.ToString();
        }

        return string.IsNullOrWhiteSpace(youtubeChannelId)
            ? "https://www.youtube.com"
            : $"https://www.youtube.com/channel/{youtubeChannelId}";
    }

    private static string ResolveYoutubeChannelId(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        if (!uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        if (segments[0].Equals("channel", StringComparison.OrdinalIgnoreCase) && segments.Length > 1)
        {
            return segments[1];
        }

        if (segments[0].Equals("user", StringComparison.OrdinalIgnoreCase) && segments.Length > 1)
        {
            return segments[1];
        }

        if (segments[0].StartsWith('@'))
        {
            return segments[0].TrimStart('@');
        }

        return string.Empty;
    }

    private static string ResolveChannelSourceUrl(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return string.Empty;
        }

        return sourceUrl.Trim();
    }

    private static string ResolveChannelFallbackId(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return "channel";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl));
        return $"channel-{Convert.ToHexString(bytes)[..12].ToLowerInvariant()}";
    }

    private static ChannelValueResponse CreateValueResponse(IEffectiveValueService effectiveValueService, string? original, string? overrideValue)
    {
        var resolvedValue = effectiveValueService.Resolve(original, overrideValue);
        return new ChannelValueResponse(resolvedValue.Original, resolvedValue.Override, resolvedValue.Effective);
    }

    private static string ResolveChannelDisplayName(EffectiveValue resolvedNameValue, string youtubeChannelId)
        => string.IsNullOrWhiteSpace(resolvedNameValue.Effective) ? youtubeChannelId : resolvedNameValue.Effective;

    private static ChannelListItemResponse MapChannelListItem(IEffectiveValueService effectiveValueService, Channel channel)
    {
        var resolvedNameValue = effectiveValueService.Resolve(channel.NameOriginal, channel.NameOverride);
        return new ChannelListItemResponse(channel.Id, channel.YoutubeChannelId, ResolveChannelDisplayName(resolvedNameValue, channel.YoutubeChannelId), channel.ProfileUrl, channel.IsPaused, channel.IsDegraded, channel.ConsecutiveFailures, channel.LastIngestedAt, channel.LastIngestionStatus);
    }

    private static ChannelDetailResponse MapChannelDetail(IEffectiveValueService effectiveValueService, Channel channel)
        => new(
            channel.Id,
            channel.YoutubeChannelId,
            CreateValueResponse(effectiveValueService, channel.NameOriginal, channel.NameOverride),
            CreateValueResponse(effectiveValueService, channel.DescriptionOriginal, channel.DescriptionOverride),
            channel.ProfileUrl,
            channel.SourceUrl,
            channel.IsPaused,
            channel.IsDegraded,
            channel.ConsecutiveFailures,
            channel.LastIngestedAt,
            channel.LastIngestionStatus,
            new ChannelIngestionDefaultsResponse(channel.DefaultMaxAgeDays, channel.DefaultBackfillMaxVideos));
}

internal sealed record CreateChannelRequest(string? SourceUrl, int? DefaultMaxAgeDays, int? DefaultBackfillMaxVideos);
internal sealed record UpdateChannelRequest(string? NameOverride, string? DescriptionOverride, bool? IsPaused, int? DefaultMaxAgeDays, int? DefaultBackfillMaxVideos);
internal sealed record ChannelListItemResponse(Guid Id, string YoutubeChannelId, string Name, string ProfileUrl, bool IsPaused, bool IsDegraded, int ConsecutiveFailures, DateTimeOffset? LastIngestedAt, string? LastIngestionStatus);
internal sealed record ChannelDetailResponse(Guid Id, string YoutubeChannelId, ChannelValueResponse Name, ChannelValueResponse Description, string ProfileUrl, string SourceUrl, bool IsPaused, bool IsDegraded, int ConsecutiveFailures, DateTimeOffset? LastIngestedAt, string? LastIngestionStatus, ChannelIngestionDefaultsResponse IngestionDefaults);
internal sealed record ChannelValueResponse(string? Original, string? Override, string? Effective);
internal sealed record ChannelIngestionDefaultsResponse(int? MaxAgeDays, int? BackfillMaxVideos);