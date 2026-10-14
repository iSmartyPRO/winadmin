-- AD Manager: Microsoft Exchange server URL
-- Run: psql -h HOST -U access -d access -f install/migrate_v6_exchange.sql

ALTER TABLE app_settings
    ADD COLUMN IF NOT EXISTS exchange_url VARCHAR(512) NOT NULL DEFAULT '';
