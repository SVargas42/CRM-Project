ALTER TABLE contacts ADD COLUMN middle_names TEXT NOT NULL DEFAULT '';
ALTER TABLE contacts ADD COLUMN prefix TEXT NOT NULL DEFAULT '';
ALTER TABLE contacts ADD COLUMN suffix TEXT NOT NULL DEFAULT '';
UPDATE schema_version SET version=6 WHERE version=5;
