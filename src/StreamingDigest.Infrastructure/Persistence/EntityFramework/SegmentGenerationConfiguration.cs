using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class SegmentGenerationConfiguration : IEntityTypeConfiguration<SegmentGeneration>
{
    public void Configure(EntityTypeBuilder<SegmentGeneration> builder)
    {
        builder.ToTable("segment_generations", "public");
        builder.HasKey(gen => gen.Id);

        builder.Property(gen => gen.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(gen => gen.VideoId).HasColumnName("video_id");
        builder.Property(gen => gen.SourceType).HasColumnName("source_type").IsRequired().HasMaxLength(64);
        builder.Property(gen => gen.GenerationVersion).HasColumnName("generation_version");
        builder.Property(gen => gen.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        builder.Property(gen => gen.RequiresUserApproval).HasColumnName("requires_user_approval").HasDefaultValue(false);
        builder.Property(gen => gen.Status).HasColumnName("status").IsRequired().HasMaxLength(64);
        builder.Property(gen => gen.LlmModel).HasColumnName("llm_model").HasMaxLength(255);
        builder.Property(gen => gen.LlmPromptVersion).HasColumnName("llm_prompt_version").HasMaxLength(128);
        builder.Property(gen => gen.CreatedByOperationId).HasColumnName("created_by_operation_id");
        builder.Property(gen => gen.ActivatedAt).HasColumnName("activated_at").HasColumnType("timestamptz");
        builder.Property(gen => gen.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Ignore(gen => gen.Screenshots);
        builder.Ignore(gen => gen.Notes);
        builder.Ignore(gen => gen.PendingInboxItems);

        builder.HasIndex(gen => new { gen.VideoId, gen.GenerationVersion })
            .HasDatabaseName("idx_segment_generations_video_version")
            .IsUnique();

        builder.HasIndex(gen => gen.VideoId)
            .HasDatabaseName("idx_segment_generations_video_id");

        builder.HasIndex(gen => gen.VideoId)
            .HasFilter("is_active = true")
            .HasDatabaseName("idx_segment_generations_video_is_active")
            .IsUnique();

        builder.HasMany(gen => gen.Segments)
            .WithOne()
            .HasForeignKey(seg => seg.SegmentGenerationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
