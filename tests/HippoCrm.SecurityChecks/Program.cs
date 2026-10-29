using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class SecurityChecks
{
    public static int Main()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("These adapter checks require the Windows-bundled SQLite engine."); return 1; }
            using var objDatabase = new Database();
            SecurityPolicyChecks.Run(objDatabase);
            MigrationChecks();
            ViewModelSecurityChecks.Run();
            Console.WriteLine($"{SecurityPolicyChecks.iCount} security/migration/view-model checks passed.");
            Console.WriteLine("Production security logic passed using the Windows in-memory SQLite TEST adapter; SQLCipher/provider/UI acceptance remains pending.");
            return 0;
        }
        catch (Exception objException) { Console.Error.WriteLine(objException); return 1; }
    }

    private static void MigrationChecks()
    {
        using var objLegacy = new Database(bLegacy: true);
        SecurityPolicyChecks.Sql(objLegacy, "INSERT INTO users(username,salt,password_hash,iterations,created_at) VALUES('fixture-legacy',zeroblob(32),zeroblob(32),600000,'2026-01-01')");
        SecurityPolicyChecks.Sql(objLegacy, "INSERT INTO audit_log(user_id,entity,entity_id,action,created_at) VALUES(1,'contacts',7,'Updated','2026-01-01')");
        objLegacy.Upgrade();
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT version FROM schema_version") == 2, "Legacy schema upgrades transactionally to version 2");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM users WHERE role='Administrator'") == 1, "The single documented bootstrap administrator retains its role");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM audit_log WHERE entity='contacts' AND entity_id=7 AND action='Updated'") == 1, "Migration preserves historical audit payloads");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM audit_log WHERE action='SchemaUpgraded'") == 1, "Schema migration is audited in its transaction");
        SecurityPolicyChecks.Reject<Microsoft.Data.Sqlite.SqliteException>(() => SecurityPolicyChecks.Sql(objLegacy, "DELETE FROM audit_log"), "Migration restores audit retention triggers");
        using var objAmbiguous = new Database(bLegacy: true);
        SecurityPolicyChecks.Sql(objAmbiguous, "INSERT INTO users(username,salt,password_hash,iterations,created_at) VALUES('fixture-first',zeroblob(32),zeroblob(32),600000,'2026-01-01'),('fixture-second',zeroblob(32),zeroblob(32),600000,'2026-01-01')");
        SecurityPolicyChecks.Reject<InvalidOperationException>(objAmbiguous.Upgrade, "Ambiguous legacy roles fail closed");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objAmbiguous, "SELECT version FROM schema_version") == 1 && SecurityPolicyChecks.Scalar(objAmbiguous, "SELECT count(*) FROM pragma_table_info('users') WHERE name='role'") == 0,
            "Rejected migration leaves the legacy schema intact");
        using var objUnversioned = new Database();
        SecurityPolicyChecks.Sql(objUnversioned, "DROP TABLE schema_version");
        SecurityPolicyChecks.Reject<InvalidOperationException>(objUnversioned.Upgrade, "Unversioned nonempty databases fail closed");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objUnversioned, "SELECT count(*) FROM sqlite_master WHERE name='schema_version'") == 0, "Rejected unversioned databases are not stamped as current");
        using var objBootstrapFailure = new Database();
        SecurityPolicyChecks.Sql(objBootstrapFailure, "CREATE TRIGGER test_block_audit BEFORE INSERT ON audit_log BEGIN SELECT RAISE(ABORT,'Fixture audit unavailable'); END");
        SecurityPolicyChecks.Reject<Microsoft.Data.Sqlite.SqliteException>(() => new AuthService(objBootstrapFailure).CreateAdministratorForTest("fixture-bootstrap", "Fictional strong password 2026!"), "Bootstrap fails secure when the audit insert fails");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objBootstrapFailure, "SELECT count(*) FROM users") == 0 && SecurityPolicyChecks.Scalar(objBootstrapFailure, "SELECT count(*) FROM sessions") == 0,
            "Failed bootstrap leaves no administrator or authenticated session");
    }
}
