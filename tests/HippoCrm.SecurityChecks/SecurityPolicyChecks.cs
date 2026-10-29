using System.Security;
using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class SecurityPolicyChecks
{
    private static int iPassed;
    internal static int iCount => iPassed;
    public static void Run(Database objDatabase)
    {
        var objAuthentication = new AuthService(objDatabase);
        using var objAdministrator = objAuthentication.CreateAdministratorForTest("fixture-admin", "Fictional strong password 2026!");
        var objAdministration = new UserAdministrationService(objDatabase, objAdministrator);
        var objAdminCrm = new CrmService(objDatabase, objAdministrator);
        var lngEditor = CreateUser(objAdministration, "fixture-editor", UserRole.Editor);
        var lngViewer = CreateUser(objAdministration, "fixture-viewer", UserRole.Viewer);
        var lngOutsider = CreateUser(objAdministration, "fixture-outsider", UserRole.Viewer);
        using var objEditor = objAuthentication.SignInForTest("fixture-editor", "Fictional strong password 2026!");
        using var objViewer = objAuthentication.SignInForTest("fixture-viewer", "Fictional strong password 2026!");
        using var objOutsider = objAuthentication.SignInForTest("fixture-outsider", "Fictional strong password 2026!");
        var objEditorCrm = new CrmService(objDatabase, objEditor);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        var objOutsiderCrm = new CrmService(objDatabase, objOutsider);
        var objVisible = new Contact { FirstName = "FICTIONAL-VISIBLE" };
        var objHidden = new Contact { FirstName = "FICTIONAL-HIDDEN" };
        var objOrganization = new Organization { Name = "FICTIONAL-ORGANIZATION" };
        objAdminCrm.Save(objVisible); objAdminCrm.Save(objHidden); objAdminCrm.Save(objOrganization);
        objAdminCrm.AddInteraction(objVisible.Id, "FICTIONAL-NOTE-NOT-FOR-AUDIT");
        objAdminCrm.AddInteraction(objHidden.Id, "FICTIONAL-HIDDEN-NOTE");
        objAdminCrm.AddEmployment(objVisible.Id, objOrganization.Id, "Fixture role", new DateOnly(2026, 1, 1), null);
        objAdminCrm.AddContract(objVisible.Id, new DateOnly(2026, 1, 1), 12.34m, "Fixture", "Fixture");
        objAdministration.SetRecordAccess(lngEditor, RecordKind.Contact, objVisible.Id, true, true);
        objAdministration.SetRecordAccess(lngEditor, RecordKind.Organization, objOrganization.Id, true, false);
        objAdministration.SetRecordAccess(lngViewer, RecordKind.Contact, objVisible.Id, true, true); // Role must override a write grant.
        Assert(objEditorCrm.Contacts().Single().Id == objVisible.Id, "Editor lists only assigned contacts");
        Assert(objEditorCrm.Contacts("FICTIONAL-HIDDEN").Count == 0 && objEditorCrm.Contacts("' OR 1=1 --").Count == 0, "Search cannot bypass record grants or SQL parameterization");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Save(objOrganization), "A read-only organization grant cannot authorize edits");
        Assert(objViewerCrm.Organizations().Count == 0, "Viewer cannot list unassigned organizations");
        Assert(objOutsiderCrm.Contacts().Count == 0 && objOutsiderCrm.Dashboard().Contacts == 0, "Unassigned users receive no records or aggregate counts");
        Assert(objViewerCrm.Dashboard().Contacts == 1 && objViewerCrm.Dashboard().Interactions30Days == 1 && objViewerCrm.Dashboard().Organizations == 0,
            "Dashboard aggregates exclude inaccessible contacts, notes and organizations");
        Assert(objViewerCrm.EmploymentHistory(objVisible.Id).Count == 0, "Employment does not expose an unassigned organization's identity");
        objAdministration.SetRecordAccess(lngViewer, RecordKind.Organization, objOrganization.Id, true, false);
        Assert(objViewerCrm.EmploymentHistory(objVisible.Id).Count == 1, "Employment becomes visible with both record grants");
        Assert(objViewerCrm.Interactions(objVisible.Id).Count == 1 && objViewerCrm.Contracts(objVisible.Id).Count == 1, "Authorized viewers can read related history");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.Interactions(objHidden.Id), "Guessed contact IDs cannot expose notes");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.Contracts(objHidden.Id), "Guessed contact IDs cannot expose contracts");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.EmploymentHistory(objHidden.Id), "Guessed contact IDs cannot expose employment");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.Save(objVisible), "Viewer cannot update a record even with a write grant");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.Save(objOrganization), "Viewer cannot update an organization");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.AddInteraction(objVisible.Id, "Blocked"), "Viewer cannot append notes");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.AddContract(objVisible.Id, new DateOnly(2026, 1, 1), 1m, "", ""), "Viewer cannot append contracts");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.AddEmployment(objVisible.Id, objOrganization.Id, "", new DateOnly(2027, 1, 1), null), "Viewer cannot append employment");
        var lngPlacement = objAdminCrm.EmploymentHistory(objVisible.Id).Single().Id;
        Reject<UnauthorizedAccessException>(() => objViewerCrm.EndEmployment(lngPlacement, new DateOnly(2026, 12, 31)), "Viewer cannot end employment");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Save(objHidden), "Editor cannot update an unassigned record");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Save(new Contact { FirstName = "Blocked" }), "Only administrators can create records");
        objVisible.Phone = "555-FIXTURE"; objEditorCrm.Save(objVisible);
        Assert(objAdminCrm.Contacts().Single(objContact => objContact.Id == objVisible.Id).Phone == "555-FIXTURE", "An editor can update an assigned writable record");
        objEditorCrm.AddInteraction(objVisible.Id, "FICTIONAL-EDITOR-NOTE");
        Assert(objAdminCrm.Interactions(objVisible.Id).First().Author == "fixture-editor", "Mutation attribution comes from the validated session");
        objAdministration.SetRecordAccess(lngEditor, RecordKind.Organization, objOrganization.Id, false, false);
        Reject<UnauthorizedAccessException>(() => objEditorCrm.EndEmployment(lngPlacement, new DateOnly(2026, 12, 31)), "Guessed employment IDs cannot bypass organization visibility");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.AddEmployment(objVisible.Id, objOrganization.Id, "", new DateOnly(2027, 1, 1), null), "Adding employment requires visibility of the referenced organization");
        objAdministration.SetRecordAccess(lngEditor, RecordKind.Organization, objOrganization.Id, true, false);
        using var secUnauthorized = Secret();
        Reject<UnauthorizedAccessException>(() => new UserAdministrationService(objDatabase, objEditor).CreateUser("blocked-user", secUnauthorized, UserRole.Administrator), "Editors cannot create administrators");
        Reject<UnauthorizedAccessException>(() => new UserAdministrationService(objDatabase, objViewer).SetRecordAccess(lngViewer, RecordKind.Contact, objHidden.Id, true, true), "Viewers cannot grant their own access");
        Reject<ArgumentException>(() => objAdministration.SetRole(objAdministrator.UserId, UserRole.Viewer), "The last enabled administrator cannot be demoted");
        using var objForged = new Session(objAdministrator.UserId, objAdministrator.Username);
        Reject<UnauthorizedAccessException>(() => new CrmService(objDatabase, objForged), "A forged identity without an issued token is rejected");
        objAdministration.SetRecordAccess(lngEditor, RecordKind.Contact, objVisible.Id, false, false);
        Assert(objEditorCrm.Contacts().Count == 0, "Grant revocation affects an existing service immediately");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Save(objVisible), "Stale models cannot bypass revoked grants");
        objAdministration.SetRecordAccess(lngEditor, RecordKind.Contact, objVisible.Id, true, true);
        objAdministration.SetRole(lngEditor, UserRole.Viewer);
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Contacts(), "Role changes revoke existing sessions");
        using var objDemoted = objAuthentication.SignInForTest("fixture-editor", "Fictional strong password 2026!");
        Reject<UnauthorizedAccessException>(() => new CrmService(objDatabase, objDemoted).Save(objVisible), "Re-authenticated demoted users cannot reuse a write grant");
        var lngBeforeUnknown = Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='SignInFailed'");
        Reject<ArgumentException>(() => objAuthentication.SignInForTest("FICTIONAL-UNKNOWN-USERNAME", "Wrong fictional password"), "Unknown accounts are rejected");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='SignInFailed'") == lngBeforeUnknown + 1, "Unknown-account attempts are audited");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='SignInFailed' AND user_id IS NULL") > 0, "Unknown-account auditing needs no fabricated user identity");
        for (var iAttempt = 0; iAttempt < 5; iAttempt++)
            Reject<ArgumentException>(() => objAuthentication.SignInForTest("fixture-outsider", "Wrong fictional password"), "Incorrect password is rejected and audited");
        Assert(Scalar(objDatabase, "SELECT failed_attempts FROM users WHERE id=" + lngOutsider) == 5, "Failure increments persist atomically");
        Reject<ArgumentException>(() => objAuthentication.SignInForTest("fixture-outsider", "Fictional strong password 2026!"), "Persisted lockout blocks a correct password");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='SignInLocked'") > 0, "Locked-account attempts are audited");
        Sql(objDatabase, "UPDATE users SET locked_until='2000-01-01T00:00:00+00:00' WHERE id=" + lngOutsider);
        using var objRecovered = objAuthentication.SignInForTest("fixture-outsider", "Fictional strong password 2026!");
        var lngSessions = Scalar(objDatabase, "SELECT count(*) FROM sessions");
        var lngContacts = Scalar(objDatabase, "SELECT count(*) FROM contacts");
        var lngAudit = Scalar(objDatabase, "SELECT count(*) FROM audit_log");
        Sql(objDatabase, "CREATE TRIGGER test_block_audit BEFORE INSERT ON audit_log BEGIN SELECT RAISE(ABORT,'Fixture audit unavailable'); END");
        Reject<SqliteException>(() => objAdminCrm.Save(new Contact { FirstName = "Must roll back" }), "Audit failure blocks a CRM mutation");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM contacts") == lngContacts, "Failed audit rolls back the record insert");
        Reject<SqliteException>(() => objAuthentication.SignInForTest("fixture-admin", "Fictional strong password 2026!"), "Audit failure prevents successful authentication");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM sessions") == lngSessions, "Failed authentication audit rolls back session issuance");
        Reject<SqliteException>(() => objAuthentication.SignInForTest("fixture-admin", "Wrong fictional password"), "Audit failure blocks password-failure persistence");
        Assert(Scalar(objDatabase, "SELECT failed_attempts FROM users WHERE id=" + objAdministrator.UserId) == 0, "Failure counters roll back with unavailable audit logging");
        Reject<SqliteException>(() => objAuthentication.SignInForTest("missing-fixture", "Wrong fictional password"), "Unknown-account attempts fail secure when auditing fails");
        Reject<SqliteException>(() => objOutsiderCrm.Interactions(objVisible.Id), "Denied reads remain blocked when their audit insert fails");
        Reject<SqliteException>(() => objViewerCrm.Contacts(), "Authorized reads also fail closed when auditing is unavailable");
        Reject<SqliteException>(() => objAdministration.SetRole(lngViewer, UserRole.Editor), "Audit failure rolls back role changes");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM users WHERE id=" + lngViewer + " AND role='Viewer'") == 1, "A failed role change preserves the previous role");
        Reject<SqliteException>(() => objAdministration.SetRecordAccess(lngViewer, RecordKind.Contact, objHidden.Id, true, true), "Audit failure rolls back access grants");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM contact_access WHERE user_id=" + lngViewer) == 1 && Scalar(objDatabase, "SELECT count(*) FROM audit_log") == lngAudit, "Failed grants expose no additional records and create no partial event");
        using var secNewUser = Secret();
        Reject<SqliteException>(() => objAdministration.CreateUser("fixture-rollback-user", secNewUser, UserRole.Viewer), "Audit failure rolls back user provisioning");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM users WHERE username='fixture-rollback-user'") == 0, "Failed provisioning creates no orphan user");
        Sql(objDatabase, "DROP TRIGGER test_block_audit");
        Assert(objViewerCrm.Role == UserRole.Viewer, "A failed role change preserves the existing session");
        objAuthentication.SignOut(objViewer);
        Reject<UnauthorizedAccessException>(() => objViewerCrm.Contacts(), "Signed-out service instances cannot read records");
        Assert(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='SignedOut'") > 0, "Sign-out is audited");
        Reject<SqliteException>(() => Sql(objDatabase, "UPDATE audit_log SET action='tampered'"), "Audit entries cannot be updated");
        Reject<SqliteException>(() => Sql(objDatabase, "DELETE FROM audit_log"), "Audit entries cannot be deleted");
        using (var dbConnection = objDatabase.Open())
        {
            using var cmdEvents = dbConnection.CreateCommand(); cmdEvents.CommandText = "SELECT entity,action FROM audit_log";
            using var rdrEvents = cmdEvents.ExecuteReader();
            var bValidEvents = true;
            while (rdrEvents.Read())
                bValidEvents &= Enum.TryParse<AuditEntity>(rdrEvents.GetString(0), out var enEntity) && Enum.TryParse<AuditAction>(rdrEvents.GetString(1), out var enAction);
            Assert(bValidEvents, "Audit text contains only defined event names");
        }
        Console.WriteLine($"{iPassed} security policy checks passed.");
    }

    private static SecureString Secret() => TestPasswords.Secret("Fictional strong password 2026!");
    private static long CreateUser(UserAdministrationService objAdministration, string strUsername, UserRole enRole)
    {
        using var secPassword = Secret(); return objAdministration.CreateUser(strUsername, secPassword, enRole);
    }
    internal static long Scalar(Database objDatabase, string strSql)
    {
        using var dbConnection = objDatabase.Open(); using var cmdStatement = dbConnection.CreateCommand(); cmdStatement.CommandText = strSql;
        return Convert.ToInt64(cmdStatement.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
    internal static void Sql(Database objDatabase, string strSql)
    {
        using var dbConnection = objDatabase.Open(); using var cmdStatement = dbConnection.CreateCommand(); cmdStatement.CommandText = strSql; cmdStatement.ExecuteNonQuery();
    }
    internal static void Assert(bool bCondition, string strDescription)
    {
        if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
        iPassed++; Console.WriteLine("PASS " + strDescription);
    }
    internal static void Reject<T>(Action actOperation, string strDescription) where T : Exception
    {
        try { actOperation(); }
        catch (T) { Assert(true, strDescription); return; }
        throw new InvalidOperationException("FAIL " + strDescription);
    }
}
