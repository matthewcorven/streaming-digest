using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class RepositoryRecordConfiguration : IEntityTypeConfiguration<RepositoryRecord>
{
    public void Configure(EntityTypeBuilder<RepositoryRecord> builder)
    {
        builder.ToTable("repositories", "public");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(r => r.Host).HasColumnName("host").IsRequired().HasMaxLength(64);
        builder.Property(r => r.CanonicalUrl).HasColumnName("canonical_url").IsRequired().HasMaxLength(2048);
        builder.Property(r => r.Owner).HasColumnName("owner").HasMaxLength(512);
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(512);
        builder.Property(r => r.NormalizedOwner).HasColumnName("normalized_owner").HasMaxLength(512);
        builder.Property(r => r.NormalizedName).HasColumnName("normalized_name").HasMaxLength(512);
        builder.Property(r => r.DefaultBranch).HasColumnName("default_branch").HasMaxLength(255);
        builder.Property(r => r.DescriptionOriginal).HasColumnName("description_original");
        builder.Property(r => r.DescriptionOverride).HasColumnName("description_override");
        builder.Property(r => r.Stars).HasColumnName("stars");
        builder.Property(r => r.Forks).HasColumnName("forks");
        builder.Property(r => r.PrimaryLanguage).HasColumnName("primary_language").HasMaxLength(128);
        builder.Property(r => r.Topics).HasColumnName("topics").HasColumnType("text[]");
        builder.Property(r => r.LicenseSpdxId).HasColumnName("license_spdx_id").HasMaxLength(64);
        builder.Property(r => r.DeepwikiUrl).HasColumnName("deepwiki_url").HasMaxLength(2048);
        builder.Property(r => r.DeepwikiCheckedAt).HasColumnName("deepwiki_checked_at").HasColumnType("timestamptz");
        builder.Property(r => r.RawMetadataJson).HasColumnName("raw_metadata_json").HasColumnType("jsonb");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(r => r.RowVersion);

        builder.HasIndex(r => r.CanonicalUrl).HasDatabaseName("idx_repositories_canonical_url").IsUnique();
    }
}
