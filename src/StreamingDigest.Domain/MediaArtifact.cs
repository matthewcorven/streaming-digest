namespace StreamingDigest.Domain;

public sealed class MediaArtifact : AuditedEntity
{
    public Guid Id { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string ArtifactKind { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public static class MediaArtifactOwnerTypes
{
    public const string Channel = "channel";
    public const string Video = "video";
}

public static class MediaArtifactKinds
{
    public const string Screenshot = "screenshot";
    public const string RawHtmlDebugCapture = "raw_html_debug_capture";
    public const string RawTranscript = "raw_transcript";
}
