using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class DomainEventConfiguration : IEntityTypeConfiguration<DomainEvent>
{
    public void Configure(EntityTypeBuilder<DomainEvent> builder)
    {
        builder.ToTable("domain_events", "public");
        builder.HasKey(domainEvent => domainEvent.Id);

        builder.Property(domainEvent => domainEvent.Id).ValueGeneratedOnAdd();
        builder.Property(domainEvent => domainEvent.EventType).IsRequired().HasMaxLength(128);
        builder.Property(domainEvent => domainEvent.Severity).IsRequired().HasMaxLength(32);
        builder.Property(domainEvent => domainEvent.EntityType).HasMaxLength(128);
        builder.Property(domainEvent => domainEvent.Message).IsRequired();
        builder.Property(domainEvent => domainEvent.DetailsJson).HasColumnType("jsonb");
        builder.Property(domainEvent => domainEvent.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(domainEvent => domainEvent.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(domainEvent => domainEvent.RowVersion).IsRowVersion();

        builder.HasIndex(domainEvent => domainEvent.IngestionRunId);
        builder.HasIndex(domainEvent => domainEvent.OperationId);
        builder.HasIndex(domainEvent => domainEvent.EventType);
        builder.HasIndex(domainEvent => domainEvent.Severity);
        builder.HasIndex(domainEvent => new { domainEvent.EntityType, domainEvent.EntityId });
        builder.HasIndex(domainEvent => domainEvent.CreatedAt);
    }
}
