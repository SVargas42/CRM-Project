-- Additive migration: no records or grants are removed and no user gains CUI access.
ALTER TABLE users ADD COLUMN cui_authorized INTEGER NOT NULL DEFAULT 0 CHECK(cui_authorized IN (0,1));
ALTER TABLE contacts ADD COLUMN classification TEXT NOT NULL DEFAULT 'CUI' CHECK(classification='CUI');
ALTER TABLE organizations ADD COLUMN classification TEXT NOT NULL DEFAULT 'CUI' CHECK(classification='CUI');
-- Historical archive flags never implied that Retired/Deceased records became Active.
UPDATE contacts SET status='Inactive',revision=revision+1 WHERE archived=1 AND status='Active';
UPDATE organizations SET status='Inactive',revision=revision+1 WHERE archived=1 AND status='Active';
