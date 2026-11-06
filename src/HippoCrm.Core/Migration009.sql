-- Additive policy migration. Existing CUI approvals acquire Basic CUI only.
CREATE TABLE cui_policy_access(
 user_id INTEGER NOT NULL REFERENCES users(id),
 cui_category TEXT NOT NULL,
 distribution_statement TEXT NOT NULL,
 approval_reference TEXT NOT NULL CHECK(length(trim(approval_reference)) BETWEEN 1 AND 256),
 status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active','Inactive','Retired','Deceased')),
 PRIMARY KEY(user_id,cui_category,distribution_statement),
 CHECK((cui_category='CUI' AND distribution_statement='') OR
       (cui_category='CUI//SP-CTI' AND distribution_statement IN ('B','C','D','E','F'))));
INSERT INTO cui_policy_access(user_id,cui_category,distribution_statement,approval_reference)
 SELECT id,'CUI','','Migrated existing Basic CUI approval' FROM users WHERE cui_authorized=1;
CREATE TRIGGER protect_cui_policy_access BEFORE DELETE ON cui_policy_access
 BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
CREATE TRIGGER validate_contacts_cui_insert BEFORE INSERT ON contacts
 WHEN NOT ((NEW.cui_category='CUI' AND NEW.distribution_statement='' AND NEW.controlling_office='' AND NEW.determination_date='' AND NEW.distribution_notice='') OR (NEW.cui_category='CUI//SP-CTI' AND NEW.distribution_statement IN ('B','C','D','E','F') AND length(trim(NEW.controlling_office)) BETWEEN 1 AND 256 AND length(NEW.determination_date)=10 AND NEW.determination_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND date(NEW.determination_date) IS NOT NULL AND date(NEW.determination_date,'+0 days')=NEW.determination_date AND length(NEW.distribution_notice) BETWEEN 26 AND 4096 AND substr(NEW.distribution_notice,1,25)='Distribution Statement ' || NEW.distribution_statement || '.'))
 BEGIN SELECT RAISE(ABORT,'Invalid CUI designation'); END;
CREATE TRIGGER validate_contacts_cui_update BEFORE UPDATE ON contacts
 WHEN NOT ((NEW.cui_category='CUI' AND NEW.distribution_statement='' AND NEW.controlling_office='' AND NEW.determination_date='' AND NEW.distribution_notice='') OR (NEW.cui_category='CUI//SP-CTI' AND NEW.distribution_statement IN ('B','C','D','E','F') AND length(trim(NEW.controlling_office)) BETWEEN 1 AND 256 AND length(NEW.determination_date)=10 AND NEW.determination_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND date(NEW.determination_date) IS NOT NULL AND date(NEW.determination_date,'+0 days')=NEW.determination_date AND length(NEW.distribution_notice) BETWEEN 26 AND 4096 AND substr(NEW.distribution_notice,1,25)='Distribution Statement ' || NEW.distribution_statement || '.'))
 BEGIN SELECT RAISE(ABORT,'Invalid CUI designation'); END;
CREATE TRIGGER validate_organizations_cui_insert BEFORE INSERT ON organizations
 WHEN NOT ((NEW.cui_category='CUI' AND NEW.distribution_statement='' AND NEW.controlling_office='' AND NEW.determination_date='' AND NEW.distribution_notice='') OR (NEW.cui_category='CUI//SP-CTI' AND NEW.distribution_statement IN ('B','C','D','E','F') AND length(trim(NEW.controlling_office)) BETWEEN 1 AND 256 AND length(NEW.determination_date)=10 AND NEW.determination_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND date(NEW.determination_date) IS NOT NULL AND date(NEW.determination_date,'+0 days')=NEW.determination_date AND length(NEW.distribution_notice) BETWEEN 26 AND 4096 AND substr(NEW.distribution_notice,1,25)='Distribution Statement ' || NEW.distribution_statement || '.'))
 BEGIN SELECT RAISE(ABORT,'Invalid CUI designation'); END;
CREATE TRIGGER validate_organizations_cui_update BEFORE UPDATE ON organizations
 WHEN NOT ((NEW.cui_category='CUI' AND NEW.distribution_statement='' AND NEW.controlling_office='' AND NEW.determination_date='' AND NEW.distribution_notice='') OR (NEW.cui_category='CUI//SP-CTI' AND NEW.distribution_statement IN ('B','C','D','E','F') AND length(trim(NEW.controlling_office)) BETWEEN 1 AND 256 AND length(NEW.determination_date)=10 AND NEW.determination_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND date(NEW.determination_date) IS NOT NULL AND date(NEW.determination_date,'+0 days')=NEW.determination_date AND length(NEW.distribution_notice) BETWEEN 26 AND 4096 AND substr(NEW.distribution_notice,1,25)='Distribution Statement ' || NEW.distribution_statement || '.'))
 BEGIN SELECT RAISE(ABORT,'Invalid CUI designation'); END;
CREATE TRIGGER validate_profile_images_cui_insert BEFORE INSERT ON profile_images
 WHEN NOT ((NEW.cui_category='CUI' AND NEW.distribution_statement='' AND NEW.controlling_office='' AND NEW.determination_date='' AND NEW.distribution_notice='') OR (NEW.cui_category='CUI//SP-CTI' AND NEW.distribution_statement IN ('B','C','D','E','F') AND length(trim(NEW.controlling_office)) BETWEEN 1 AND 256 AND length(NEW.determination_date)=10 AND NEW.determination_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND date(NEW.determination_date) IS NOT NULL AND date(NEW.determination_date,'+0 days')=NEW.determination_date AND length(NEW.distribution_notice) BETWEEN 26 AND 4096 AND substr(NEW.distribution_notice,1,25)='Distribution Statement ' || NEW.distribution_statement || '.'))
 BEGIN SELECT RAISE(ABORT,'Invalid CUI designation'); END;
