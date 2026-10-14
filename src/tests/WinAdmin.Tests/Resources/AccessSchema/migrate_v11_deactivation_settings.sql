-- AD Manager: настройки деактивации пользователей
-- Run: psql -h HOST -U access -d access -f install/migrate_v11_deactivation_settings.sql

ALTER TABLE terminated_user_ous
    ADD COLUMN IF NOT EXISTS is_deactivation_default BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE app_settings
    ADD COLUMN IF NOT EXISTS fired_users_group VARCHAR(512) NOT NULL DEFAULT '';
