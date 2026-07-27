using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class SegmentConfiguration : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> builder)
    {
        builder.ToTable("segments", "public");
        builder.HasKey(seg => seg.Id);

        builder.Property(seg => seg.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(seg => seg.VideoId).HasColumnName("video_id");
        builder.Property(seg => seg.SegmentGenerationId).HasColumnName("segment_generation_id");
        builder.Property(seg => seg.SourceType).HasColumnName("source_type").IsRequired().HasMaxLength(64);
        builder.Property(seg => seg.Sequence).HasColumnName("sequence");
        builder.Property(seg => seg.StartSeconds).HasColumnName("start_seconds").HasColumnType("numeric");
        builder.Property(seg => seg.EndSeconds).HasColumnName("end_seconds").HasColumnType("numeric");
        builder.Property(seg => seg.TitleOriginal).HasColumnName("title_original").IsRequired().HasMaxLength(1024);
        builder.Property(seg => seg.TitleOverride).HasColumnName("title_override").HasMaxLength(1024);
        builder.Property(seg => seg.SummaryOriginal).HasColumnName("summary_original");
        builder.Property(seg => seg.SummaryOverride).HasColumnName("summary_override");
        builder.Property(seg => seg.LlmModel).HasColumnName("llm_model").HasMaxLength(255);
        builder.Property(seg => seg.LlmPromptVersion).HasColumnName("llm_prompt_version").HasMaxLength(128);
        builder.Property(seg => seg.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(seg => seg.RequiresEmbeddingApproval).HasColumnName("requires_embedding_approval").HasDefaultValue(false);
        builder.Property(seg => seg.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(seg => seg.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(seg => seg.RowVersion);

        builder.HasIndex(seg => new { seg.SegmentGenerationId, seg.Sequence })
            .HasDatabaseName("idx_segments_generation_sequence")
            .IsUnique();

        builder.HasIndex(seg => new { seg.VideoId, seg.StartSeconds })
            .HasDatabaseName("idx_segments_video_start_seconds");

        builder.HasIndex(seg => new { seg.VideoId, seg.IsActive })
            .HasDatabaseName("idx_segments_video_is_active");
    }
}
