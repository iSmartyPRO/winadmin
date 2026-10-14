-- AD Manager: DN для OU уволенных (поиск вне ldap_path)
-- Run: psql -h HOST -U access -d access -f install/migrate_v9_terminated_user_ou_dn.sql

ALTER TABLE terminated_user_ous
    ADD COLUMN IF NOT EXISTS ou_dn VARCHAR(1024);
