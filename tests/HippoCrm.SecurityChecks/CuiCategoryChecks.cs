using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class CuiCategoryChecks
{
    internal static CuiDesignation Designation(string strLetter) => new(CuiPolicy.strCtiMarking, strLetter,
        "FICTIONAL controlling office", "2026-11-04",
        $"Distribution Statement {strLetter}. FICTIONAL authority-approved audience and reason; determination 2026-11-04. Refer other requests to FICTIONAL controlling office.");

    internal static void Run()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL CTI policy password!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("cti-admin", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        Reject<UnauthorizedAccessException>(() => objCrm.Save(CuiPolicy.Apply(new Contact { FirstName = "Denied" }, Designation("B"))), "Basic CUI administrator cannot create CTI without explicit category/distribution approval");
        Reject<ArgumentException>(() => objUsers.SetCuiPolicyAccess(objAdmin.UserId, CuiPolicy.strCtiMarking, "B", true, "FICTIONAL unverified approval"), "CTI approval requires verified recipient eligibility");
        objUsers.SetCuiRecipientKind(objAdmin.UserId, CuiRecipientKind.DoD, "FICTIONAL DoD affiliation evidence");
        Reject<ArgumentException>(() => objUsers.SetCuiPolicyAccess(objAdmin.UserId, CuiPolicy.strCtiMarking, "A", true, "FICTIONAL approval"), "Public distribution A is rejected for CTI");
        Reject<ArgumentException>(() => CuiPolicy.Validate(new CuiDesignation(CuiPolicy.strCtiMarking, "B")), "CTI requires controlling office, determination date and full statement");
        Reject<ArgumentException>(() => CuiPolicy.Validate(Designation("B") with { strDeterminationDate = "2026-02-30" }), "Impossible CTI determination date is rejected");
        Reject<ArgumentException>(() => CuiPolicy.Validate(Designation("B") with { strDistributionNotice = "Distribution Statement C. Wrong statement" }), "Statement text cannot contradict the distribution letter");
        var lstContacts = new List<Contact>(); var lstOrganizations = new List<Organization>();
        foreach (var strLetter in new[] { "B", "C", "D", "E", "F" })
        {
            objUsers.SetCuiPolicyAccess(objAdmin.UserId, CuiPolicy.strCtiMarking, strLetter, true, "FICTIONAL controlling authority " + strLetter);
            var objContact = CuiPolicy.Apply(new Contact { FirstName = "FICTIONAL CTI " + strLetter, strTags = "tag-" + strLetter }, Designation(strLetter));
            var objOrganization = CuiPolicy.Apply(new Organization { Name = "FICTIONAL CTI " + strLetter }, Designation(strLetter));
            objCrm.Save(objContact); objCrm.Save(objOrganization); lstContacts.Add(objContact); lstOrganizations.Add(objOrganization);
            Check(objCrm.ContactRecord(objContact.Id).objCuiDesignation == Designation(strLetter), "Contact policy persists " + strLetter);
            Check(objCrm.OrganizationRecord(objOrganization.Id).objCuiDesignation == Designation(strLetter), "Organization policy persists " + strLetter);
        }
        var lngOtherAdmin = objUsers.CreateUser("cti-other-admin", secPassword, UserRole.Administrator);
        objUsers.SetCuiAuthorization(lngOtherAdmin, true); objUsers.SetCuiRecipientKind(lngOtherAdmin, CuiRecipientKind.UsGovernment, "FICTIONAL Government evidence");
        objUsers.SetCuiPolicyAccess(lngOtherAdmin, CuiPolicy.strCtiMarking, "B", true, "FICTIONAL B approval");
        using var objOtherAdmin = new AuthService(objDatabase).SignIn("cti-other-admin", secPassword);
        var objOtherCrm = new CrmService(objDatabase, objOtherAdmin);
        Check(objOtherCrm.Contacts().Count == 0, "CTI administrator still needs the independent record grant");
        objUsers.SetRecordAccess(lngOtherAdmin, RecordKind.Contact, lstContacts[0].Id, true, false);
        Check(objOtherCrm.Contacts().Single().Id == lstContacts[0].Id, "Explicit CTI record grant permits only its approved record");
        Reject<UnauthorizedAccessException>(() => objOtherCrm.Save(objOtherCrm.ContactRecord(lstContacts[0].Id)), "Administrator cannot override a read-only CTI record grant");
        var lngEditor = objUsers.CreateUser("cti-editor", secPassword, UserRole.Editor);
        objUsers.SetCuiAuthorization(lngEditor, true);
        objUsers.SetCuiRecipientKind(lngEditor, CuiRecipientKind.UsDodContractor, "FICTIONAL DoD contractor evidence");
        Reject<ArgumentException>(() => objUsers.SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "B", true, "FICTIONAL wrong audience"), "Government-only B cannot be granted to a contractor");
        Reject<ArgumentException>(() => objUsers.SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "E", true, "FICTIONAL wrong audience"), "DoD-only E cannot be granted to a contractor");
        foreach (var objContact in lstContacts) objUsers.SetRecordAccess(lngEditor, RecordKind.Contact, objContact.Id, true, true);
        foreach (var objOrganization in lstOrganizations) objUsers.SetRecordAccess(lngEditor, RecordKind.Organization, objOrganization.Id, true, true);
        using var objEditor = new AuthService(objDatabase).SignIn("cti-editor", secPassword);
        var objEditorCrm = new CrmService(objDatabase, objEditor);
        Check(objEditorCrm.Contacts().Count == 0 && objEditorCrm.Organizations().Count == 0 && objEditorCrm.Dashboard().Contacts == 0, "Record grants plus Basic CUI cannot disclose CTI rows or aggregates");
        objUsers.SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "C", true, "FICTIONAL C authority");
        Sql(objDatabase, "INSERT INTO cui_policy_access(user_id,cui_category,distribution_statement,approval_reference,status) VALUES($user,'CUI//SP-CTI','B','FICTIONAL invalid legacy approval','Active')", ("$user", lngEditor));
        Check(objEditorCrm.Contacts().Single().Id == lstContacts[1].Id && objEditorCrm.Organizations().Single().Id == lstOrganizations[1].Id, "Only explicitly approved distribution C is visible; B/D/E/F are independent");
        Check(objEditorCrm.LookupPage().lstContacts.Count == 1, "Runtime recipient checks reject an ineligible distribution even if an invalid approval row exists");
        Check(objEditorCrm.TokenChoices().Tags.SequenceEqual(new[] { "tag-C" }), "Token catalog cannot disclose tags from other distributions");
        Check(objEditorCrm.LookupPage().lstContacts.Single().objCuiDesignation == Designation("C"), "Paged projections retain complete CTI designation");
        Check(objEditorCrm.Contacts(iLimit: 128).Single().objCuiDesignation == Designation("C"), "Bounded browse projection retains complete CTI designation");
        Check(objEditorCrm.AllOrganizations(bNamesOnly: true).Single().objCuiDesignation == Designation("C"), "Name-only relationship choices retain CTI labels");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.ContactRecord(lstContacts[0].Id), "Guessed record ID cannot bypass distribution policy");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Interactions(lstContacts[0].Id), "Related history inherits the contact policy");
        Reject<UnauthorizedAccessException>(() => new UserAdministrationService(objDatabase, objEditor).SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "B", true, "FICTIONAL self approval"), "Editor cannot self-approve a category");
        var objEditable = objEditorCrm.ContactRecord(lstContacts[1].Id); objEditable.FirstName = "FICTIONAL allowed edit";
        objEditorCrm.Save(objEditable);
        Check(objEditorCrm.ContactRecord(objEditable.Id).FirstName == objEditable.FirstName, "Authorized CTI editor can save content without changing protection");
        CuiPolicy.Apply(objEditable, new CuiDesignation(CuiPolicy.strMarking));
        Reject<ArgumentException>(() => objEditorCrm.Save(objEditable), "Ordinary save cannot downgrade a CTI designation");
        Reject<SqliteException>(() => Sql(objDatabase, "UPDATE contacts SET distribution_statement='' WHERE id=$id", ("$id", lstContacts[1].Id)), "SQLite rejects CTI without distribution independently of C# validation");
        var lngEpoch = objCrm.CuiEpoch();
        using var objLease = objEditor.CreateCuiMemoryLease(lngEpoch);
        Sql(objDatabase, "CREATE TRIGGER fixture_policy_audit BEFORE INSERT ON audit_log WHEN NEW.action='CuiPolicyRevoked' BEGIN SELECT RAISE(ABORT,'FICTIONAL audit failure'); END");
        Reject<SqliteException>(() => objUsers.SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "C", false, "FICTIONAL revoke"), "Category revocation fails closed when audit is unavailable");
        Check(objEditorCrm.Contacts().Count == 1 && objCrm.CuiEpoch() == lngEpoch, "Failed category revocation rolls back policy, session revocation and epoch");
        Sql(objDatabase, "DROP TRIGGER fixture_policy_audit");
        objUsers.SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "C", false, "FICTIONAL revoke");
        Check(objCrm.CuiEpoch() > lngEpoch && objLease.NeedsRefresh, "Committed category revocation invalidates authorization freshness");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Contacts(), "Category revocation invalidates an already-issued session");
        objUsers.SetCuiAuthorization(lngEditor, false); objUsers.SetCuiAuthorization(lngEditor, true);
        using var objReenabled = new AuthService(objDatabase).SignIn("cti-editor", secPassword);
        Check(new CrmService(objDatabase, objReenabled).Contacts().Count == 0, "Global CUI reapproval cannot revive former CTI grants");
        var objBefore = objCrm.ContactRecord(lstContacts[0].Id);
        Sql(objDatabase, "CREATE TRIGGER fixture_designation_audit BEFORE INSERT ON audit_log WHEN NEW.action='CuiDesignationChanged' BEGIN SELECT RAISE(ABORT,'FICTIONAL audit failure'); END");
        Reject<SqliteException>(() => objUsers.SetRecordCuiDesignation(RecordKind.Contact, objBefore.Id, objBefore.Revision, Designation("F")), "Designation change rolls back on audit failure");
        Check(objCrm.ContactRecord(objBefore.Id).objCuiDesignation == Designation("B"), "Failed designation change preserves original policy");
        Sql(objDatabase, "DROP TRIGGER fixture_designation_audit");
        objUsers.SetRecordCuiDesignation(RecordKind.Contact, objBefore.Id, objBefore.Revision, Designation("F"));
        Check(objCrm.ContactRecord(objBefore.Id).objCuiDesignation == Designation("F"), "Audited administrator designation command changes policy");
        Reject<ArgumentException>(() => objUsers.SetRecordCuiDesignation(RecordKind.Contact, objBefore.Id, objBefore.Revision, Designation("B")), "Stale designation revision cannot overwrite a concurrent change");
        var lngEvents = SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM audit_log"); objDatabase.Upgrade();
        Check(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM audit_log") == lngEvents, "Reopening schema 9 does not regrant policies or repeat migration");
        VersionEightMigrationChecks();
    }

    private static void VersionEightMigrationChecks()
    {
        using var objLegacy = new Database(bVersionSix: true);
        using var secPassword = TestPasswords.Secret("FICTIONAL version eight CTI migration!");
        using var objAdmin = new AuthService(objLegacy).CreateAdministrator("cti-legacy", secPassword);
        using var stmMigration = typeof(Database).Assembly.GetManifestResourceStream("HippoCrm.Core.Migration007.sql")!;
        using var rdrMigration = new StreamReader(stmMigration); Sql(objLegacy, rdrMigration.ReadToEnd());
        Sql(objLegacy, "UPDATE schema_version SET version=8; UPDATE users SET cui_authorized=1 WHERE id=1; INSERT INTO organizations(name,email,phone,address,services,tags,status,created_at,updated_at) VALUES('FICTIONAL retained image owner','','','','','','Active','fixture','fixture'); INSERT INTO profile_images(entity,entity_id,png,status,created_at,user_id) VALUES('Organization',1,X'010203','Inactive','fixture',1)");
        Sql(objLegacy, "CREATE TRIGGER fixture_deny_policy_migration BEFORE INSERT ON audit_log WHEN NEW.action='SchemaUpgraded' AND NEW.entity_id=9 BEGIN SELECT RAISE(ABORT,'FICTIONAL audit unavailable'); END");
        Reject<SqliteException>(objLegacy.Upgrade, "Version 8 policy migration fails closed when audit is unavailable");
        Check(SecurityPolicyChecks.Scalar(objLegacy, "SELECT version FROM schema_version") == 8
            && SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM sqlite_master WHERE name='cui_policy_access'") == 0,
            "Failed policy migration rolls back new grants, columns and version together");
        Sql(objLegacy, "DROP TRIGGER fixture_deny_policy_migration"); objLegacy.Upgrade();
        Check(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM cui_policy_access WHERE cui_category='CUI' AND status='Active'") == 1
            && SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM cui_policy_access WHERE cui_category='CUI//SP-CTI'") == 0,
            "Version 8 migration preserves Basic CUI authorization without inventing CTI approvals");
        Check(SecurityPolicyChecks.Scalar(objLegacy, "SELECT count(*) FROM profile_images WHERE cui_category='CUI' AND distribution_statement='' AND png=X'010203' AND status='Inactive'") == 1,
            "Migration retains original image BLOB/status and inherits its parent's Basic CUI policy");
        Check(new CrmService(objLegacy, objAdmin).Organizations().Single().strCuiCategory == CuiPolicy.strMarking, "Existing approved session retains only its prior Basic CUI access after migration");
    }

    internal static void Sql(Database objDatabase, string strSql, params (string strName, object? objValue)[] arrParameters)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = Database.Command(dbConnection, strSql, arrParameters); cmdSql.ExecuteNonQuery(); }
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception => SecurityPolicyChecks.Reject<T>(actCheck, strMessage);
}
