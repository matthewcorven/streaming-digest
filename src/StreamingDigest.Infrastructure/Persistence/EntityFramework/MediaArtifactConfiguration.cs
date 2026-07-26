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

        builder.Property(artifact => artifact.Id).ValueGeneratedOnAdd();
        builder.Property(artifact => artifact.OwnerType).IsRequired().HasMaxLength(128);
        builder.Property(artifact => artifact.ArtifactKind).IsRequired().HasMaxLength(128);
        builder.Property(artifact => artifact.FilePath).IsRequired().HasMaxLength(4096);
        builder.Property(artifact => artifact.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(artifact => artifact.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(artifact => artifact.RowVersion).IsRowVersion();

        builder.HasIndex(artifact => new { artifact.OwnerType, artifact.OwnerId });
        builder.HasIndex(artifact => artifact.ArtifactKind);
        builder.HasIndex(artifact => artifact.FilePath).IsUnique();
    }
}
