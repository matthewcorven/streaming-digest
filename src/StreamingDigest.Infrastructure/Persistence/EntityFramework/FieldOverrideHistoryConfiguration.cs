using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class FieldOverrideHistoryConfiguration : IEntityTypeConfiguration<FieldOverrideHistory>
{
    public void Configure(EntityTypeBuilder<FieldOverrideHistory> builder)
    {
        builder.ToTable("field_override_history", "public");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(h => h.EntityType).HasColumnName("entity_type").IsRequired().HasMaxLength(128);
        builder.Property(h => h.EntityId).HasColumnName("entity_id");
        builder.Property(h => h.FieldName).HasColumnName("field_name").IsRequired().HasMaxLength(128);
        builder.Property(h => h.PreviousValue).HasColumnName("previous_value");
        builder.Property(h => h.NewValue).HasColumnName("new_value");
        builder.Property(h => h.ChangedAt).HasColumnName("changed_at").HasColumnType("timestamptz");

        builder.HasIndex(h => new { h.EntityType, h.EntityId }).HasDatabaseName("idx_field_override_history_entity");
        builder.HasIndex(h => h.ChangedAt).HasDatabaseName("idx_field_override_history_changed_at");
    }
}
