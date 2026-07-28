using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamingDigest.Application;

public interface ISearchDocumentGenerator
{
    IReadOnlyList<GeneratedSearchDocument> Generate(SearchDocumentGenerationRequest request);
}

public sealed class SearchDocumentGenerator : ISearchDocumentGenerator
{
    public IReadOnlyList<GeneratedSearchDocument> Generate(SearchDocumentGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var documents = new List<GeneratedSearchDocument>();

        documents.AddRange(request.VideoMetadata.Select(item => BuildVideoMetadataDocument(request.ParentVideoId, item)));
        documents.AddRange(request.SegmentTitlesAndSummaries.Select(item => BuildSegmentDocument(request.ParentVideoId, item)));
        documents.AddRange(request.TranscriptChunks.Select(item => BuildTranscriptChunkDocument(request.ParentVideoId, item)));
        documents.AddRange(request.ExternalLinkMetadata.Select(item => BuildExternalLinkMetadataDocument(request.ParentVideoId, item)));
        documents.AddRange(request.ScrapedPageText.Select(item => BuildScrapedPageTextDocument(request.ParentVideoId, item)));
        documents.AddRange(request.RepositoryReadmeChunks.Select(item => BuildRepositoryReadmeChunkDocument(request.ParentVideoId, item)));
        documents.AddRange(request.Notes.Select(item => BuildNoteDocument(request.ParentVideoId, item)));

        return documents;
    }

    private static GeneratedSearchDocument BuildVideoMetadataDocument(Guid parentVideoId, VideoMetadataDocumentInput item)
    {
        var title = EffectiveValue(item.TitleOriginal, item.TitleOverride);
        var description = EffectiveValue(item.DescriptionOriginal, item.DescriptionOverride);
        return CreateDocument(
            SearchDocumentTypeNames.VideoMetadata,
            SearchDocumentSourceEntityTypes.Video,
            item.SourceEntityId,
            parentVideoId,
            title,
            description,
            sourceFieldName: "video_metadata");
    }

    private static GeneratedSearchDocument BuildSegmentDocument(Guid parentVideoId, SegmentTitleSummaryDocumentInput item)
    {
        var title = EffectiveValue(item.TitleOriginal, item.TitleOverride);
        var summary = EffectiveValue(item.SummaryOriginal, item.SummaryOverride);
        return CreateDocument(
            SearchDocumentTypeNames.SegmentTitle,
            SearchDocumentSourceEntityTypes.Segment,
            item.SourceEntityId,
            parentVideoId,
            title,
            summary,
            sourceFieldName: "segment_title_summary");
    }

    private static GeneratedSearchDocument BuildTranscriptChunkDocument(Guid parentVideoId, TranscriptChunkDocumentInput item)
    {
        var text = EffectiveValue(item.TextOriginal, item.TextOverride);
        return CreateDocument(
            SearchDocumentTypeNames.TranscriptChunk,
            SearchDocumentSourceEntityTypes.TranscriptCue,
            item.SourceEntityId,
            parentVideoId,
            string.Empty,
            text,
            sourceFieldName: "text",
            chunkIndex: item.ChunkIndex);
    }

    private static GeneratedSearchDocument BuildExternalLinkMetadataDocument(Guid parentVideoId, ExternalLinkMetadataDocumentInput item)
    {
        var title = EffectiveValue(item.TitleOriginal, item.TitleOverride);
        var description = EffectiveValue(item.DescriptionOriginal, item.DescriptionOverride);
        var classification = EffectiveValue(item.ClassificationOriginal, item.ClassificationOverride);
        var domain = EffectiveValue(item.DomainOriginal, item.DomainOverride);
        var body = JoinNonEmpty(description, classification, domain);
        return CreateDocument(
            SearchDocumentTypeNames.ExternalResourceMetadata,
            SearchDocumentSourceEntityTypes.ExternalResource,
            item.SourceEntityId,
            parentVideoId,
            title,
            body,
            sourceFieldName: "external_resource_metadata");
    }

    private static GeneratedSearchDocument BuildScrapedPageTextDocument(Guid parentVideoId, ScrapedPageTextDocumentInput item)
    {
        var title = EffectiveValue(item.TitleOriginal, item.TitleOverride);
        var description = EffectiveValue(item.DescriptionOriginal, item.DescriptionOverride);
        var visibleText = EffectiveValue(item.VisibleTextOriginal, item.VisibleTextOverride);
        var body = JoinNonEmpty(description, visibleText);
        return CreateDocument(
            SearchDocumentTypeNames.ScrapedPageText,
            SearchDocumentSourceEntityTypes.ScrapedPage,
            item.SourceEntityId,
            parentVideoId,
            title,
            body,
            sourceFieldName: "visible_text");
    }

    private static GeneratedSearchDocument BuildRepositoryReadmeChunkDocument(Guid parentVideoId, RepositoryReadmeChunkDocumentInput item)
    {
        var content = EffectiveValue(item.ContentOriginal, item.ContentOverride);
        return CreateDocument(
            SearchDocumentTypeNames.RepositoryReadmeChunk,
            SearchDocumentSourceEntityTypes.Repository,
            item.SourceEntityId,
            parentVideoId,
            string.Empty,
            content,
            sourceFieldName: "readme_content",
            chunkIndex: item.ChunkIndex);
    }

