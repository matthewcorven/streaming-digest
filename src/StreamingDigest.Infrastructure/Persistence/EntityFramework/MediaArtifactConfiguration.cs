using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class MediaArtifactConfiguration : IEntityTypeConfiguration<MediaArtifact>
{
    public void Configure(EntityTypeBuilder<MediaArtifact> builder)
    {
        builder.ToTable("media_artifacts", "public");
        builder.HasKey(artifact => artifact.Id);

        builder.Property(artifact => artifact.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(artifact => artifact.OwnerType).HasColumnName("owner_type").IsRequired().HasMaxLength(128);
        builder.Property(artifact => artifact.OwnerId).HasColumnName("owner_id");
        builder.Property(artifact => artifact.ArtifactKind).HasColumnName("artifact_kind").IsRequired().HasMaxLength(128);
        builder.Property(artifact => artifact.FilePath).HasColumnName("file_path").IsRequired().HasMaxLength(4096);
        builder.Property(artifact => artifact.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(artifact => artifact.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(artifact => artifact.RowVersion);

        builder.HasIndex(artifact => new { artifact.OwnerType, artifact.OwnerId });
        builder.HasIndex(artifact => artifact.ArtifactKind);
        builder.HasIndex(artifact => artifact.FilePath).IsUnique();
    }
}
