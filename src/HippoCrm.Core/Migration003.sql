-- Relationship only. Existing contacts remain unassigned; no image metadata is stored.
ALTER TABLE contacts ADD COLUMN organization_id INTEGER REFERENCES organizations(id);
UPDATE schema_version SET version=3 WHERE version=2;
