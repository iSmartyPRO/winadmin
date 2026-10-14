-- AD Manager: role principals (AD users/groups per access type)
-- Run: psql -h HOST -U access -d access -f install/migrate_v4_role_principals.sql

CREATE TABLE IF NOT EXISTS role_principals (
    id                  SERIAL PRIMARY KEY,
    role_id             INT NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    principal_type      VARCHAR(10) NOT NULL,
    sam_account_name    VARCHAR(256),
    distinguished_name  VARCHAR(1024) NOT NULL,
    display_name        VARCHAR(512) NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by          VARCHAR(256)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_role_principals_dn
    ON role_principals (role_id, principal_type, distinguished_name);

CREATE INDEX IF NOT EXISTS ix_role_principals_role ON role_principals (role_id);
