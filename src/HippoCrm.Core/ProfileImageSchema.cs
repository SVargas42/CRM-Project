using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

// The feature upgrade is transactional and additive. Keep older image BLOBs as
// retained evidence; never drop a business table or delete a retained version.
internal static class ProfileImageSchema
{
    internal static void Prepare(SqliteConnection dbConnection, SqliteTransaction txSchema)
    {
        using var cmdLegacy = Database.Command(dbConnection, "SELECT count(*) FROM pragma_table_info('profile_images') WHERE name='image_data'");
        cmdLegacy.Transaction = txSchema;
        if (Convert.ToInt64(cmdLegacy.ExecuteScalar()) == 0) return;
        Execute(dbConnection, txSchema, """
            ALTER TABLE profile_images RENAME TO legacy_profile_images_v5;
            DROP TRIGGER IF EXISTS protect_profile_images;
            CREATE TRIGGER protect_legacy_profile_images_v5 BEFORE DELETE ON legacy_profile_images_v5 BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
            CREATE TRIGGER immutable_legacy_profile_images_v5 BEFORE UPDATE ON legacy_profile_images_v5 BEGIN SELECT RAISE(ABORT,'Image versions are immutable'); END;
            """);
    }

    internal static void Initialize(SqliteConnection dbConnection, SqliteTransaction txSchema)
    {
        using var cmdLegacy = Database.Command(dbConnection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='legacy_profile_images_v5'");
        cmdLegacy.Transaction = txSchema;
        if (Convert.ToInt64(cmdLegacy.ExecuteScalar()) != 0)
        {
            // Copy only once, using retained IDs. Any invalid parent aborts the
            // entire upgrade instead of silently omitting an old photograph.
            Execute(dbConnection, txSchema, """
                INSERT INTO profile_images(id,entity,entity_id,png,status,created_at,user_id)
                SELECT l.id,CASE WHEN l.contact_id IS NOT NULL THEN 'Contact' ELSE 'Organization' END,
                    COALESCE(l.contact_id,l.organization_id),l.image_data,
                    CASE WHEN l.id=(SELECT MAX(x.id) FROM legacy_profile_images_v5 x
                        WHERE x.contact_id IS l.contact_id AND x.organization_id IS l.organization_id) THEN 'Active' ELSE 'Inactive' END,
                    l.created_at,l.user_id FROM legacy_profile_images_v5 l
                    WHERE NOT EXISTS(SELECT 1 FROM profile_images p WHERE p.id=l.id);
                """);
        }
        Execute(dbConnection, txSchema, """
            CREATE TRIGGER IF NOT EXISTS validate_image_parent BEFORE INSERT ON profile_images
            WHEN NOT ((NEW.entity='Contact' AND EXISTS(SELECT 1 FROM contacts WHERE id=NEW.entity_id AND classification='CUI' AND archived=0))
                OR (NEW.entity='Organization' AND EXISTS(SELECT 1 FROM organizations WHERE id=NEW.entity_id AND classification='CUI' AND archived=0)))
            BEGIN SELECT RAISE(ABORT,'Invalid image owner'); END;
            CREATE TRIGGER IF NOT EXISTS immutable_contact_record_id BEFORE UPDATE OF id ON contacts
            WHEN NEW.id IS NOT OLD.id BEGIN SELECT RAISE(ABORT,'Record IDs are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS immutable_organization_record_id BEFORE UPDATE OF id ON organizations
            WHEN NEW.id IS NOT OLD.id BEGIN SELECT RAISE(ABORT,'Record IDs are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS immutable_image_version BEFORE UPDATE ON profile_images
            WHEN NEW.id IS NOT OLD.id OR NEW.entity IS NOT OLD.entity OR NEW.entity_id IS NOT OLD.entity_id
                OR NEW.png IS NOT OLD.png OR NEW.created_at IS NOT OLD.created_at OR NEW.user_id IS NOT OLD.user_id
                OR NOT (NEW.status=OLD.status OR (OLD.status='Active' AND NEW.status='Inactive'))
            BEGIN SELECT RAISE(ABORT,'Image versions are immutable'); END;
            CREATE TABLE IF NOT EXISTS record_links(
                id INTEGER PRIMARY KEY, entity TEXT NOT NULL CHECK(entity IN ('Contact','Organization')),
                source_id INTEGER NOT NULL, target_id INTEGER NOT NULL CHECK(source_id<target_id),
                created_at TEXT NOT NULL, user_id INTEGER NOT NULL REFERENCES users(id),
                UNIQUE(entity,source_id,target_id));
            CREATE TRIGGER IF NOT EXISTS validate_record_link BEFORE INSERT ON record_links
            WHEN NOT ((NEW.entity='Contact' AND (SELECT count(*) FROM contacts WHERE id IN (NEW.source_id,NEW.target_id) AND classification='CUI')=2)
                OR (NEW.entity='Organization' AND (SELECT count(*) FROM organizations WHERE id IN (NEW.source_id,NEW.target_id) AND classification='CUI')=2))
            BEGIN SELECT RAISE(ABORT,'Invalid record link'); END;
            CREATE TRIGGER IF NOT EXISTS immutable_record_link BEFORE UPDATE ON record_links BEGIN SELECT RAISE(ABORT,'Links are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS retain_record_link BEFORE DELETE ON record_links BEGIN SELECT RAISE(ABORT,'Deletion is prohibited'); END;
            """);
        using var cmdParents = Database.Command(dbConnection, """
            SELECT count(*) FROM profile_images p WHERE NOT
                ((p.entity='Contact' AND EXISTS(SELECT 1 FROM contacts WHERE id=p.entity_id AND classification='CUI'))
                OR (p.entity='Organization' AND EXISTS(SELECT 1 FROM organizations WHERE id=p.entity_id AND classification='CUI')))
            """);
        cmdParents.Transaction = txSchema;
        if (Convert.ToInt64(cmdParents.ExecuteScalar()) != 0) throw new InvalidOperationException("Image history requires administrator review.");
    }

    private static void Execute(SqliteConnection dbConnection, SqliteTransaction txSchema, string strSql)
    { using var cmdSchema = Database.Command(dbConnection, strSql); cmdSchema.Transaction = txSchema; cmdSchema.ExecuteNonQuery(); }
}
