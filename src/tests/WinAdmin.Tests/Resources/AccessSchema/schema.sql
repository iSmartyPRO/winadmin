-- AD Manager: PostgreSQL schema
-- Run: psql -h HOST -U access -d access -f install/schema.sql

CREATE TABLE IF NOT EXISTS roles (
    id          SERIAL PRIMARY KEY,
    code        VARCHAR(50) NOT NULL UNIQUE,
    name        VARCHAR(100) NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS portal_users (
    id               SERIAL PRIMARY KEY,
    sam_account_name VARCHAR(256) NOT NULL,
    role_id          INT NOT NULL REFERENCES roles(id),
    is_active        BOOLEAN NOT NULL DEFAULT TRUE,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by       VARCHAR(256),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_portal_users_sam UNIQUE (sam_account_name)
);

CREATE INDEX IF NOT EXISTS ix_portal_users_sam ON portal_users (LOWER(sam_account_name));

CREATE TABLE IF NOT EXISTS excluded_ous (
    id         SERIAL PRIMARY KEY,
    ou_name    VARCHAR(256) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_excluded_ous_name UNIQUE (ou_name)
);

CREATE TABLE IF NOT EXISTS terminated_user_ous (
    id                      SERIAL PRIMARY KEY,
    ou_name                 VARCHAR(256) NOT NULL,
    ou_dn                   VARCHAR(1024),
    display_name            VARCHAR(256),
    is_deactivation_default BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_terminated_user_ous_name UNIQUE (ou_name)
);

CREATE TABLE IF NOT EXISTS audit_log (
    id          BIGSERIAL PRIMARY KEY,
    actor_sam   VARCHAR(256) NOT NULL,
    action      VARCHAR(100) NOT NULL,
    target_type VARCHAR(50),
    target_id   VARCHAR(512),
    details     JSONB,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_audit_log_created_at ON audit_log (created_at DESC);

CREATE TABLE IF NOT EXISTS app_settings (
    id                   INT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    domain               VARCHAR(256) NOT NULL DEFAULT '',
    ldap_path            VARCHAR(512) NOT NULL DEFAULT '',
    ad_service_domain    VARCHAR(256) NOT NULL DEFAULT '',
    ad_service_username  VARCHAR(256) NOT NULL DEFAULT '',
    ad_service_password  VARCHAR(512) NOT NULL DEFAULT '',
    yandex_maps_api_key  VARCHAR(256) NOT NULL DEFAULT '',
    exchange_url         VARCHAR(512) NOT NULL DEFAULT '',
    fired_users_group    VARCHAR(512) NOT NULL DEFAULT '',
    api_settings         JSONB NOT NULL DEFAULT '{}'::jsonb,
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
