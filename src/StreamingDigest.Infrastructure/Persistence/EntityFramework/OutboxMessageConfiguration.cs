using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", "public");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id).ValueGeneratedOnAdd();
        builder.Property(message => message.MessageType).IsRequired().HasMaxLength(128);
        builder.Property(message => message.AggregateType).HasMaxLength(128);
        builder.Property(message => message.AggregateId);
        builder.Property(message => message.PayloadJson).IsRequired().HasColumnType("jsonb");
        builder.Property(message => message.Status).IsRequired().HasMaxLength(32);
        builder.Property(message => message.AttemptCount).HasDefaultValue(0);
        builder.Property(message => message.NextAttemptAt).HasColumnType("timestamptz");
        builder.Property(message => message.LastErrorSummary);
        builder.Property(message => message.SentAt).HasColumnType("timestamptz");
        builder.Property(message => message.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(message => message.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(message => message.RowVersion).IsRowVersion();

        builder.HasIndex(message => message.Status);
        builder.HasIndex(message => message.NextAttemptAt);
    }
}
