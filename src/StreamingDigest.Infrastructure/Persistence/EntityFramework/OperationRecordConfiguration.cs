using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class OperationRecordConfiguration : IEntityTypeConfiguration<OperationRecord>
{
    public void Configure(EntityTypeBuilder<OperationRecord> builder)
    {
        builder.ToTable("operations", "public");
        builder.HasKey(operation => operation.Id);

        builder.Property(operation => operation.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(operation => operation.OperationType).HasColumnName("operation_type").IsRequired().HasMaxLength(128);
        builder.Property(operation => operation.Status).HasColumnName("status").IsRequired().HasMaxLength(64);
        builder.Property(operation => operation.RiskLevel).HasColumnName("risk_level").HasMaxLength(64);
        builder.Property(operation => operation.RequestedBy).HasColumnName("requested_by").HasMaxLength(256);
        builder.Property(operation => operation.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(128);
        builder.Property(operation => operation.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(operation => operation.HangfireJobId).HasColumnName("hangfire_job_id").HasMaxLength(128);
        builder.Property(operation => operation.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        builder.Property(operation => operation.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(operation => operation.SummaryJson).HasColumnName("summary_json").HasColumnType("jsonb");
        builder.Property(operation => operation.ErrorSummary).HasColumnName("error_summary");
        builder.Property(operation => operation.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(operation => operation.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
    }
}
