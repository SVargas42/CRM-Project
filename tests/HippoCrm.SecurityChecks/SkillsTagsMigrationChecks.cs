using HippoCrm.Core;
using Microsoft.Data.Sqlite;

internal static class SkillsTagsMigrationChecks
{
    private static readonly string[] arrTables = ["skills", "tags", "contact_skills", "contact_tags", "organization_tags"];
    internal static void Run()
    {
        string strFreshDefinitions;
        using (var objFresh = new Database())
        {
            Check(Scalar(objFresh, "SELECT version FROM schema_version") == 11 && Tables(objFresh) == 5,
                "Fresh schema reaches version 11 with all five word/link tables");
            strFreshDefinitions = WordDefinitions(objFresh);
            var strBefore = Snapshot(objFresh);
            Check(Upgrade(objFresh).Count == 0 && Snapshot(objFresh) == strBefore,
                "Fresh version 11 reinitialization has no skipped values or changes");
        }
        using var objDatabase = VersionNineFixture();
        // Preserve exact SQLite UTF-8 bytes, including separators/control characters, in assertions only.
        var strOriginal = SourceSnapshot(objDatabase);
        var lstSkipped = Upgrade(objDatabase);
        Check(Scalar(objDatabase, "SELECT version FROM schema_version") == 11 && Tables(objDatabase) == 5,
            "Version 9 upgrades transactionally to 10 with all five tables");
        Check(WordDefinitions(objDatabase) == strFreshDefinitions, "Fresh and upgraded databases have identical word/link definitions and indexes");
        Check(SourceSnapshot(objDatabase) == strOriginal, "Backfill preserves original source column bytes including NULL and organization services");
        Check(Scalar(objDatabase, "SELECT count(*) FROM skills") == 5 && Scalar(objDatabase, "SELECT count(*) FROM contact_skills") == 6,
            "Backfill handles separators, trim, duplicates, variants, Unicode, empty, NULL, archived and multiple batches");
        Check(Scalar(objDatabase, "SELECT count(*) FROM tags") == 3 && Scalar(objDatabase, "SELECT count(*) FROM contact_tags") == 2
            && Scalar(objDatabase, "SELECT count(*) FROM organization_tags") == 2,
            "Backfill shares tags across both parent types without importing services");
        Check(Scalar(objDatabase, "SELECT count(*) FROM skills WHERE name=$name", ("$name", "Demolitions")) == 1
            && Text(objDatabase, "SELECT name FROM skills WHERE name=$name", ("$name", "demolitions")) == "Demolitions",
            "Case-only duplicates retain first spelling while spelling variants remain independent");
        Check(lstSkipped.Count == 2 && lstSkipped.All(objSkip => objSkip.strTable == "contacts" && objSkip.lngRecordId == 2 && objSkip.strColumn == "skills")
            && !string.Join("", lstSkipped).Contains("Bad", StringComparison.Ordinal),
            "Skipped report contains only contacts record ID 2 column skills metadata for two rejected values");
        foreach (var strTable in arrTables)
        {
            Check(Scalar(objDatabase, $"SELECT count(*) FROM {strTable} WHERE status<>'Active'") == 0, "Backfill creates Active rows in " + strTable);
            SecurityPolicyChecks.Reject<SqliteException>(() => Sql(objDatabase, $"UPDATE {strTable} SET status='Invalid'"), "Status CHECK rejects invalid lifecycle in " + strTable);
            SecurityPolicyChecks.Reject<SqliteException>(() => Sql(objDatabase, $"DELETE FROM {strTable}"), "Retention rejects DELETE on " + strTable);
        }
        foreach (var strName in new[] { "", " ", "a,b", "a\tb", "a\u007fb", new string('x', 65) })
            SecurityPolicyChecks.Reject<SqliteException>(() => Sql(objDatabase, "INSERT INTO skills(name) VALUES($name)", ("$name", strName)), "Word schema rejects invalid name without exposing text");
        SecurityPolicyChecks.Reject<SqliteException>(() => Sql(objDatabase, "INSERT INTO contact_skills VALUES(999999,1,'Active')"), "Word links enforce parent foreign keys");
        SecurityPolicyChecks.Reject<SqliteException>(() => Sql(objDatabase, "INSERT INTO contact_tags VALUES(1,999999,'Active')"), "Word links enforce word foreign keys");
        Check(Scalar(objDatabase, "SELECT count(*) FROM pragma_index_list('contact_skills') WHERE name='idx_contact_skills_skill_id'") == 1
            && Scalar(objDatabase, "SELECT count(*) FROM pragma_index_list('contact_tags') WHERE name='idx_contact_tags_tag_id'") == 1
            && Scalar(objDatabase, "SELECT count(*) FROM pragma_index_list('organization_tags') WHERE name='idx_organization_tags_tag_id'") == 1,
            "Each word link has its word-column index");
        var strUpgraded = Snapshot(objDatabase);
        Check(Upgrade(objDatabase).Count == 0 && Snapshot(objDatabase) == strUpgraded, "Second Initialize leaves version 11 data, schema and audit unchanged");
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE entity_id=10 AND action='SchemaUpgraded'") == 2,
            "Upgrade appends exactly one version 11 event to preexisting fixture history");

