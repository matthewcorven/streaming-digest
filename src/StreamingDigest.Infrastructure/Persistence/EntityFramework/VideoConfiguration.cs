using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.ToTable("videos", "public");
        builder.HasKey(video => video.Id);

        builder.Property(video => video.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(video => video.Title).HasColumnName("title_original").IsRequired().HasMaxLength(1024);
        builder.Property(video => video.Platform).HasColumnName("platform").IsRequired().HasMaxLength(64).HasDefaultValue("youtube");
        builder.Property(video => video.PlatformVideoUrl).HasColumnName("platform_video_url").IsRequired().HasMaxLength(2048);
        builder.Property(video => video.PlatformVideoId).HasColumnName("platform_video_id").IsRequired().HasMaxLength(255);
        builder.Property(video => video.YoutubeVideoId).HasColumnName("youtube_video_id").IsRequired().HasMaxLength(255);
        builder.Property(video => video.ChannelId).HasColumnName("channel_id");
        builder.Property(video => video.TitleOverride).HasColumnName("title_override").HasMaxLength(1024);
        builder.Property(video => video.AuthorOriginal).HasColumnName("author_original").IsRequired().HasMaxLength(1024);
        builder.Property(video => video.AuthorOverride).HasColumnName("author_override").HasMaxLength(1024);
        builder.Property(video => video.DescriptionOriginal).HasColumnName("description_original").HasMaxLength(4000);
        builder.Property(video => video.DescriptionOverride).HasColumnName("description_override").HasMaxLength(4000);
        builder.Property(video => video.VideoUrl).HasColumnName("video_url").IsRequired().HasMaxLength(2048);
        builder.Property(video => video.PublishedAt).HasColumnName("published_at");
        builder.Property(video => video.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(video => video.ChaptersJson).HasColumnName("chapters_json");
        builder.Property(video => video.CaptionsJson).HasColumnName("captions_json");
        builder.Property(video => video.ThumbnailUrl).HasColumnName("thumbnail_url");
        builder.Property(video => video.IsLongForm).HasColumnName("is_long_form");
        builder.Property(video => video.IngestionStatus).HasColumnName("ingestion_status").IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(video => video.TranscriptStatus).HasColumnName("transcript_status").IsRequired().HasMaxLength(64).HasDefaultValue("unknown");
        builder.Property(video => video.ScreenshotStatus).HasColumnName("screenshot_status").IsRequired().HasMaxLength(64).HasDefaultValue("unknown");
        builder.Property(video => video.ProcessingVersion).HasColumnName("processing_version").HasMaxLength(128);
        builder.Property(video => video.LastSuccessfulIngestionRunId).HasColumnName("last_successful_ingestion_run_id");
        builder.Property(video => video.LastFailedIngestionRunId).HasColumnName("last_failed_ingestion_run_id");
        builder.Property(video => video.MetadataFetchedAt).HasColumnName("metadata_fetched_at");
        builder.Property(video => video.TranscriptFetchedAt).HasColumnName("transcript_fetched_at");
        builder.Property(video => video.LinksExtractedAt).HasColumnName("links_extracted_at");
        builder.Property(video => video.SearchIndexedAt).HasColumnName("search_indexed_at");
        builder.Property(video => video.RawMetadataJson).HasColumnName("raw_metadata_json").HasColumnType("jsonb");
        builder.Property(video => video.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(video => video.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(video => video.RowVersion);

        builder.HasIndex(video => new { video.Platform, video.PlatformVideoId }).HasDatabaseName("idx_videos_platform_video_id").IsUnique();
        builder.HasIndex(video => video.ChannelId).HasDatabaseName("idx_videos_channel_id");
        builder.HasOne(video => video.Channel)
            .WithMany()
            .HasForeignKey(video => video.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
