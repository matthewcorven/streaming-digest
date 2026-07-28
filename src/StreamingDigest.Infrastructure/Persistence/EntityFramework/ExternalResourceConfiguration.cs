using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class ExternalResourceConfiguration : IEntityTypeConfiguration<ExternalResource>
{
    public void Configure(EntityTypeBuilder<ExternalResource> builder)
    {
        builder.ToTable("external_resources", "public");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(r => r.CanonicalUrl).HasColumnName("canonical_url").IsRequired().HasMaxLength(2048);
        builder.Property(r => r.FinalUrl).HasColumnName("final_url").HasMaxLength(2048);
        builder.Property(r => r.Domain).HasColumnName("domain").HasMaxLength(512);
        builder.Property(r => r.ResourceType).HasColumnName("resource_type").IsRequired().HasMaxLength(64).HasDefaultValue("unknown");
        builder.Property(r => r.TitleOriginal).HasColumnName("title_original").HasMaxLength(1024);
        builder.Property(r => r.TitleOverride).HasColumnName("title_override").HasMaxLength(1024);
        builder.Property(r => r.DescriptionOriginal).HasColumnName("description_original");
        builder.Property(r => r.DescriptionOverride).HasColumnName("description_override");
        builder.Property(r => r.ClassificationOriginal).HasColumnName("classification_original").IsRequired().HasMaxLength(64).HasDefaultValue("unknown");
        builder.Property(r => r.ClassificationOverride).HasColumnName("classification_override").HasMaxLength(64);
        builder.Property(r => r.ClassificationConfidence).HasColumnName("classification_confidence").HasColumnType("numeric");
        builder.Property(r => r.ClassificationMethod).HasColumnName("classification_method").HasMaxLength(64);
        builder.Property(r => r.IsAdOrSponsor).HasColumnName("is_ad_or_sponsor").HasDefaultValue(false);
        builder.Property(r => r.RawMetadataJson).HasColumnName("raw_metadata_json").HasColumnType("jsonb");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(r => r.RowVersion);

        builder.HasIndex(r => r.CanonicalUrl).HasDatabaseName("idx_external_resources_canonical_url").IsUnique();
    }
}
