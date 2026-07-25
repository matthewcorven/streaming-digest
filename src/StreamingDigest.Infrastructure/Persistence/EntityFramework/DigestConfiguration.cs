using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class DigestConfiguration : IEntityTypeConfiguration<Digest>
{
    public void Configure(EntityTypeBuilder<Digest> builder)
    {
        builder.ToTable("digests", "public");
        builder.HasKey(digest => digest.Id);

        builder.Property(digest => digest.Id).ValueGeneratedOnAdd();
        builder.Property(digest => digest.IngestionRunId).IsRequired();
        builder.Property(digest => digest.RunType).IsRequired().HasMaxLength(64);
        builder.Property(digest => digest.PayloadJson).IsRequired().HasColumnType("jsonb");
        builder.Property(digest => digest.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(digest => digest.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(digest => digest.RowVersion).IsRowVersion();

        builder.HasIndex(digest => digest.IngestionRunId).IsUnique();
    }
}
