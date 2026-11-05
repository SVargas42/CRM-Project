using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class MacosMergeChecks
{
    internal static void Run()
    {
        NameLookupChecks(); HeadSevenUpgradeChecks(); MacSixUpgradeChecks();
    }
    private static void NameLookupChecks()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL merged name policy!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("merged-names", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objContact = new Contact { Prefix = "Dr", FirstName = "Ada", MiddleNames = "Augusta\r\nByron\r\n", LastName = "Lovelace", Suffix = "PhD" };
        objCrm.Save(objContact);
        Check(objCrm.Contacts("Augusta Byron").Single().Id == objContact.Id, "Merged lookup normalizes Windows/macOS middle-name line endings");
        Check(objCrm.Contacts("Dr Ada Augusta Byron Lovelace PhD").Single().Id == objContact.Id, "Merged lookup includes prefix, multiple middle names and suffix");
        Check(objCrm.Contacts("Ada Lovelace").Single().Id == objContact.Id, "Original first/last-name lookup remains compatible with expanded names");
        var objSimple = new Contact { FirstName = "Grace", LastName = "Hopper", Suffix = "Admiral" }; objCrm.Save(objSimple);
        Check(objCrm.Contacts("Grace Hopper Admiral").Single().Id == objSimple.Id, "Empty optional name parts do not break phrase lookup");
        foreach (var enStatus in Enum.GetValues<RecordStatus>())
            objCrm.Save(new Contact { FirstName = "FICTIONAL", MiddleNames = "MergedSearchToken", LastName = enStatus.ToString(), Status = enStatus });
        foreach (var enStatus in Enum.GetValues<RecordStatus>())
            Check(objCrm.Contacts("MergedSearchToken", enStatus: enStatus).Single().Status == enStatus, "Expanded-name lookup retains the " + enStatus + " predicate");
        var lngViewer = objUsers.CreateUser("merged-viewer", secPassword, UserRole.Viewer);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, true, false);
        using var objViewer = new AuthService(objDatabase).SignIn("merged-viewer", secPassword);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        Check(objViewerCrm.Contacts("Augusta Byron").Count == 0, "Expanded names cannot bypass missing CUI authorization");
        objUsers.SetCuiAuthorization(lngViewer, true);
        Check(objViewerCrm.Contacts("Augusta Byron").Single().Id == objContact.Id && objViewerCrm.Contacts("Grace Hopper Admiral").Count == 0,
            "Expanded names preserve explicit record grants");
        Reject<ArgumentException>(() => objCrm.Save(new Contact { Prefix = "Dr", MiddleNames = "Only", Suffix = "III" }), "Titles alone do not satisfy the first/last-name requirement");
        Reject<ArgumentException>(() => objCrm.Save(new Contact { FirstName = "Invalid", MiddleNames = "Named", strClassification = "PUBLIC" }), "Name-part support cannot downgrade CUI classification");
    }
    private static void HeadSevenUpgradeChecks()
    {
        using var objDatabase = new Database(bVersionSix: true);
        using var secPassword = TestPasswords.Secret("FICTIONAL version seven policy!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("head-seven", secPassword);
        // Use the captured old schema, rather than downgrading a current workspace
        // that already contains newer policy tables and migration guards.
        using var stmMigration = typeof(Database).Assembly.GetManifestResourceStream("HippoCrm.Core.Migration007.sql")!;
        using var rdrMigration = new StreamReader(stmMigration); Sql(objDatabase, rdrMigration.ReadToEnd());
        Sql(objDatabase, "UPDATE schema_version SET version=7; UPDATE users SET cui_authorized=1 WHERE id=1;");
        var arrColumns = new[] { "first_name", "last_name", "email", "phone", "address", "skills", "spouse", "children", "family", "friends", "twitter", "instagram", "snapchat", "facebook" };
        Sql(objDatabase, $"INSERT INTO contacts({string.Join(',', arrColumns)},status,availability,created_at,updated_at) VALUES({string.Join(',', arrColumns.Select(strColumn => strColumn == "first_name" ? "'FICTIONAL retained'" : "''"))},'Retired','Unknown','fixture','fixture')");
        var lngAudit = Scalar(objDatabase, "SELECT count(*) FROM audit_log");
        Sql(objDatabase, "CREATE TRIGGER fixture_deny_name_upgrade BEFORE INSERT ON audit_log WHEN NEW.action='SchemaUpgraded' AND NEW.entity_id=9 BEGIN SELECT RAISE(ABORT,'Fixture migration audit unavailable'); END");
        Reject<SqliteException>(objDatabase.Upgrade, "Name migration fails closed if its signed audit cannot append");
        Check(Scalar(objDatabase, "SELECT version FROM schema_version") == 7 && Scalar(objDatabase, "SELECT count(*) FROM pragma_table_info('contacts') WHERE name='middle_names'") == 0,
            "Failed name migration rolls back columns and schema version together");
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log") == lngAudit && Scalar(objDatabase, "SELECT cui_authorized FROM users WHERE id=1") == 1,
            "Failed migration preserves signed history and existing approved CUI access");
        Sql(objDatabase, "DROP TRIGGER fixture_deny_name_upgrade"); objDatabase.Upgrade();
        var objCrm = new CrmService(objDatabase, objAdmin);
        Check(Scalar(objDatabase, "SELECT version FROM schema_version") == 9 && Scalar(objDatabase, "SELECT count(*) FROM pragma_table_info('contacts') WHERE name IN ('middle_names','prefix','suffix')") == 3,
            "HEAD version 7 gains all macOS name columns additively");
        Check(objCrm.Contacts(enStatus: RecordStatus.Retired).Single().FirstName == "FICTIONAL retained" && Scalar(objDatabase, "SELECT cui_authorized FROM users WHERE id=1") == 1,
            "Version 7 upgrade preserves retained records and authorized users");
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='SchemaUpgraded' AND entity_id=9") == 1, "Combined migration records one signed version-9 upgrade event");
        Check(Scalar(objDatabase, "SELECT count(*) FROM cui_policy_access WHERE cui_category='CUI' AND status='Active'") == 1
            && Scalar(objDatabase, "SELECT count(*) FROM cui_policy_access WHERE cui_category='CUI//SP-CTI'") == 0, "Migration preserves only existing Basic CUI approval and grants no CTI distribution");
        lngAudit = Scalar(objDatabase, "SELECT count(*) FROM audit_log"); objDatabase.Upgrade();
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log") == lngAudit, "Reopening merged version 9 does not repeat schema migration");
    }
    private static void MacSixUpgradeChecks()
    {
        using var objDatabase = new Database(bVersionSix: true);
        using var secPassword = TestPasswords.Secret("FICTIONAL macOS version six!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("mac-six", secPassword);
        Sql(objDatabase, "ALTER TABLE contacts ADD COLUMN middle_names TEXT NOT NULL DEFAULT ''; ALTER TABLE contacts ADD COLUMN prefix TEXT NOT NULL DEFAULT ''; ALTER TABLE contacts ADD COLUMN suffix TEXT NOT NULL DEFAULT '';");
        var arrColumns = new[] { "first_name", "last_name", "email", "phone", "address", "skills", "spouse", "children", "family", "friends", "twitter", "instagram", "snapchat", "facebook" };
        Sql(objDatabase, $"INSERT INTO contacts({string.Join(',', arrColumns)},prefix,middle_names,suffix,status,availability,created_at,updated_at) VALUES({string.Join(',', arrColumns.Select(strColumn => strColumn == "first_name" ? "'FICTIONAL mac'" : "''"))},'Dr','Existing Middle','III','Retired','Unknown','fixture','fixture')");
        objDatabase.Upgrade();
        Check(Scalar(objDatabase, "SELECT version FROM schema_version") == 9 && Scalar(objDatabase, "SELECT count(*) FROM contacts WHERE prefix='Dr' AND middle_names='Existing Middle' AND suffix='III' AND classification='CUI' AND status='Retired'") == 1,
            "Version 6 with macOS fields preserves name values and gains CUI tagging");
        Check(Scalar(objDatabase, "SELECT cui_authorized FROM users WHERE id=1") == 0, "Pre-CUI macOS users receive no implicit CUI authorization");
        using var objUnanchored = new Database(bVersionSix: true);
        Sql(objUnanchored, "DROP TABLE audit_chain_state");
        Reject<System.Security.Cryptography.CryptographicException>(objUnanchored.Upgrade, "Version 6 without its required audit anchor cannot be silently trusted");
    }
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception => SecurityPolicyChecks.Reject<T>(actCheck, strMessage);
    private static long Scalar(Database objDatabase, string strSql) => SecurityPolicyChecks.Scalar(objDatabase, strSql);
    private static void Sql(Database objDatabase, string strSql) => SecurityPolicyChecks.Sql(objDatabase, strSql);
}
