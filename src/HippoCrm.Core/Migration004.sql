-- Preserve existing assignments while introducing multiple organizations and contact tags.
ALTER TABLE contacts ADD COLUMN tags TEXT NOT NULL DEFAULT '';
CREATE TABLE contact_organizations(
 contact_id INTEGER NOT NULL REFERENCES contacts(id) ON DELETE CASCADE,
 organization_id INTEGER NOT NULL REFERENCES organizations(id),
 PRIMARY KEY(contact_id,organization_id));
CREATE INDEX idx_contact_organizations_organization ON contact_organizations(organization_id);
INSERT INTO contact_organizations(contact_id,organization_id)
 SELECT id,organization_id FROM contacts WHERE organization_id IS NOT NULL;
UPDATE schema_version SET version=4 WHERE version=3;
