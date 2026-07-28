CREATE TABLE IF NOT EXISTS public.scraped_pages (
    id uuid PRIMARY KEY,
    external_resource_id uuid NOT NULL,
    final_url text NOT NULL,
    title_original text NULL,
    title_override text NULL,
    description_original text NULL,
    description_override text NULL,
    opengraph_json jsonb NULL,
    visible_text_original text NULL,
    visible_text_override text NULL,
    robots_allowed boolean NULL,
    scrape_status text NOT NULL,
    exclusion_reason text NULL,
    http_status integer NULL,
    content_type text NULL,
    content_hash text NULL,
    fetch_duration_ms integer NULL,
    page_size_bytes bigint NULL,
    scraped_at timestamptz NULL,
    raw_html_debug_path text NULL,
    error_summary text NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_scraped_pages_external_resource_id
    ON public.scraped_pages (external_resource_id);

CREATE INDEX IF NOT EXISTS idx_scraped_pages_final_url
    ON public.scraped_pages (final_url);

CREATE INDEX IF NOT EXISTS idx_scraped_pages_scrape_status
    ON public.scraped_pages (scrape_status);

CREATE INDEX IF NOT EXISTS idx_scraped_pages_created_at
    ON public.scraped_pages (created_at);
