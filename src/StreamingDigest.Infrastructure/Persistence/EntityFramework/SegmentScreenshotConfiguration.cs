using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class SegmentScreenshotConfiguration : IEntityTypeConfiguration<SegmentScreenshot>
{
    public void Configure(EntityTypeBuilder<SegmentScreenshot> builder)
    {
        builder.ToTable("screenshots", "public");
        builder.HasKey(shot => shot.Id);

        builder.Property(shot => shot.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(shot => shot.VideoId).HasColumnName("video_id");
        builder.Property(shot => shot.SegmentId).HasColumnName("segment_id");
        builder.Property(shot => shot.TimestampSeconds).HasColumnName("timestamp_seconds").HasColumnType("numeric");
        builder.Property(shot => shot.FilePath).HasColumnName("file_path").IsRequired();
        builder.Property(shot => shot.StorageKey).HasColumnName("storage_key");
        builder.Property(shot => shot.PublicUrlPath).HasColumnName("public_url_path");
        builder.Property(shot => shot.MimeType).HasColumnName("mime_type").IsRequired().HasMaxLength(64).HasDefaultValue("image/webp");
        builder.Property(shot => shot.Width).HasColumnName("width");
        builder.Property(shot => shot.Height).HasColumnName("height");
        builder.Property(shot => shot.FileSizeBytes).HasColumnName("file_size_bytes");
        builder.Property(shot => shot.ContentHash).HasColumnName("content_hash");
        builder.Property(shot => shot.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        // In-memory grouping only — no persisted column (see entity remark).
        builder.Ignore(shot => shot.SegmentGenerationId);
        builder.Ignore(shot => shot.IsActive);

        builder.HasIndex(shot => shot.VideoId)
            .HasDatabaseName("idx_screenshots_video_id");

        builder.HasIndex(shot => shot.SegmentId)
            .HasDatabaseName("idx_screenshots_segment_id");
    }
}
