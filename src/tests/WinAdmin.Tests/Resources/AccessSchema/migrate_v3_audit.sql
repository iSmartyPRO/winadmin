-- AD Manager: audit_log indexes for filtered journal queries
-- Run: psql -h HOST -U access -d access -f install/migrate_v3_audit.sql

CREATE INDEX IF NOT EXISTS ix_audit_log_actor ON audit_log (LOWER(actor_sam));
CREATE INDEX IF NOT EXISTS ix_audit_log_action ON audit_log (action);
CREATE INDEX IF NOT EXISTS ix_audit_log_target ON audit_log (target_type, target_id);
CREATE INDEX IF NOT EXISTS ix_audit_log_details_gin ON audit_log USING gin (details);
