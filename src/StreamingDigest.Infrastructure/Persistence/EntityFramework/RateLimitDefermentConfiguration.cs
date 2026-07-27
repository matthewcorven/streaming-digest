using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class RateLimitDefermentConfiguration : IEntityTypeConfiguration<RateLimitDeferment>
{
    public void Configure(EntityTypeBuilder<RateLimitDeferment> builder)
    {
        builder.ToTable("rate_limit_deferments", "public");
        builder.HasKey(deferment => deferment.Id);

        builder.Property(deferment => deferment.Id).ValueGeneratedOnAdd();
        builder.Property(deferment => deferment.ScopeType).IsRequired().HasMaxLength(64);
        builder.Property(deferment => deferment.ScopeKey).IsRequired().HasMaxLength(256);
        builder.Property(deferment => deferment.Reason).IsRequired().HasMaxLength(512);
        builder.Property(deferment => deferment.RetryAfterAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(deferment => deferment.Status).IsRequired().HasMaxLength(32);
        builder.Property(deferment => deferment.DetailsJson).HasColumnType("jsonb");
        builder.Property(deferment => deferment.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(deferment => deferment.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(deferment => deferment.RowVersion).IsRowVersion();

        builder.HasIndex(deferment => new { deferment.ScopeType, deferment.ScopeKey });
        builder.HasIndex(deferment => deferment.Status);
        builder.HasIndex(deferment => deferment.RetryAfterAt);
        builder.HasIndex(deferment => deferment.CreatedAt);
    }
}
