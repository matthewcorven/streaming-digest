using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class SearchDocumentGenerationServiceTests
{
    private readonly ISearchDocumentGenerationService _service = new SearchDocumentGenerationService();

    [Fact]
    public void CreateDocument_uses_effective_values_and_hashes_the_effective_content()
    {
        var videoId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        var document = _service.CreateDocument(new SearchDocumentCandidate(
            DocumentType: "video_metadata",
            SourceEntityType: "videos",
            SourceEntityId: sourceId,
            ParentVideoId: videoId,
            TitleOriginal: "Original title",
            TitleOverride: "  Updated title  ",
            BodyOriginal: "Original body",
            BodyOverride: null));

        Assert.Equal("video_metadata", document.DocumentType);
        Assert.Equal("videos", document.SourceEntityType);
        Assert.Equal(videoId, document.ParentVideoId);
        Assert.Equal("Updated title", document.TitleEffective);
        Assert.Equal("Original body", document.BodyEffective);
        Assert.False(document.IsStale);
        Assert.False(string.IsNullOrWhiteSpace(document.ContentHash));
    }

    [Fact]
    public void Generate_populates_all_documents_with_expected_metadata()
    {
        var parentVideoId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        var documents = _service.Generate(new[]
        {
            new SearchDocumentCandidate(
                DocumentType: "transcript_chunk",
                SourceEntityType: "transcript_cues",
                SourceEntityId: transcriptId,
                ParentVideoId: parentVideoId,
                ChunkIndex: 2,
                SourceFieldName: "body",
                BodyOriginal: "Transcript content",
                BodyOverride: "Transcript content"),
            new SearchDocumentCandidate(
                DocumentType: "note",
                SourceEntityType: "notes",
                SourceEntityId: noteId,
                ParentVideoId: parentVideoId,
                SourceFieldName: "markdown",
                BodyOriginal: "Remember to review this later",
                BodyOverride: null)
        });

        var transcriptDocument = Assert.Single(documents, d => d.DocumentType == "transcript_chunk");
        var noteDocument = Assert.Single(documents, d => d.DocumentType == "note");

        Assert.Equal(parentVideoId, transcriptDocument.ParentVideoId);
        Assert.Equal(2, transcriptDocument.ChunkIndex);
        Assert.Equal("body", transcriptDocument.SourceFieldName);
        Assert.Equal("transcript_cues", transcriptDocument.SourceEntityType);

        Assert.Equal(noteId, noteDocument.SourceEntityId);
        Assert.Equal("markdown", noteDocument.SourceFieldName);
        Assert.Equal("Remember to review this later", noteDocument.BodyEffective);
        Assert.True(noteDocument.EmbeddingRequired);
    }
}
