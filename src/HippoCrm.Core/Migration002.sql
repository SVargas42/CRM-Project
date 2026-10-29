-- Database preflight permits at most one legacy user: the documented bootstrap administrator.
ALTER TABLE users ADD COLUMN role TEXT NOT NULL DEFAULT 'Viewer' CHECK(role IN ('Administrator','Editor','Viewer'));
ALTER TABLE users ADD COLUMN is_enabled INTEGER NOT NULL DEFAULT 1 CHECK(is_enabled IN (0,1));
UPDATE users SET role='Administrator';
-- SQLite cannot remove a NOT NULL constraint in place. Preserve every old event in one transaction.
DROP TRIGGER protect_audit;
DROP TRIGGER immutable_audit;
ALTER TABLE audit_log RENAME TO audit_log_v1;
CREATE TABLE audit_log(
 id INTEGER PRIMARY KEY, user_id INTEGER REFERENCES users(id), subject_user_id INTEGER REFERENCES users(id),
 entity TEXT NOT NULL, entity_id INTEGER NOT NULL, action TEXT NOT NULL, created_at TEXT NOT NULL);
INSERT INTO audit_log(id,user_id,entity,entity_id,action,created_at)
 SELECT id,user_id,entity,entity_id,action,created_at FROM audit_log_v1;
DROP TABLE audit_log_v1;
UPDATE schema_version SET version=2 WHERE version=1;
