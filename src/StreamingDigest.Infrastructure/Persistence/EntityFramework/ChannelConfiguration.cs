using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("channels", "public");
        builder.HasKey(channel => channel.Id);

        builder.Property(channel => channel.Id).ValueGeneratedOnAdd();
        builder.Property(channel => channel.YoutubeChannelId).IsRequired().HasMaxLength(255);
        builder.Property(channel => channel.NameOriginal).IsRequired().HasMaxLength(255);
        builder.Property(channel => channel.NameOverride).HasMaxLength(255);
        builder.Property(channel => channel.ProfileUrl).IsRequired().HasMaxLength(2048);
        builder.Property(channel => channel.SourceUrl).IsRequired().HasMaxLength(2048);
        builder.Property(channel => channel.DescriptionOriginal).HasMaxLength(4000);
        builder.Property(channel => channel.DescriptionOverride).HasMaxLength(4000);
        builder.Property(channel => channel.LastIngestionStatus).HasMaxLength(64);

        builder.Property(channel => channel.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(channel => channel.UpdatedAt).HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(channel => channel.RowVersion).IsRowVersion();

        builder.HasIndex(channel => channel.YoutubeChannelId).IsUnique();
    }
}