    private static GeneratedSearchDocument BuildNoteDocument(Guid parentVideoId, NoteDocumentInput item)
    {
        var markdown = EffectiveValue(item.MarkdownOriginal, item.MarkdownOverride);
        return CreateDocument(
            SearchDocumentTypeNames.Note,
            SearchDocumentSourceEntityTypes.Note,
            item.SourceEntityId,
            parentVideoId,
            string.Empty,
            markdown,
            sourceFieldName: "markdown");
    }

    private static GeneratedSearchDocument CreateDocument(
        string documentType,
        string sourceEntityType,
        Guid sourceEntityId,
        Guid parentVideoId,
        string title,
        string body,
        string? sourceFieldName = null,
        int? chunkIndex = null)
    {
        var normalizedTitle = Normalize(title);
        var normalizedBody = Normalize(body);
        return new GeneratedSearchDocument(
            documentType,
            sourceEntityType,
            sourceEntityId,
            parentVideoId,
            normalizedTitle,
            normalizedBody,
            ComputeContentHash(normalizedTitle, normalizedBody),
            sourceFieldName,
            chunkIndex);
    }

    private static string EffectiveValue(string? original, string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue.Trim();
        }

        return original ?? string.Empty;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string JoinNonEmpty(params string[] values)
    {
        return string.Join("\n", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string ComputeContentHash(string title, string body)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new[] { title, body });
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}

public sealed class SearchDocumentGenerationRequest
{
    public Guid ParentVideoId { get; init; }
    public IReadOnlyCollection<VideoMetadataDocumentInput> VideoMetadata { get; init; } = Array.Empty<VideoMetadataDocumentInput>();
    public IReadOnlyCollection<SegmentTitleSummaryDocumentInput> SegmentTitlesAndSummaries { get; init; } = Array.Empty<SegmentTitleSummaryDocumentInput>();
    public IReadOnlyCollection<TranscriptChunkDocumentInput> TranscriptChunks { get; init; } = Array.Empty<TranscriptChunkDocumentInput>();
    public IReadOnlyCollection<ExternalLinkMetadataDocumentInput> ExternalLinkMetadata { get; init; } = Array.Empty<ExternalLinkMetadataDocumentInput>();
    public IReadOnlyCollection<ScrapedPageTextDocumentInput> ScrapedPageText { get; init; } = Array.Empty<ScrapedPageTextDocumentInput>();
    public IReadOnlyCollection<RepositoryReadmeChunkDocumentInput> RepositoryReadmeChunks { get; init; } = Array.Empty<RepositoryReadmeChunkDocumentInput>();
    public IReadOnlyCollection<NoteDocumentInput> Notes { get; init; } = Array.Empty<NoteDocumentInput>();
}

public sealed record GeneratedSearchDocument(
    string DocumentType,
    string SourceEntityType,
    Guid SourceEntityId,
    Guid ParentVideoId,
    string TitleEffective,
    string BodyEffective,
    string ContentHash,
    string? SourceFieldName = null,
    int? ChunkIndex = null);

public sealed record VideoMetadataDocumentInput(
    Guid SourceEntityId,
    string? TitleOriginal,
    string? TitleOverride = null,
    string? DescriptionOriginal = null,
    string? DescriptionOverride = null);

public sealed record SegmentTitleSummaryDocumentInput(
    Guid SourceEntityId,
    string? TitleOriginal,
    string? TitleOverride = null,
    string? SummaryOriginal = null,
    string? SummaryOverride = null);

public sealed record TranscriptChunkDocumentInput(
    Guid SourceEntityId,
    string? TextOriginal,
    string? TextOverride = null,
    int? ChunkIndex = null);

public sealed record ExternalLinkMetadataDocumentInput(
    Guid SourceEntityId,
    string? TitleOriginal,
    string? TitleOverride = null,
    string? DescriptionOriginal = null,
    string? DescriptionOverride = null,
    string? ClassificationOriginal = null,
    string? ClassificationOverride = null,
    string? DomainOriginal = null,
    string? DomainOverride = null);

public sealed record ScrapedPageTextDocumentInput(
    Guid SourceEntityId,
    string? TitleOriginal,
    string? TitleOverride = null,
    string? DescriptionOriginal = null,
    string? DescriptionOverride = null,
    string? VisibleTextOriginal = null,
    string? VisibleTextOverride = null);

public sealed record RepositoryReadmeChunkDocumentInput(
    Guid SourceEntityId,
    string? ContentOriginal,
    string? ContentOverride = null,
    int? ChunkIndex = null);

public sealed record NoteDocumentInput(
    Guid SourceEntityId,
    string? MarkdownOriginal,
    string? MarkdownOverride = null);

public static class SearchDocumentTypeNames
{
    public const string VideoMetadata = "video_metadata";
    public const string SegmentTitle = "segment_title";
    public const string TranscriptChunk = "transcript_chunk";
    public const string ExternalResourceMetadata = "external_resource_metadata";
    public const string ScrapedPageText = "scraped_page_text";
    public const string RepositoryReadmeChunk = "repository_readme_chunk";
    public const string Note = "note";
}

public static class SearchDocumentSourceEntityTypes
{
    public const string Video = "video";
    public const string Segment = "segment";
    public const string TranscriptCue = "transcript_cue";
    public const string ExternalResource = "external_resource";
    public const string ScrapedPage = "scraped_page";
    public const string Repository = "repository";
    public const string Note = "note";
}
