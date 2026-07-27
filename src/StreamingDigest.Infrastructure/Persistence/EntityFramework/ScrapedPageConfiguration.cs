using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

internal sealed class ScrapedPageConfiguration : IEntityTypeConfiguration<ScrapedPage>
{
    public void Configure(EntityTypeBuilder<ScrapedPage> builder)
    {
        builder.ToTable("scraped_pages", "public");
        builder.HasKey(page => page.Id);

        builder.Property(page => page.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(page => page.ExternalResourceId).HasColumnName("external_resource_id").IsRequired();
        builder.Property(page => page.FinalUrl).HasColumnName("final_url").IsRequired().HasMaxLength(4096);
        builder.Property(page => page.TitleOriginal).HasColumnName("title_original").HasMaxLength(2048);
        builder.Property(page => page.TitleOverride).HasColumnName("title_override").HasMaxLength(2048);
        builder.Property(page => page.DescriptionOriginal).HasColumnName("description_original");
        builder.Property(page => page.DescriptionOverride).HasColumnName("description_override");
        builder.Property(page => page.OpenGraphJson).HasColumnName("opengraph_json").HasColumnType("jsonb");
        builder.Property(page => page.VisibleTextOriginal).HasColumnName("visible_text_original");
        builder.Property(page => page.VisibleTextOverride).HasColumnName("visible_text_override");
        builder.Property(page => page.RobotsAllowed).HasColumnName("robots_allowed");
        builder.Property(page => page.ScrapeStatus).HasColumnName("scrape_status").IsRequired().HasMaxLength(32);
        builder.Property(page => page.ExclusionReason).HasColumnName("exclusion_reason");
        builder.Property(page => page.HttpStatus).HasColumnName("http_status");
        builder.Property(page => page.ContentType).HasColumnName("content_type").HasMaxLength(256);
        builder.Property(page => page.ContentHash).HasColumnName("content_hash").HasMaxLength(512);
        builder.Property(page => page.FetchDurationMs).HasColumnName("fetch_duration_ms");
        builder.Property(page => page.PageSizeBytes).HasColumnName("page_size_bytes");
        builder.Property(page => page.ScrapedAt).HasColumnName("scraped_at").HasColumnType("timestamptz");
        builder.Property(page => page.RawHtmlDebugPath).HasColumnName("raw_html_debug_path").HasMaxLength(4096);
        builder.Property(page => page.ErrorSummary).HasColumnName("error_summary");
        builder.Property(page => page.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(page => page.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(page => page.RowVersion);

        builder.HasIndex(page => page.ExternalResourceId);
        builder.HasIndex(page => page.FinalUrl);
        builder.HasIndex(page => page.ScrapeStatus);
        builder.HasIndex(page => page.CreatedAt);
    }
}
