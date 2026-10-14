-- AD Manager: external API keys (JSONB)
-- Run: psql -h HOST -U access -d access -f install/migrate_v7_api_settings.sql

ALTER TABLE app_settings
    ADD COLUMN IF NOT EXISTS api_settings JSONB NOT NULL DEFAULT '{}'::jsonb;

UPDATE app_settings
SET api_settings = jsonb_set(
    COALESCE(api_settings, '{}'::jsonb),
    '{yandexMaps}',
    jsonb_build_object('apiKey', yandex_maps_api_key),
    true
)
WHERE id = 1
  AND yandex_maps_api_key IS NOT NULL
  AND TRIM(yandex_maps_api_key) <> '';
