using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("notes", "public");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(n => n.TargetType).HasColumnName("target_type").IsRequired().HasMaxLength(64);
        builder.Property(n => n.TargetId).HasColumnName("target_id").IsRequired();
        builder.Property(n => n.Title).HasColumnName("title");
        builder.Property(n => n.Markdown).HasColumnName("markdown").IsRequired();
        builder.Property(n => n.EmbeddingStatus).HasColumnName("embedding_status").IsRequired().HasMaxLength(32).HasDefaultValue("stale");
        builder.Property(n => n.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(n => n.RowVersion);

        builder.HasIndex(n => new { n.TargetType, n.TargetId })
            .HasFilter("deleted_at IS NULL")
            .IsUnique()
            .HasDatabaseName("idx_notes_target_unique_live");

        builder.HasIndex(n => n.EmbeddingStatus)
            .HasDatabaseName("idx_notes_embedding_status");
    }
}
