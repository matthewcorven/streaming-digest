UPDATE public.app_settings
SET
    value_json = to_jsonb(70),
    updated_at = CURRENT_TIMESTAMP
WHERE key = 'search.highSignalThresholdPercent'
  AND value_json::text = '80';
