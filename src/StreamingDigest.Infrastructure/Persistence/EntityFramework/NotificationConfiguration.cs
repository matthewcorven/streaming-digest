using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", "public");
        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id).ValueGeneratedOnAdd();
        builder.Property(notification => notification.OperationId);
        builder.Property(notification => notification.IngestionRunId);
        builder.Property(notification => notification.NotificationType).IsRequired().HasMaxLength(128);
        builder.Property(notification => notification.Provider).IsRequired().HasMaxLength(64);
        builder.Property(notification => notification.Target).IsRequired().HasMaxLength(512);
        builder.Property(notification => notification.Status).IsRequired().HasMaxLength(32);
        builder.Property(notification => notification.PayloadJson).HasColumnType("jsonb");
        builder.Property(notification => notification.RenderedBody);
        builder.Property(notification => notification.MessageSummary).HasMaxLength(1024);
        builder.Property(notification => notification.ProviderMessageId).HasMaxLength(512);
        builder.Property(notification => notification.AttemptCount).HasDefaultValue(0);
        builder.Property(notification => notification.NextRetryAt).HasColumnType("timestamptz");
        builder.Property(notification => notification.ErrorSummary);
        builder.Property(notification => notification.SentAt).HasColumnType("timestamptz");
        builder.Property(notification => notification.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(notification => notification.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(notification => notification.RowVersion).IsRowVersion();

        builder.HasIndex(notification => notification.IngestionRunId);
        builder.HasIndex(notification => notification.OperationId);
        builder.HasIndex(notification => notification.Status);
    }
}
