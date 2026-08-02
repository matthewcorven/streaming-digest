using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class IngestionItemConfiguration : IEntityTypeConfiguration<IngestionItem>
{
    public void Configure(EntityTypeBuilder<IngestionItem> builder)
    {
        builder.ToTable("ingestion_items", "public");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.IngestionRunId).HasColumnName("ingestion_run_id");
        builder.Property(item => item.OperationId).HasColumnName("operation_id");
        builder.Property(item => item.ItemType).HasColumnName("item_type").IsRequired().HasMaxLength(64);
        builder.Property(item => item.ItemId).HasColumnName("item_id");
        builder.Property(item => item.ExternalKey).HasColumnName("external_key");
        builder.Property(item => item.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(item => item.DependsOnItemId).HasColumnName("depends_on_item_id");
        builder.Property(item => item.Stage).HasColumnName("stage").IsRequired().HasMaxLength(128);
        builder.Property(item => item.StageVersion).HasColumnName("stage_version").HasMaxLength(128);
        builder.Property(item => item.JobPayloadVersion).HasColumnName("job_payload_version").HasMaxLength(128);
        builder.Property(item => item.Status).HasColumnName("status").IsRequired().HasMaxLength(64);
        builder.Property(item => item.Attempt).HasColumnName("attempt");
        builder.Property(item => item.RetryCount).HasColumnName("retry_count");
        builder.Property(item => item.MaxAttempts).HasColumnName("max_attempts");
        builder.Property(item => item.IsRetryable).HasColumnName("is_retryable");
        builder.Property(item => item.NextRetryAt).HasColumnName("next_retry_at").HasColumnType("timestamptz");
        builder.Property(item => item.DeferredUntil).HasColumnName("deferred_until").HasColumnType("timestamptz");
        builder.Property(item => item.DefermentReason).HasColumnName("deferment_reason");
        builder.Property(item => item.WorkerId).HasColumnName("worker_id").HasMaxLength(256);
        builder.Property(item => item.StartedByJobId).HasColumnName("started_by_job_id").HasMaxLength(128);
        builder.Property(item => item.CompletedByJobId).HasColumnName("completed_by_job_id").HasMaxLength(128);
        builder.Property(item => item.ErrorSummary).HasColumnName("error_summary");
        builder.Property(item => item.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        builder.Property(item => item.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        // Per-stage status tracking
        builder.Property(item => item.TranscriptStatus).HasColumnName("transcript_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(item => item.SegmentsStatus).HasColumnName("segments_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(item => item.ScreenshotsStatus).HasColumnName("screenshots_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(item => item.LinksStatus).HasColumnName("links_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(item => item.ReposStatus).HasColumnName("repos_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(item => item.WebsitesStatus).HasColumnName("websites_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(item => item.EmbeddingsStatus).HasColumnName("embeddings_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
    }
}
