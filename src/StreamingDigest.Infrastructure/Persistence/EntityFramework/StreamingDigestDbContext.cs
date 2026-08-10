using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class StreamingDigestDbContext(DbContextOptions<StreamingDigestDbContext> options) : DbContext(options), IStreamingDigestDbContext
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<SegmentGeneration> SegmentGenerations => Set<SegmentGeneration>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<SegmentScreenshot> SegmentScreenshots => Set<SegmentScreenshot>();
    public DbSet<Digest> Digests => Set<Digest>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DomainEvent> DomainEvents => Set<DomainEvent>();
    public DbSet<MediaArtifact> MediaArtifacts => Set<MediaArtifact>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OperationRecord> Operations => Set<OperationRecord>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<IngestionItem> IngestionItems => Set<IngestionItem>();
    public DbSet<ScrapedPage> ScrapedPages => Set<ScrapedPage>();
    public DbSet<RateLimitDeferment> RateLimitDeferments => Set<RateLimitDeferment>();
    public DbSet<VideoTranscript> VideoTranscripts => Set<VideoTranscript>();
    public DbSet<TranscriptCue> TranscriptCues => Set<TranscriptCue>();
    public DbSet<SegmentTranscriptRange> SegmentTranscriptRanges => Set<SegmentTranscriptRange>();
    public DbSet<ExternalResource> ExternalResources => Set<ExternalResource>();
    public DbSet<RepositoryRecord> Repositories => Set<RepositoryRecord>();
    public DbSet<FieldOverrideHistory> FieldOverrideHistories => Set<FieldOverrideHistory>();
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ChannelConfiguration());
        modelBuilder.ApplyConfiguration(new VideoConfiguration());
        modelBuilder.ApplyConfiguration(new SegmentGenerationConfiguration());
        modelBuilder.ApplyConfiguration(new SegmentConfiguration());
        modelBuilder.ApplyConfiguration(new SegmentScreenshotConfiguration());
        modelBuilder.ApplyConfiguration(new DigestConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationConfiguration());
        modelBuilder.ApplyConfiguration(new DomainEventConfiguration());
        modelBuilder.ApplyConfiguration(new MediaArtifactConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OperationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new IngestionRunConfiguration());
        modelBuilder.ApplyConfiguration(new IngestionItemConfiguration());
        modelBuilder.ApplyConfiguration(new ScrapedPageConfiguration());
        modelBuilder.ApplyConfiguration(new RateLimitDefermentConfiguration());
        modelBuilder.ApplyConfiguration(new VideoTranscriptConfiguration());
        modelBuilder.ApplyConfiguration(new TranscriptCueConfiguration());
        modelBuilder.ApplyConfiguration(new SegmentTranscriptRangeConfiguration());
        modelBuilder.ApplyConfiguration(new ExternalResourceConfiguration());
        modelBuilder.ApplyConfiguration(new RepositoryRecordConfiguration());
        modelBuilder.ApplyConfiguration(new FieldOverrideHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new NoteConfiguration());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    private void UpdateTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
