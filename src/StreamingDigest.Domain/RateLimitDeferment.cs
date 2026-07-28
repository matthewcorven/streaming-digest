namespace StreamingDigest.Domain;

public sealed class RateLimitDeferment : AuditedEntity
{
    public required Guid Id { get; set; }
    public required string ScopeType { get; set; }
    public required string ScopeKey { get; set; }
    public required string Reason { get; set; }
    public required DateTimeOffset RetryAfterAt { get; set; }
    public required string Status { get; set; }
    public string? DetailsJson { get; set; }

    public static class StatusValues
    {
        public const string Active = "active";
        public const string Expired = "expired";
        public const string Cleared = "cleared";

        public static bool IsValid(string? status) =>
            status is Active or Expired or Cleared;
    }

    public static class ScopeTypes
    {
        public const string YouTube = "youtube";
        public const string RepositoryHost = "repository_host";
        public const string WebsiteHost = "website_host";
        public const string DeepWiki = "deepwiki";
    }

    public bool IsActive() => Status == StatusValues.Active;

    public bool IsExpired(DateTimeOffset now) => Status == StatusValues.Expired || RetryAfterAt <= now;

    public void Expire() => Status = StatusValues.Expired;

    public void Clear() => Status = StatusValues.Cleared;
}
