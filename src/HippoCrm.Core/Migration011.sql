ALTER TABLE contacts ADD COLUMN social_media TEXT NOT NULL DEFAULT '';
UPDATE schema_version SET version=11 WHERE version=10;
