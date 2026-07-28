using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class SegmentTranscriptRangeConfiguration : IEntityTypeConfiguration<SegmentTranscriptRange>
{
    public void Configure(EntityTypeBuilder<SegmentTranscriptRange> builder)
    {
        builder.ToTable("segment_transcript_ranges", "public");
        builder.HasKey(r => new { r.SegmentId, r.TranscriptCueId });

        builder.Property(r => r.SegmentId).HasColumnName("segment_id");
        builder.Property(r => r.TranscriptCueId).HasColumnName("transcript_cue_id");

        builder.HasIndex(r => r.SegmentId)
            .HasDatabaseName("idx_segment_transcript_ranges_segment_id");

        builder.HasIndex(r => r.TranscriptCueId)
            .HasDatabaseName("idx_segment_transcript_ranges_cue_id");
    }
}
