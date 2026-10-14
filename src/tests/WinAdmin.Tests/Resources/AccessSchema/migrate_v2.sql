-- Migration: AD settings and local admin (run on existing databases)
-- psql -h HOST -U access -d access -f install/migrate_v2.sql

CREATE TABLE IF NOT EXISTS app_settings (
    id                   INT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    domain               VARCHAR(256) NOT NULL DEFAULT '',
    ldap_path            VARCHAR(512) NOT NULL DEFAULT '',
    ad_service_domain    VARCHAR(256) NOT NULL DEFAULT '',
    ad_service_username  VARCHAR(256) NOT NULL DEFAULT '',
    ad_service_password  VARCHAR(512) NOT NULL DEFAULT '',
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS local_users (
    id            SERIAL PRIMARY KEY,
    username      VARCHAR(256) NOT NULL,
    password_hash VARCHAR(512) NOT NULL,
    role_id       INT NOT NULL REFERENCES roles(id),
    is_active     BOOLEAN NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_local_users_username UNIQUE (username)
);

CREATE INDEX IF NOT EXISTS ix_local_users_username ON local_users (LOWER(username));

INSERT INTO app_settings (id, domain, ldap_path, ad_service_domain, ad_service_username, ad_service_password)
VALUES (1, 'GESCONS', 'LDAP://OU=Accounts,DC=GESCONS,DC=RU', 'GESCONS', 'access', '')
ON CONFLICT (id) DO NOTHING;

-- Default local admin: admin / Admin123!
INSERT INTO local_users (username, password_hash, role_id)
SELECT 'admin', 'pbkdf2-sha256|100000|++rusBqfjdGAVaOuN+Etsg==|uY3we5EX9MUjuh+sVpkOfvH9EJXCdJqdcdNzVKqSm0M=', r.id
FROM roles r WHERE r.code = 'super_admin'
ON CONFLICT (username) DO NOTHING;
