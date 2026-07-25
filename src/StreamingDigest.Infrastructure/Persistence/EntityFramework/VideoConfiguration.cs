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

        builder.Property(video => video.Id).ValueGeneratedOnAdd();
        builder.Property(video => video.Title).IsRequired().HasMaxLength(1024);
        builder.Property(video => video.Platform).IsRequired().HasMaxLength(64).HasDefaultValue("youtube");
        builder.Property(video => video.PlatformVideoUrl).IsRequired().HasMaxLength(2048);
        builder.Property(video => video.PlatformVideoId).IsRequired().HasMaxLength(255);
        builder.Property(video => video.YoutubeVideoId).IsRequired().HasMaxLength(255);
        builder.Property(video => video.AuthorOriginal).IsRequired().HasMaxLength(1024);
        builder.Property(video => video.AuthorOverride).HasMaxLength(1024);
        builder.Property(video => video.DescriptionOriginal).HasMaxLength(4000);
        builder.Property(video => video.DescriptionOverride).HasMaxLength(4000);
        builder.Property(video => video.VideoUrl).IsRequired().HasMaxLength(2048);
        builder.Property(video => video.IngestionStatus).IsRequired().HasMaxLength(64).HasDefaultValue("pending");
        builder.Property(video => video.TranscriptStatus).IsRequired().HasMaxLength(64).HasDefaultValue("unknown");
        builder.Property(video => video.ScreenshotStatus).IsRequired().HasMaxLength(64).HasDefaultValue("unknown");
        builder.Property(video => video.ProcessingVersion).HasMaxLength(128);
        builder.Property(video => video.RawMetadataJson).HasColumnType("jsonb");
        builder.Property(video => video.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(video => video.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(video => video.RowVersion).IsRowVersion();

        builder.HasIndex(video => new { video.Platform, video.PlatformVideoId }).IsUnique();
        builder.HasIndex(video => video.ChannelId);
        builder.HasOne(video => video.Channel)
            .WithMany()
            .HasForeignKey(video => video.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
