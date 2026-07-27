namespace StreamingDigest.Domain;

public static class SegmentSourceTypes
{
    public const string AuthorChapter = "author_chapter";
    public const string DeterministicChunk = "deterministic_chunk";
    public const string SemanticLlm = "semantic_llm";

    public static IReadOnlyList<string> All { get; } =
    [
        AuthorChapter,
        DeterministicChunk,
        SemanticLlm
    ];
}
