using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class PhaseThreeRollbackChecks
{
    internal static void Run()
    {
        CheckSuffixTruncation(); CheckDatabaseRollback(); CheckExpiredEf();
    }

    private static void CheckSuffixTruncation()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL suffix fixture password!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("suffix-fixture", secPassword);
        new UserAdministrationService(objDatabase, objAdmin).SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objContact = new Contact { FirstName = "Before" }; objCrm.Save(objContact);
        var lngOldTip = SecurityPolicyChecks.Scalar(objDatabase, "SELECT MAX(id) FROM audit_log");
        objContact.FirstName = "After"; objCrm.Save(objContact);
        // Simulate hostile file-level surgery in this disposable MEMORY database.
        // Every retained audit row/signature remains byte-for-byte valid; business data remains current.
        using (var dbHostile = objDatabase.Open())
        using (var cmdHostile = new SqliteCommand("""
            CREATE TABLE fixture_prefix AS SELECT * FROM audit_log WHERE id<=$tip;
            DROP TABLE audit_log;
            ALTER TABLE fixture_prefix RENAME TO audit_log;
            """, dbHostile))
        { cmdHostile.Parameters.AddWithValue("$tip", lngOldTip); cmdHostile.ExecuteNonQuery(); }
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM contacts WHERE first_name='After'") == 1,
            "Suffix attack preserves current business data while removing only signed audit suffix");
        SecurityPolicyChecks.Reject<AuditRecoveryRequiredException>(() => objCrm.Contacts(), "Valid signed suffix truncation fails the external freshness check");
    }

    private static void CheckDatabaseRollback()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL rollback fixture password!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("rollback-fixture", secPassword);
        new UserAdministrationService(objDatabase, objAdmin).SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objContact = new Contact { FirstName = "Snapshot" }; objCrm.Save(objContact);
        using var dbSnapshot = new SqliteConnection("Data Source=:memory:;Pooling=False"); dbSnapshot.Open();
        using var dbLive = objDatabase.Open(); dbLive.BackupDatabase(dbSnapshot);
        objContact.FirstName = "Latest"; objCrm.Save(objContact);
        var objTrusted = objDatabase.objCheckpoint.Read();
        dbSnapshot.BackupDatabase(dbLive);
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM contacts WHERE first_name='Snapshot'") == 1,
            "Whole-database rollback fixture restores an actual prior SQLite snapshot");
        SecurityPolicyChecks.Reject<AuditRecoveryRequiredException>(() => objCrm.Contacts(), "Whole-database rollback is rejected despite valid historical HMACs");
        SecurityPolicyChecks.Assert(objDatabase.objCheckpoint.Read() == objTrusted, "Rejected rollback cannot reset external trusted progress");
    }

    private static void CheckExpiredEf()
    {
        using var objDatabase = new Database();
        var objClock = new ManualSessionClock();
        using var secPassword = TestPasswords.Secret("FICTIONAL expired EF fixture password!");
        using var objAdmin = new AuthService(objDatabase, objClock).CreateAdministrator("expired-ef", secPassword);
        new UserAdministrationService(objDatabase, objAdmin).SetCuiAuthorization(objAdmin.UserId, true);
        using var dbContext = new CrmDbContext(objDatabase);
        objClock.lngTicks += Session.InactivityLimit.Ticks + 1;
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => dbContext.ReadAuthorizedContacts(objAdmin), "EF gateway rejects input immediately after expiry");
        dbContext.dbContacts.Add(new Contact { FirstName = "Denied" });
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => dbContext.SaveAuthorizedChanges(objAdmin), "EF mutation rejects an expired session before saving");
    }
}
