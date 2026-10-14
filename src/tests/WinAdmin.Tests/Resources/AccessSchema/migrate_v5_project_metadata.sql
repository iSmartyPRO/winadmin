-- AD Manager: project metadata + Yandex Maps API key
-- Run: psql -h HOST -U access -d access -f install/migrate_v5_project_metadata.sql

CREATE TABLE IF NOT EXISTS project_metadata (
    project_name        VARCHAR(256) PRIMARY KEY,
    address             VARCHAR(512),
    city                VARCHAR(256),
    latitude            DOUBLE PRECISION,
    longitude           DOUBLE PRECISION,
    mail_group_dn       VARCHAR(1024),
    mail_group_name     VARCHAR(512),
    mail_group_email    VARCHAR(256),
    hr_email            VARCHAR(256),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by          VARCHAR(256)
);

ALTER TABLE app_settings
    ADD COLUMN IF NOT EXISTS yandex_maps_api_key VARCHAR(256) NOT NULL DEFAULT '';
