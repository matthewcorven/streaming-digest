using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("channels", "public");
        builder.HasKey(channel => channel.Id);

        builder.Property(channel => channel.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(channel => channel.YoutubeChannelId).HasColumnName("youtube_channel_id").IsRequired().HasMaxLength(255);
        builder.Property(channel => channel.NameOriginal).HasColumnName("name_original").IsRequired().HasMaxLength(255);
        builder.Property(channel => channel.NameOverride).HasColumnName("name_override").HasMaxLength(255);
        builder.Property(channel => channel.ProfileUrl).HasColumnName("profile_url").IsRequired().HasMaxLength(2048);
        builder.Property(channel => channel.SourceUrl).HasColumnName("source_url").IsRequired().HasMaxLength(2048);
        builder.Property(channel => channel.DescriptionOriginal).HasColumnName("description_original").HasMaxLength(4000);
        builder.Property(channel => channel.DescriptionOverride).HasColumnName("description_override").HasMaxLength(4000);
        builder.Property(channel => channel.IsPaused).HasColumnName("is_paused");
        builder.Property(channel => channel.DefaultMaxAgeDays).HasColumnName("default_max_age_days");
        builder.Property(channel => channel.DefaultBackfillMaxVideos).HasColumnName("default_backfill_max_videos");
        builder.Property(channel => channel.IsDegraded).HasColumnName("is_degraded");
        builder.Property(channel => channel.ConsecutiveFailures).HasColumnName("consecutive_failures");
        builder.Property(channel => channel.LastProbeAt).HasColumnName("last_probe_at");
        builder.Property(channel => channel.DegradedAt).HasColumnName("degraded_at");
        builder.Property(channel => channel.LastIngestedAt).HasColumnName("last_ingested_at");
        builder.Property(channel => channel.LastIngestionStatus).HasColumnName("last_ingestion_status").HasMaxLength(64);

        builder.Property(channel => channel.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(channel => channel.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(channel => channel.RowVersion);

        builder.HasIndex(channel => channel.YoutubeChannelId).HasDatabaseName("idx_channels_youtube_channel_id").IsUnique();
    }
}
