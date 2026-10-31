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
            AccountChecks();
            Console.WriteLine($"{SecurityPolicyChecks.iCount} security/migration/view-model checks passed.");
            Console.WriteLine("Production security logic passed using the Windows in-memory SQLite TEST adapter; SQLCipher/provider/UI acceptance remains pending.");
            return 0;
        }
        catch (Exception objException) { Console.Error.WriteLine(objException); return 1; }
    }

    private static void AccountChecks()
    {
        using var objDatabase=new Database(); var objAuth=new AuthService(objDatabase);
        using var objAdmin=objAuth.CreateAdministratorForTest("fixture-owner","Fictional strong password 2026!");
        var objUsers=new UserAdministrationService(objDatabase,objAdmin);
        using var secInitial=TestPasswords.Secret("Fictional initial password 2026!");
        using var secNew=TestPasswords.Secret("Fictional replacement password 2026!");
        var lngUser=objUsers.CreateUser("fixture-colleague",secInitial,UserRole.Editor);
        using var objSession=objAuth.SignIn("fixture-colleague",secInitial); var objCrm=new CrmService(objDatabase,objSession);
        SecurityPolicyChecks.Assert(objUsers.Users().Count==2,"User administration lists persisted accounts");
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => new UserAdministrationService(objDatabase,objSession).Users(),"Regular users cannot list accounts");
        SecurityPolicyChecks.Reject<ArgumentException>(() => objUsers.SetEnabled(objAdmin.UserId,false),"Administrator cannot disable their own account");
        objUsers.SetEnabled(lngUser,false);
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => objCrm.Contacts(),"Disabling an account revokes existing sessions");
        SecurityPolicyChecks.Reject<ArgumentException>(() => objAuth.SignIn("fixture-colleague",secInitial),"Disabled accounts cannot sign in");
        objUsers.SetEnabled(lngUser,true); objUsers.ResetPassword(lngUser,secNew);
        SecurityPolicyChecks.Reject<ArgumentException>(() => objAuth.SignIn("fixture-colleague",secInitial),"Reset invalidates the previous password");
        using var objNew=objAuth.SignIn("fixture-colleague",secNew);
        SecurityPolicyChecks.Reject<ArgumentException>(() => objAuth.ChangePassword(objNew,secInitial,secInitial),"Password changes require the current password");
        objAuth.ChangePassword(objNew,secNew,secInitial);
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => new CrmService(objDatabase,objNew),"Password change revokes all prior sessions");
        using var objChanged=objAuth.SignIn("fixture-colleague",secInitial);
        SecurityPolicyChecks.Assert(new CrmService(objDatabase,objChanged).Role==UserRole.Editor,"Changed password signs in successfully");
        SecurityPolicyChecks.Sql(objDatabase,"CREATE TRIGGER test_block_password_audit BEFORE INSERT ON audit_log BEGIN SELECT RAISE(ABORT,'Fixture audit unavailable'); END");
        SecurityPolicyChecks.Reject<Microsoft.Data.Sqlite.SqliteException>(() => objUsers.ResetPassword(lngUser,secNew),"Password reset fails closed when its audit insert fails");
        SecurityPolicyChecks.Sql(objDatabase,"DROP TRIGGER test_block_password_audit");
        using var objStillValid=objAuth.SignIn("fixture-colleague",secInitial);
        SecurityPolicyChecks.Assert(new CrmService(objDatabase,objStillValid).Role==UserRole.Editor,"Failed audited reset preserves the password and sessions");
    }
    private static void MigrationChecks()
    {
        using var objLegacy = new Database(bLegacy: true);
        SecurityPolicyChecks.Sql(objLegacy, "INSERT INTO users(username,salt,password_hash,iterations,created_at) VALUES('fixture-legacy',zeroblob(32),zeroblob(32),600000,'2026-01-01')");
        SecurityPolicyChecks.Sql(objLegacy, "INSERT INTO audit_log(user_id,entity,entity_id,action,created_at) VALUES(1,'contacts',7,'Updated','2026-01-01')");
        objLegacy.Upgrade();
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT version FROM schema_version") == 5, "Legacy schema upgrades transactionally to version 5");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM users WHERE role='Administrator'") == 1, "The single documented bootstrap administrator retains its role");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM audit_log WHERE entity='contacts' AND entity_id=7 AND action='Updated'") == 1, "Migration preserves historical audit payloads");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM audit_log WHERE action='SchemaUpgraded' AND entity_id=2") == 1, "Schema migration is audited in its transaction");
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
        using var objConcurrent = new Database();
        using var objSession = new AuthService(objConcurrent).CreateAdministratorForTest("fixture-concurrent","Fictional strong password 2026!");
        var objCrm = new CrmService(objConcurrent,objSession);
        var objContact = new Contact { FirstName="Fixture" }; objCrm.Save(objContact);
        var objStale = objCrm.Contacts().Single(); objContact.Phone="+1 (202) 555-0141"; objCrm.Save(objContact); objStale.Phone="+1 (202) 555-0142";
        SecurityPolicyChecks.Reject<ArgumentException>(() => objCrm.Save(objStale),"Concurrent contact edits reject a stale revision");
        SecurityPolicyChecks.Assert(objCrm.Contacts().Single().Phone=="+1 (202) 555-0141","Rejected stale contact saves preserve the current record");
        var objOrganization = new Organization { Name="Fixture org" }; objCrm.Save(objOrganization);
        var objOld = objCrm.Organizations().Single(); objOrganization.Phone="+1 (202) 555-0143"; objCrm.Save(objOrganization);
        SecurityPolicyChecks.Reject<ArgumentException>(() => objCrm.Save(objOld),"Concurrent organization edits reject a stale revision");
        objConcurrent.Upgrade(); SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objConcurrent,"SELECT version FROM schema_version")==5,"Reopening version 5 does not repeat migrations");
    }
}
