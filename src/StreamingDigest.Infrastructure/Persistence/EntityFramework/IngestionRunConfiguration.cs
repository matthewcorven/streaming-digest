using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class IngestionRunConfiguration : IEntityTypeConfiguration<IngestionRun>
{
    public void Configure(EntityTypeBuilder<IngestionRun> builder)
    {
        builder.ToTable("ingestion_runs", "public");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(run => run.OperationId).HasColumnName("operation_id");
        builder.Property(run => run.CorrelationId).HasColumnName("correlation_id");
        builder.Property(run => run.ScheduleId).HasColumnName("schedule_id");
        builder.Property(run => run.RunType).HasColumnName("run_type").IsRequired().HasMaxLength(64);
        builder.Property(run => run.TriggeredBy).HasColumnName("triggered_by").IsRequired().HasMaxLength(128);
        builder.Property(run => run.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(run => run.Status).HasColumnName("status").IsRequired().HasMaxLength(64);
        builder.Property(run => run.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        builder.Property(run => run.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(run => run.ChannelsChecked).HasColumnName("channels_checked");
        builder.Property(run => run.NewVideosFound).HasColumnName("new_videos_found");
        builder.Property(run => run.VideosIngested).HasColumnName("videos_ingested");
        builder.Property(run => run.VideosFailed).HasColumnName("videos_failed");
        builder.Property(run => run.VideosSkipped).HasColumnName("videos_skipped");
        builder.Property(run => run.TranscriptsFound).HasColumnName("transcripts_found");
        builder.Property(run => run.TranscriptsMissing).HasColumnName("transcripts_missing");
        builder.Property(run => run.RepositoriesFound).HasColumnName("repositories_found");
        builder.Property(run => run.ConfigSnapshotJson).HasColumnName("config_snapshot_json").HasColumnType("jsonb");
        builder.Property(run => run.SummaryJson).HasColumnName("summary_json").HasColumnType("jsonb");
        builder.Property(run => run.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
    }
}
