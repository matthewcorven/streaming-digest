using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class TranscriptCueConfiguration : IEntityTypeConfiguration<TranscriptCue>
{
    public void Configure(EntityTypeBuilder<TranscriptCue> builder)
    {
        builder.ToTable("transcript_cues", "public");
        builder.HasKey(cue => cue.Id);

        builder.Property(cue => cue.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(cue => cue.TranscriptId).HasColumnName("transcript_id");
        builder.Property(cue => cue.Sequence).HasColumnName("sequence");
        builder.Property(cue => cue.StartSeconds).HasColumnName("start_seconds").HasColumnType("numeric").IsRequired();
        builder.Property(cue => cue.EndSeconds).HasColumnName("end_seconds").HasColumnType("numeric");
        builder.Property(cue => cue.TextOriginal).HasColumnName("text_original").IsRequired();
        builder.Property(cue => cue.TextOverride).HasColumnName("text_override");
        builder.Property(cue => cue.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(cue => cue.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(cue => cue.RowVersion);

        builder.HasIndex(cue => new { cue.TranscriptId, cue.Sequence })
            .HasDatabaseName("idx_transcript_cues_transcript_id_sequence")
            .IsUnique();
    }
}
