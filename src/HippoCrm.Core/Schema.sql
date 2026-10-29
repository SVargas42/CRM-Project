-- SQLCipher keying and cipher_version validation MUST precede this embedded script.
-- Encryption is a connection/file-format property, not a CREATE TABLE option.
-- Database initializes this schema inside one transaction; no key belongs in SQL.
CREATE TABLE IF NOT EXISTS schema_version(version INTEGER PRIMARY KEY);
INSERT OR IGNORE INTO schema_version VALUES(2);
CREATE TABLE IF NOT EXISTS users(
 id INTEGER PRIMARY KEY, username TEXT NOT NULL UNIQUE COLLATE NOCASE,
 salt BLOB NOT NULL, password_hash BLOB NOT NULL, iterations INTEGER NOT NULL,
 failed_attempts INTEGER NOT NULL DEFAULT 0, locked_until TEXT,
 created_at TEXT NOT NULL,
 role TEXT NOT NULL DEFAULT 'Viewer' CHECK(role IN ('Administrator','Editor','Viewer')),
 is_enabled INTEGER NOT NULL DEFAULT 1 CHECK(is_enabled IN (0,1)));
CREATE TABLE IF NOT EXISTS organizations(
 id INTEGER PRIMARY KEY, name TEXT NOT NULL CHECK(length(trim(name))>0),
 email TEXT NOT NULL, phone TEXT NOT NULL, address TEXT NOT NULL,
 services TEXT NOT NULL, tags TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status IN ('Active','Inactive','Retired','Deceased')),
 archived INTEGER NOT NULL DEFAULT 0 CHECK(archived IN (0,1)),
 created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS contacts(
 id INTEGER PRIMARY KEY, first_name TEXT NOT NULL, last_name TEXT NOT NULL,
 email TEXT NOT NULL, phone TEXT NOT NULL, address TEXT NOT NULL, skills TEXT NOT NULL,
 spouse TEXT NOT NULL, children TEXT NOT NULL, family TEXT NOT NULL, friends TEXT NOT NULL,
 twitter TEXT NOT NULL, instagram TEXT NOT NULL, snapchat TEXT NOT NULL, facebook TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status IN ('Active','Inactive','Retired','Deceased')),
 availability TEXT NOT NULL CHECK(availability IN ('Available','Unavailable','Unknown')),
 archived INTEGER NOT NULL DEFAULT 0 CHECK(archived IN (0,1)),
 created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
 CHECK(length(trim(first_name || last_name))>0));
CREATE TABLE IF NOT EXISTS interactions(
 id INTEGER PRIMARY KEY, contact_id INTEGER NOT NULL REFERENCES contacts(id),
 body TEXT NOT NULL CHECK(length(trim(body))>0), created_at TEXT NOT NULL,
 user_id INTEGER NOT NULL REFERENCES users(id));
CREATE TABLE IF NOT EXISTS employment(
 id INTEGER PRIMARY KEY, contact_id INTEGER NOT NULL REFERENCES contacts(id),
 organization_id INTEGER NOT NULL REFERENCES organizations(id), role TEXT NOT NULL,
 start_date TEXT NOT NULL, end_date TEXT, CHECK(end_date IS NULL OR end_date>=start_date));
CREATE UNIQUE INDEX IF NOT EXISTS one_current_employer ON employment(contact_id) WHERE end_date IS NULL;
CREATE TABLE IF NOT EXISTS contracts(
 id INTEGER PRIMARY KEY, contact_id INTEGER NOT NULL REFERENCES contacts(id),
 contract_date TEXT NOT NULL, value_cents INTEGER NOT NULL CHECK(value_cents>=0),
 location TEXT NOT NULL, description TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS sessions(
 token_hash BLOB PRIMARY KEY NOT NULL CHECK(length(token_hash)=32),
 user_id INTEGER NOT NULL REFERENCES users(id), created_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS contact_access(
 user_id INTEGER NOT NULL REFERENCES users(id), contact_id INTEGER NOT NULL REFERENCES contacts(id),
 can_write INTEGER NOT NULL CHECK(can_write IN (0,1)), PRIMARY KEY(user_id,contact_id));
CREATE TABLE IF NOT EXISTS organization_access(
 user_id INTEGER NOT NULL REFERENCES users(id), organization_id INTEGER NOT NULL REFERENCES organizations(id),
 can_write INTEGER NOT NULL CHECK(can_write IN (0,1)), PRIMARY KEY(user_id,organization_id));
CREATE TABLE IF NOT EXISTS audit_log(
 id INTEGER PRIMARY KEY, user_id INTEGER REFERENCES users(id), subject_user_id INTEGER REFERENCES users(id),
 entity TEXT NOT NULL, entity_id INTEGER NOT NULL, action TEXT NOT NULL, created_at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS contacts_name ON contacts(last_name,first_name);
CREATE INDEX IF NOT EXISTS interactions_contact_date ON interactions(contact_id,created_at);
CREATE INDEX IF NOT EXISTS contracts_contact_date ON contracts(contact_id,contract_date);
CREATE TRIGGER IF NOT EXISTS protect_users BEFORE DELETE ON users BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
CREATE TRIGGER IF NOT EXISTS protect_organizations BEFORE DELETE ON organizations BEGIN SELECT RAISE(ABORT,'Use archiving'); END;
CREATE TRIGGER IF NOT EXISTS protect_contacts BEFORE DELETE ON contacts BEGIN SELECT RAISE(ABORT,'Use archiving'); END;
CREATE TRIGGER IF NOT EXISTS protect_interactions BEFORE DELETE ON interactions BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
CREATE TRIGGER IF NOT EXISTS immutable_interactions BEFORE UPDATE ON interactions BEGIN SELECT RAISE(ABORT,'Interaction notes are immutable'); END;
CREATE TRIGGER IF NOT EXISTS protect_employment BEFORE DELETE ON employment BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
CREATE TRIGGER IF NOT EXISTS protect_contracts BEFORE DELETE ON contracts BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
CREATE TRIGGER IF NOT EXISTS protect_audit BEFORE DELETE ON audit_log BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
CREATE TRIGGER IF NOT EXISTS immutable_audit BEFORE UPDATE ON audit_log BEGIN SELECT RAISE(ABORT,'Audit entries are immutable'); END;
