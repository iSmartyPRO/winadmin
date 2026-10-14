-- AD Manager: OU уволенных пользователей (отображение в разделе «Пользователи»)
-- Run: psql -h HOST -U access -d access -f install/migrate_v8_terminated_user_ous.sql

CREATE TABLE IF NOT EXISTS terminated_user_ous (
    id         SERIAL PRIMARY KEY,
    ou_name    VARCHAR(256) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_terminated_user_ous_name UNIQUE (ou_name)
);