CREATE TRIGGER validate_profile_images_cui_update BEFORE UPDATE ON profile_images
 WHEN NOT ((NEW.cui_category='CUI' AND NEW.distribution_statement='' AND NEW.controlling_office='' AND NEW.determination_date='' AND NEW.distribution_notice='') OR (NEW.cui_category='CUI//SP-CTI' AND NEW.distribution_statement IN ('B','C','D','E','F') AND length(trim(NEW.controlling_office)) BETWEEN 1 AND 256 AND length(NEW.determination_date)=10 AND NEW.determination_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND date(NEW.determination_date) IS NOT NULL AND date(NEW.determination_date,'+0 days')=NEW.determination_date AND length(NEW.distribution_notice) BETWEEN 26 AND 4096 AND substr(NEW.distribution_notice,1,25)='Distribution Statement ' || NEW.distribution_statement || '.'))
 BEGIN SELECT RAISE(ABORT,'Invalid CUI designation'); END;
CREATE TRIGGER inherit_image_contact_insert BEFORE INSERT ON profile_images
 WHEN NEW.entity='Contact' AND NOT EXISTS(SELECT 1 FROM contacts p WHERE p.id=NEW.entity_id AND p.cui_category=NEW.cui_category AND p.distribution_statement=NEW.distribution_statement AND p.controlling_office=NEW.controlling_office AND p.determination_date=NEW.determination_date AND p.distribution_notice=NEW.distribution_notice)
 BEGIN SELECT RAISE(ABORT,'Image designation must match its parent'); END;
CREATE TRIGGER inherit_image_contact_update BEFORE UPDATE ON profile_images
 WHEN NEW.entity='Contact' AND NOT EXISTS(SELECT 1 FROM contacts p WHERE p.id=NEW.entity_id AND p.cui_category=NEW.cui_category AND p.distribution_statement=NEW.distribution_statement AND p.controlling_office=NEW.controlling_office AND p.determination_date=NEW.determination_date AND p.distribution_notice=NEW.distribution_notice)
 BEGIN SELECT RAISE(ABORT,'Image designation must match its parent'); END;
CREATE TRIGGER propagate_contacts_cui AFTER UPDATE OF cui_category,distribution_statement,controlling_office,determination_date,distribution_notice ON contacts
 BEGIN UPDATE profile_images SET cui_category=NEW.cui_category,distribution_statement=NEW.distribution_statement,controlling_office=NEW.controlling_office,determination_date=NEW.determination_date,distribution_notice=NEW.distribution_notice WHERE entity='Contact' AND entity_id=NEW.id; END;
UPDATE profile_images SET cui_category=(SELECT p.cui_category FROM contacts p WHERE p.id=profile_images.entity_id),distribution_statement=(SELECT p.distribution_statement FROM contacts p WHERE p.id=profile_images.entity_id),controlling_office=(SELECT p.controlling_office FROM contacts p WHERE p.id=profile_images.entity_id),determination_date=(SELECT p.determination_date FROM contacts p WHERE p.id=profile_images.entity_id),distribution_notice=(SELECT p.distribution_notice FROM contacts p WHERE p.id=profile_images.entity_id) WHERE entity='Contact';
CREATE TRIGGER inherit_image_organization_insert BEFORE INSERT ON profile_images
 WHEN NEW.entity='Organization' AND NOT EXISTS(SELECT 1 FROM organizations p WHERE p.id=NEW.entity_id AND p.cui_category=NEW.cui_category AND p.distribution_statement=NEW.distribution_statement AND p.controlling_office=NEW.controlling_office AND p.determination_date=NEW.determination_date AND p.distribution_notice=NEW.distribution_notice)
 BEGIN SELECT RAISE(ABORT,'Image designation must match its parent'); END;
CREATE TRIGGER inherit_image_organization_update BEFORE UPDATE ON profile_images
 WHEN NEW.entity='Organization' AND NOT EXISTS(SELECT 1 FROM organizations p WHERE p.id=NEW.entity_id AND p.cui_category=NEW.cui_category AND p.distribution_statement=NEW.distribution_statement AND p.controlling_office=NEW.controlling_office AND p.determination_date=NEW.determination_date AND p.distribution_notice=NEW.distribution_notice)
 BEGIN SELECT RAISE(ABORT,'Image designation must match its parent'); END;
CREATE TRIGGER propagate_organizations_cui AFTER UPDATE OF cui_category,distribution_statement,controlling_office,determination_date,distribution_notice ON organizations
 BEGIN UPDATE profile_images SET cui_category=NEW.cui_category,distribution_statement=NEW.distribution_statement,controlling_office=NEW.controlling_office,determination_date=NEW.determination_date,distribution_notice=NEW.distribution_notice WHERE entity='Organization' AND entity_id=NEW.id; END;
UPDATE profile_images SET cui_category=(SELECT p.cui_category FROM organizations p WHERE p.id=profile_images.entity_id),distribution_statement=(SELECT p.distribution_statement FROM organizations p WHERE p.id=profile_images.entity_id),controlling_office=(SELECT p.controlling_office FROM organizations p WHERE p.id=profile_images.entity_id),determination_date=(SELECT p.determination_date FROM organizations p WHERE p.id=profile_images.entity_id),distribution_notice=(SELECT p.distribution_notice FROM organizations p WHERE p.id=profile_images.entity_id) WHERE entity='Organization';
