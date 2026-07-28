using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class SearchDocumentGeneratorTests
{
    [Fact]
    public void Generate_ProducesExpectedDocumentTypesForRepresentativeInputs()
    {
        var generator = new SearchDocumentGenerator();
        var request = new SearchDocumentGenerationRequest
        {
            ParentVideoId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            VideoMetadata =
            [
                new VideoMetadataDocumentInput(
                    SourceEntityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    TitleOriginal: "Original video title",
                    TitleOverride: "Overridden video title",
                    DescriptionOriginal: "Original description")
            ],
            SegmentTitlesAndSummaries =
            [
                new SegmentTitleSummaryDocumentInput(
                    SourceEntityId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    TitleOriginal: "Original segment title",
                    SummaryOriginal: "Original segment summary")
            ],
            TranscriptChunks =
            [
                new TranscriptChunkDocumentInput(
                    SourceEntityId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    TextOriginal: "Transcript text chunk")
            ],
            ExternalLinkMetadata =
            [
                new ExternalLinkMetadataDocumentInput(
                    SourceEntityId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    TitleOriginal: "External link",
                    DescriptionOriginal: "Link description",
                    ClassificationOriginal: "article",
                    DomainOriginal: "example.com")
            ],
            ScrapedPageText =
            [
                new ScrapedPageTextDocumentInput(
                    SourceEntityId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    TitleOriginal: "Scraped page title",
                    VisibleTextOriginal: "Visible page text")
            ],
            RepositoryReadmeChunks =
            [
                new RepositoryReadmeChunkDocumentInput(
                    SourceEntityId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    ContentOriginal: "# Repo\nReadme text")
            ],
            Notes =
            [
                new NoteDocumentInput(
                    SourceEntityId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
                    MarkdownOriginal: "# Note\nNote markdown")
            ]
        };

        var documents = generator.Generate(request);

        Assert.Collection(
            documents,
            document => AssertDocument(document, SearchDocumentTypeNames.VideoMetadata, "Overridden video title", "Original description"),
            document => AssertDocument(document, SearchDocumentTypeNames.SegmentTitle, "Original segment title", "Original segment summary"),
            document => AssertDocument(document, SearchDocumentTypeNames.TranscriptChunk, string.Empty, "Transcript text chunk"),
            document => AssertDocument(document, SearchDocumentTypeNames.ExternalResourceMetadata, "External link", "Link description\narticle\nexample.com"),
            document => AssertDocument(document, SearchDocumentTypeNames.ScrapedPageText, "Scraped page title", "Visible page text"),
            document => AssertDocument(document, SearchDocumentTypeNames.RepositoryReadmeChunk, string.Empty, "# Repo\nReadme text"),
            document => AssertDocument(document, SearchDocumentTypeNames.Note, string.Empty, "# Note\nNote markdown"));

        Assert.All(documents, document => Assert.False(string.IsNullOrWhiteSpace(document.ContentHash)));
        Assert.All(documents, document => Assert.Equal(request.ParentVideoId, document.ParentVideoId));
    }

    private static void AssertDocument(GeneratedSearchDocument document, string expectedDocumentType, string expectedTitle, string expectedBody)
    {
        Assert.Equal(expectedDocumentType, document.DocumentType);
        Assert.Equal(expectedTitle, document.TitleEffective);
        Assert.Equal(expectedBody, document.BodyEffective);
        Assert.NotEqual(Guid.Empty, document.SourceEntityId);
    }
}
