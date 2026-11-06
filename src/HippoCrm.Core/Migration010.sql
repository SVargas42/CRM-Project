-- Word tables must never be read directly for pickers or reports; join through authorized parent records.
CREATE TABLE IF NOT EXISTS skills(
 id INTEGER PRIMARY KEY,
 name TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK(length(trim(name)) BETWEEN 1 AND 64 AND instr(name,',')=0 AND instr(name,char(0))=0 AND instr(name,char(1))=0 AND instr(name,char(2))=0 AND instr(name,char(3))=0 AND instr(name,char(4))=0 AND instr(name,char(5))=0 AND instr(name,char(6))=0 AND instr(name,char(7))=0 AND instr(name,char(8))=0 AND instr(name,char(9))=0 AND instr(name,char(10))=0 AND instr(name,char(11))=0 AND instr(name,char(12))=0 AND instr(name,char(13))=0 AND instr(name,char(14))=0 AND instr(name,char(15))=0 AND instr(name,char(16))=0 AND instr(name,char(17))=0 AND instr(name,char(18))=0 AND instr(name,char(19))=0 AND instr(name,char(20))=0 AND instr(name,char(21))=0 AND instr(name,char(22))=0 AND instr(name,char(23))=0 AND instr(name,char(24))=0 AND instr(name,char(25))=0 AND instr(name,char(26))=0 AND instr(name,char(27))=0 AND instr(name,char(28))=0 AND instr(name,char(29))=0 AND instr(name,char(30))=0 AND instr(name,char(31))=0 AND instr(name,char(127))=0 AND instr(name,char(128))=0 AND instr(name,char(129))=0 AND instr(name,char(130))=0 AND instr(name,char(131))=0 AND instr(name,char(132))=0 AND instr(name,char(133))=0 AND instr(name,char(134))=0 AND instr(name,char(135))=0 AND instr(name,char(136))=0 AND instr(name,char(137))=0 AND instr(name,char(138))=0 AND instr(name,char(139))=0 AND instr(name,char(140))=0 AND instr(name,char(141))=0 AND instr(name,char(142))=0 AND instr(name,char(143))=0 AND instr(name,char(144))=0 AND instr(name,char(145))=0 AND instr(name,char(146))=0 AND instr(name,char(147))=0 AND instr(name,char(148))=0 AND instr(name,char(149))=0 AND instr(name,char(150))=0 AND instr(name,char(151))=0 AND instr(name,char(152))=0 AND instr(name,char(153))=0 AND instr(name,char(154))=0 AND instr(name,char(155))=0 AND instr(name,char(156))=0 AND instr(name,char(157))=0 AND instr(name,char(158))=0 AND instr(name,char(159))=0),
 status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active','Inactive','Retired','Deceased')));
CREATE TABLE IF NOT EXISTS tags(
 id INTEGER PRIMARY KEY,
 name TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK(length(trim(name)) BETWEEN 1 AND 64 AND instr(name,',')=0 AND instr(name,char(0))=0 AND instr(name,char(1))=0 AND instr(name,char(2))=0 AND instr(name,char(3))=0 AND instr(name,char(4))=0 AND instr(name,char(5))=0 AND instr(name,char(6))=0 AND instr(name,char(7))=0 AND instr(name,char(8))=0 AND instr(name,char(9))=0 AND instr(name,char(10))=0 AND instr(name,char(11))=0 AND instr(name,char(12))=0 AND instr(name,char(13))=0 AND instr(name,char(14))=0 AND instr(name,char(15))=0 AND instr(name,char(16))=0 AND instr(name,char(17))=0 AND instr(name,char(18))=0 AND instr(name,char(19))=0 AND instr(name,char(20))=0 AND instr(name,char(21))=0 AND instr(name,char(22))=0 AND instr(name,char(23))=0 AND instr(name,char(24))=0 AND instr(name,char(25))=0 AND instr(name,char(26))=0 AND instr(name,char(27))=0 AND instr(name,char(28))=0 AND instr(name,char(29))=0 AND instr(name,char(30))=0 AND instr(name,char(31))=0 AND instr(name,char(127))=0 AND instr(name,char(128))=0 AND instr(name,char(129))=0 AND instr(name,char(130))=0 AND instr(name,char(131))=0 AND instr(name,char(132))=0 AND instr(name,char(133))=0 AND instr(name,char(134))=0 AND instr(name,char(135))=0 AND instr(name,char(136))=0 AND instr(name,char(137))=0 AND instr(name,char(138))=0 AND instr(name,char(139))=0 AND instr(name,char(140))=0 AND instr(name,char(141))=0 AND instr(name,char(142))=0 AND instr(name,char(143))=0 AND instr(name,char(144))=0 AND instr(name,char(145))=0 AND instr(name,char(146))=0 AND instr(name,char(147))=0 AND instr(name,char(148))=0 AND instr(name,char(149))=0 AND instr(name,char(150))=0 AND instr(name,char(151))=0 AND instr(name,char(152))=0 AND instr(name,char(153))=0 AND instr(name,char(154))=0 AND instr(name,char(155))=0 AND instr(name,char(156))=0 AND instr(name,char(157))=0 AND instr(name,char(158))=0 AND instr(name,char(159))=0),
 status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active','Inactive','Retired','Deceased')));
CREATE TABLE IF NOT EXISTS contact_skills(
 contact_id INTEGER NOT NULL REFERENCES contacts(id),
 skill_id INTEGER NOT NULL REFERENCES skills(id),
 status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active','Inactive','Retired','Deceased')),
 PRIMARY KEY(contact_id,skill_id));
CREATE INDEX IF NOT EXISTS idx_contact_skills_skill_id ON contact_skills(skill_id);
CREATE TABLE IF NOT EXISTS contact_tags(
 contact_id INTEGER NOT NULL REFERENCES contacts(id),
 tag_id INTEGER NOT NULL REFERENCES tags(id),
 status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active','Inactive','Retired','Deceased')),
 PRIMARY KEY(contact_id,tag_id));
CREATE INDEX IF NOT EXISTS idx_contact_tags_tag_id ON contact_tags(tag_id);
CREATE TABLE IF NOT EXISTS organization_tags(
 organization_id INTEGER NOT NULL REFERENCES organizations(id),
 tag_id INTEGER NOT NULL REFERENCES tags(id),
 status TEXT NOT NULL DEFAULT 'Active' CHECK(status IN ('Active','Inactive','Retired','Deceased')),
 PRIMARY KEY(organization_id,tag_id));
CREATE INDEX IF NOT EXISTS idx_organization_tags_tag_id ON organization_tags(tag_id);
