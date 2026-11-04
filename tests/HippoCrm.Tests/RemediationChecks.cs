using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using HippoCrm.Core;
using Microsoft.Data.Sqlite;

internal static class RemediationChecks
{
    internal static void Run(string strFolder) => RunAsync(strFolder).GetAwaiter().GetResult();

    private static async Task RunAsync(string strFolder)
    {
        using var objKeys = new FixtureKeys();
        var objDatabase = new Database(Path.Combine(strFolder, "security.db"), objKeys);
        var objAuth = new AuthService(objDatabase);
        using var secPassword = Secret("FICTIONAL-Passphrase-2026!");
        using var secWrong = Secret("FICTIONAL-Wrong-Passphrase!");
        using var objAdmin = objAuth.CreateAdministrator("fixture-admin", secPassword);
        Check(objKeys.arrLastRead!.All(bytValue => bytValue == 0), "Transient credential-store key buffers are wiped");
        using (var dbConnection = objDatabase.Open())
        using (var cmdSignature = new SqliteCommand("SELECT count(*) FROM audit_log WHERE signature IS NULL OR length(signature)<>32", dbConnection))
            Check(Convert.ToInt64(cmdSignature.ExecuteScalar()) == 0, "Every new audit entry has a 32-byte signature");

        var swAttempt = Stopwatch.StartNew();
        await RejectAsync<ArgumentException>(() => objAuth.SignInAsync("fixture-admin", secWrong));
        Check(swAttempt.ElapsedMilliseconds >= 500, "Rejected login observes the 500ms minimum");
        swAttempt.Restart();
        using (var objLogin = await objAuth.SignInAsync("fixture-admin", secPassword))
            Check(swAttempt.ElapsedMilliseconds >= 500, "Successful login observes the 500ms minimum");
        swAttempt.Restart();
        await RejectAsync<OperationCanceledException>(() => objAuth.SignInAsync("fixture-admin", secPassword, new CancellationToken(true)));
        Check(swAttempt.ElapsedMilliseconds >= 500, "Cancelled login cannot bypass the delay");

        var objCrm = new CrmService(objDatabase, objAdmin);
        var objOrganization = new Organization { Name = "FICTIONAL Organization" };
        objCrm.Save(objOrganization);
        var objContact = new Contact { FirstName = "FICTIONAL", LastName = "Person", lstOrganizationIds = [objOrganization.Id] };
        objCrm.Save(objContact);
        using (var dbConnection = objDatabase.Open())
        using (var cmdFailAudit = new SqliteCommand("CREATE TRIGGER fixture_reject_audit BEFORE INSERT ON audit_log WHEN NEW.action='Created' BEGIN SELECT RAISE(ABORT,'Fixture audit failure'); END", dbConnection))
            cmdFailAudit.ExecuteNonQuery();
        Reject<SqliteException>(() => objCrm.Save(new Organization { Name = "FICTIONAL must roll back" }));
        using (var dbConnection = objDatabase.Open())
        using (var cmdRestoreAudit = new SqliteCommand("DROP TRIGGER fixture_reject_audit; SELECT count(*) FROM organizations", dbConnection))
            Check(Convert.ToInt64(cmdRestoreAudit.ExecuteScalar()) == 1, "Audit write failure rolls back the business mutation");
        objContact.lstOrganizationIds.Clear(); objContact.lngOrganizationId = null; objCrm.Save(objContact);
        Check(objCrm.Contacts().Single().lstOrganizationIds.Count == 0, "Inactive organization assignments are hidden");
        using (var dbConnection = objDatabase.Open())
        using (var cmdRetained = new SqliteCommand("SELECT count(*) FROM contact_organizations WHERE status='Inactive'", dbConnection))
            Check(Convert.ToInt64(cmdRetained.ExecuteScalar()) == 1, "Removed organization assignments remain retained");

        long lngViewer;
        using (var dbConnection = objDatabase.Open())
        using (var txOperation = dbConnection.BeginTransaction(deferred: false))
        {
            lngViewer = AuthService.InsertUser(dbConnection, txOperation, "fixture-viewer", secPassword, UserRole.Viewer);
            SecurityAudit.Append(dbConnection, txOperation, objAdmin.UserId, AuditEntity.Users, lngViewer, AuditAction.UserCreated);
            txOperation.Commit();
        }
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, true, false);
        using var objViewer = await objAuth.SignInAsync("fixture-viewer", secPassword);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        Check(objViewerCrm.Contacts().Count == 1, "Active record grants permit scoped reads");
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, false, false);
        Check(objViewerCrm.Contacts().Count == 0, "Revoked retained grants do not grant access");
        objAuth.SignOut(objViewer);
        using (var dbConnection = objDatabase.Open())
        using (var cmdSessions = new SqliteCommand("SELECT count(*) FROM sessions WHERE user_id=$user AND status='Inactive'", dbConnection))
        {
            cmdSessions.Parameters.AddWithValue("$user", lngViewer);
            Check(Convert.ToInt64(cmdSessions.ExecuteScalar()) == 1, "Sign-out retains and inactivates session records");
        }

        for (var iAttempt = 0; iAttempt < 5; iAttempt++)
            await RejectAsync<ArgumentException>(() => objAuth.SignInAsync("fixture-admin", secWrong));
        await RejectAsync<ArgumentException>(() => objAuth.SignInAsync("fixture-admin", secPassword));
        using (var dbConnection = objDatabase.Open())
        using (var cmdLock = new SqliteCommand("SELECT locked_until FROM users WHERE id=$user", dbConnection))
        {
            cmdLock.Parameters.AddWithValue("$user", objAdmin.UserId);
            Check(DateTimeOffset.Parse((string)cmdLock.ExecuteScalar()!) > DateTimeOffset.UtcNow.AddMinutes(14), "Five failed attempts enforce a 15-minute lockout");
        }

        using (var dbConnection = objDatabase.Open())
        {
            using var cmdUnsigned = new SqliteCommand("INSERT INTO audit_log(entity,entity_id,action,created_at) VALUES('Contacts',0,'Updated','invalid')", dbConnection);
            Reject<SqliteException>(() => cmdUnsigned.ExecuteNonQuery());
            Check(true, "Unsigned audit inserts are rejected");
            // Simulate an administrator bypassing SQL triggers; HMAC still detects the change.
            using var cmdTamper = new SqliteCommand("DROP TRIGGER immutable_audit; UPDATE audit_log SET action='tampered' WHERE id=(SELECT MAX(id) FROM audit_log)", dbConnection);
            cmdTamper.ExecuteNonQuery();
        }
        Reject<CryptographicException>(() => { using var dbConnection = objDatabase.Open(); });
        Check(true, "Tampered signed audit history blocks workspace access");
        CheckLegacyMigration(Path.Combine(strFolder, "legacy.db"), objKeys);
    }

    private static void CheckLegacyMigration(string strPath, FixtureKeys objKeys)
    {
        using (var dbConnection = SqlCipherConnectionFactory.Open(strPath, objKeys, bCreate: true))
        {
            using var stmSchema = typeof(Database).Assembly.GetManifestResourceStream("HippoCrm.Core.Schema.sql")!;
            using var rdrSchema = new StreamReader(stmSchema);
            using var cmdLegacy = new SqliteCommand(rdrSchema.ReadToEnd() + "; UPDATE schema_version SET version=5; INSERT INTO audit_log(entity,entity_id,action,created_at) VALUES('Schema',5,'SchemaUpgraded','fixture-historical-time');", dbConnection);
            cmdLegacy.ExecuteNonQuery();
        }
        var objDatabase = new Database(strPath, objKeys);
        using (var dbConnection = objDatabase.Open())
        {
            using var cmdLegacy = new SqliteCommand("SELECT count(*) FROM audit_log WHERE id=1 AND signature IS NULL", dbConnection);
            Check(Convert.ToInt64(cmdLegacy.ExecuteScalar()) == 1, "Migration preserves original unsigned historical audit entries");
            using var cmdSigned = new SqliteCommand("SELECT count(*) FROM audit_log WHERE id>1 AND length(signature)=32", dbConnection);
            Check(Convert.ToInt64(cmdSigned.ExecuteScalar()) > 0, "Migration appends a signed schema-upgrade event");
            using var cmdTamper = new SqliteCommand("DROP TRIGGER immutable_audit; UPDATE audit_log SET action='tampered' WHERE id=1", dbConnection);
            cmdTamper.ExecuteNonQuery();
        }
        Reject<CryptographicException>(() => { using var dbConnection = objDatabase.Open(); });
        Check(true, "Historical snapshot changes are detected after migration");
    }

    private static SecureString Secret(string strFixture)
    {
        var secPassword = new SecureString();
        foreach (var chrValue in strFixture) secPassword.AppendChar(chrValue);
        secPassword.MakeReadOnly(); return secPassword;
    }
    private static void Check(bool bCondition, string strMessage)
    {
        if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage);
        Console.WriteLine("PASS " + strMessage);
    }
    private static void Reject<T>(Action actOperation) where T : Exception
    {
        try { actOperation(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static async Task RejectAsync<T>(Func<Task<Session>> fnOperation) where T : Exception
    {
        try { using var objUnexpected = await fnOperation(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class FixtureKeys : IDatabaseKeyStore, IDisposable
    {
        private readonly Dictionary<string, byte[]> dicKeys = new(StringComparer.OrdinalIgnoreCase);
        public byte[]? arrLastRead { get; private set; }
        public void EnsureKey(string strIdentity, bool bAllowCreate)
        {
            if (dicKeys.ContainsKey(strIdentity)) return;
            if (!bAllowCreate) throw new CryptographicException("Missing fixture key.");
            var arrKey = SensitiveMemory.AllocateBytes(32); RandomNumberGenerator.Fill(arrKey); dicKeys.Add(strIdentity, arrKey);
        }
        public byte[] ReadKey(string strIdentity)
        {
            arrLastRead = SensitiveMemory.AllocateBytes(32);
            dicKeys[strIdentity].CopyTo(arrLastRead, 0); return arrLastRead;
        }
        public void Dispose() { foreach (var arrKey in dicKeys.Values) SensitiveMemory.Clear(arrKey); }
    }
}