        using var objFailure = VersionNineFixture();
        var strFailureBefore = SourceSnapshot(objFailure);
        var strAuditBefore = Text(objFailure, "SELECT group_concat(hex(signature),'|') FROM audit_log");
        var bRejected = false;
        using (var dbConnection = objFailure.Open())
        {
            using var txSchema = dbConnection.BeginTransaction(false);
            // Install the migration and a hostile trigger INSIDE the same transaction. Rollback
            // must remove both, exercising insertion failure without a production fault hook.
            Execute(dbConnection, txSchema, Resource("Migration010.sql"));
            Execute(dbConnection, txSchema, "CREATE TRIGGER fixture_backfill_failure BEFORE INSERT ON skills WHEN (SELECT count(*) FROM skills)>0 BEGIN SELECT RAISE(ABORT,'PRIVATE_TRIGGER_VALUE'); END");
            try { SchemaManager.Initialize(dbConnection, txSchema); }
            catch (InvalidOperationException objError)
            {
                bRejected = objError.Message == "Token migration failed: contacts, record ID 1, column skills."
                    && objError.InnerException is null && !objError.ToString().Contains("PRIVATE_TRIGGER_VALUE", StringComparison.Ordinal);
            }
        }
        Check(bRejected, "Backfill failure exposes only contacts record ID 1 column skills and suppresses provider value text");
        Check(Scalar(objFailure, "SELECT version FROM schema_version") == 9 && Tables(objFailure) == 0
            && SourceSnapshot(objFailure) == strFailureBefore && Text(objFailure, "SELECT group_concat(hex(signature),'|') FROM audit_log") == strAuditBefore,
            "Backfill failure rolls back to version 9 without new tables or source/audit changes");
    }

    private static Database VersionNineFixture()
    {
        var objDatabase = new Database();
        // Reverse only fixture additions to reconstruct the version 9 boundary; never run on disk.
        Sql(objDatabase, "DROP TABLE contact_skills; DROP TABLE contact_tags; DROP TABLE organization_tags; DROP TABLE skills; DROP TABLE tags; UPDATE schema_version SET version=9");
        // Nullable historical text is deliberately simulated; production definitions remain NOT NULL.
        Sql(objDatabase, "ALTER TABLE contacts RENAME COLUMN skills TO fixture_old_skills; ALTER TABLE contacts ADD COLUMN skills TEXT");
        const string strInsert = "INSERT INTO contacts(id,first_name,last_name,email,phone,address,fixture_old_skills,skills,tags,spouse,children,family,friends,twitter,instagram,snapchat,facebook,status,availability,archived,created_at,updated_at) VALUES($id,'Fixture','','','','','',$skills,$tags,'','','','','','','','','Inactive','Unknown',$archived,'fixture','fixture')";
        for (var iId = 1; iId <= 505; iId++)
        {
            object? objSkills = iId switch
            {
                1 => ", Demolitions,demolitions,, demolition,\tEngineer\t,\r\nÉquipe,équipe,",
                2 => "Demolitions," + new string('x', 65) + ",Bad\u0001Name,\t",
                4 => null,
                505 => "BatchWord",
                _ => ""
            };
            Sql(objDatabase, strInsert, ("$id", iId), ("$skills", objSkills), ("$tags", iId == 1 ? "Shared,,ContactOnly" : ""), ("$archived", iId == 2));
        }
        Sql(objDatabase, "INSERT INTO organizations(id,name,email,phone,address,services,tags,status,archived,created_at,updated_at) VALUES(1,'Fixture','','','',$services,$tags,'Inactive',1,'fixture','fixture')",
            ("$services", "SERVICE_SENTINEL"), ("$tags", "shared,OrgOnly"));
        return objDatabase;
    }
    private static IReadOnlyList<SchemaManager.SkippedToken> Upgrade(Database objDatabase)
    {
        using var dbConnection = objDatabase.Open(); using var txSchema = dbConnection.BeginTransaction(false);
        var lstSkipped = SchemaManager.Initialize(dbConnection, txSchema);
        SecurityAudit.Commit(dbConnection, txSchema); return lstSkipped;
    }
    private static long Tables(Database objDatabase) => Scalar(objDatabase,
        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('skills','tags','contact_skills','contact_tags','organization_tags')");
    private static string WordDefinitions(Database objDatabase) => Text(objDatabase,
        "SELECT group_concat(payload,'|') FROM (SELECT type||':'||name||':'||sql AS payload FROM sqlite_master WHERE tbl_name IN ('skills','tags','contact_skills','contact_tags','organization_tags') ORDER BY type,name)");
    private static string SourceSnapshot(Database objDatabase) => Text(objDatabase,
        "SELECT group_concat(payload,'|') FROM (SELECT id||':'||hex(skills)||':'||(skills IS NULL)||':'||hex(tags) AS payload FROM contacts ORDER BY id)")
        + Text(objDatabase, "SELECT group_concat(payload,'|') FROM (SELECT id||':'||hex(tags)||':'||hex(services) AS payload FROM organizations ORDER BY id)");
    private static string Snapshot(Database objDatabase)
    {
        var strResult = Text(objDatabase, "SELECT group_concat(payload,'|') FROM (SELECT type||':'||name||':'||sql AS payload FROM sqlite_master ORDER BY type,name)");
        foreach (var strTable in arrTables.Concat(new[] { "schema_version", "audit_log" }))
        {
            var strProjection = strTable switch
            {
                "skills" or "tags" => "id||':'||hex(name)||':'||status",
                "contact_skills" => "contact_id||':'||skill_id||':'||status",
                "contact_tags" => "contact_id||':'||tag_id||':'||status",
                "organization_tags" => "organization_id||':'||tag_id||':'||status",
                "schema_version" => "version",
                _ => "id||':'||hex(signature)"
            };
            strResult += Text(objDatabase, $"SELECT group_concat(payload,'|') FROM (SELECT {strProjection} AS payload FROM {strTable} ORDER BY 1)");
        }
        return strResult;
    }
    private static string Resource(string strName)
    { using var stmSql = typeof(SchemaManager).Assembly.GetManifestResourceStream("HippoCrm.Core." + strName)!; using var rdrSql = new StreamReader(stmSql); return rdrSql.ReadToEnd(); }
    private static void Execute(SqliteConnection dbConnection, SqliteTransaction txSchema, string strSql)
    { using var cmdSql = Database.Command(dbConnection, strSql); cmdSql.Transaction = txSchema; cmdSql.ExecuteNonQuery(); }
    private static void Sql(Database objDatabase, string strSql, params (string, object?)[] arrParameters) => CuiCategoryChecks.Sql(objDatabase, strSql, arrParameters);
    private static long Scalar(Database objDatabase, string strSql, params (string, object?)[] arrParameters)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = Database.Command(dbConnection, strSql, arrParameters); return Convert.ToInt64(cmdSql.ExecuteScalar()); }
    private static string Text(Database objDatabase, string strSql, params (string, object?)[] arrParameters)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = Database.Command(dbConnection, strSql, arrParameters); return Convert.ToString(cmdSql.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? ""; }
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
}
