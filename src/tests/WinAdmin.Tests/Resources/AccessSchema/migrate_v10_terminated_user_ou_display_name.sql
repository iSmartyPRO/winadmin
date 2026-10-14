-- AD Manager: человекочитаемое название OU уволенных
-- Run: psql -h HOST -U access -d access -f install/migrate_v10_terminated_user_ou_display_name.sql

ALTER TABLE terminated_user_ous
    ADD COLUMN IF NOT EXISTS display_name VARCHAR(256);
