namespace StreamingDigest.Domain;

public sealed record Video(Guid Id, string Title)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled video" : Title.Trim();
}