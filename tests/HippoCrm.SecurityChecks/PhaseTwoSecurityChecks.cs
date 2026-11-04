using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class PhaseTwoSecurityChecks
{
    internal static void Run()
    {
        LifecycleAndCuiChecks(); MigrationChecks();
    }

    private static void LifecycleAndCuiChecks()
    {
        using var objDatabase = new Database();
        using var objAdmin = new AuthService(objDatabase).CreateAdministratorForTest("phase2-admin", "FICTIONAL phase two password!");
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        var objCrm = new CrmService(objDatabase, objAdmin);
        Check(!objUsers.Users().Single().bCuiAuthorized && !objCrm.CanCreateRecords, "Administrator role does not implicitly authorize CUI");
        Reject<UnauthorizedAccessException>(() => objCrm.Save(new Contact { FirstName = "Denied" }), "Unapproved administrator cannot create CUI");
        Check(objCrm.Contacts().Count == 0 && objCrm.Dashboard().Contacts == 0, "Unapproved administrator sees no CUI records or aggregates");
        objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var lstContacts = new List<Contact>(); var lstOrganizations = new List<Organization>();
        foreach (var enStatus in Enum.GetValues<RecordStatus>())
        {
            var objContact = new Contact { FirstName = "FICTIONAL " + enStatus, Status = enStatus, strTags = "tag-" + enStatus, Skills = "skill-" + enStatus };
            var objOrganization = new Organization { Name = "FICTIONAL " + enStatus, Status = enStatus, Tags = "org-" + enStatus };
            objCrm.Save(objContact); objCrm.Save(objOrganization); lstContacts.Add(objContact); lstOrganizations.Add(objOrganization);
            Check(objCrm.Contacts(enStatus: enStatus).Single().Id == objContact.Id, "Contact lifecycle filter selects " + enStatus);
            Check(objCrm.Organizations(enStatus: enStatus).Single().Id == objOrganization.Id, "Organization lifecycle filter selects " + enStatus);
            Check(objCrm.TokenChoices(enStatus).Tags.Contains("tag-" + enStatus), "Token catalog honors explicit " + enStatus + " lifecycle");
            Check(objCrm.Dashboard(enStatus).Contacts == 1 && objCrm.Dashboard(enStatus).Organizations == 1, "Dashboard parameter isolates " + enStatus);
        }
        Check(objCrm.Contacts().Count == 1 && objCrm.Organizations().Count == 1 && objCrm.AllOrganizations().Count == 1, "Default lookup and organization choices include only Active records");
        Check(objCrm.Contacts(enStatus: null).Count == 4 && objCrm.Organizations(enStatus: null).Count == 4, "Explicit Any status retains all four authorized lifecycle states");
        Check(objCrm.TokenChoices().Tags.All(strTag => strTag.Contains("Active", StringComparison.Ordinal)), "Default catalogs exclude inactive, retired and deceased data");
        Check(objCrm.Dashboard().Contacts == 1 && objCrm.Dashboard().Organizations == 1, "Dashboard totals use Active lifecycle scope");
        Reject<ArgumentException>(() => objCrm.Contacts(enStatus: (RecordStatus)99), "Invalid contact lifecycle is rejected");
        Reject<ArgumentException>(() => objCrm.Organizations(enStatus: (RecordStatus)99), "Invalid organization lifecycle is rejected");
        Reject<ArgumentException>(() => objCrm.AllOrganizations((RecordStatus)99), "Invalid organization choice status is rejected");
        Reject<ArgumentException>(() => objCrm.TokenChoices((RecordStatus)99), "Invalid token lifecycle is rejected");
        Reject<ArgumentException>(() => objCrm.Dashboard((RecordStatus)99), "Invalid dashboard lifecycle is rejected");
        Reject<ArgumentException>(() => objCrm.Save(new Contact { FirstName = "Invalid", strClassification = "" }), "Missing contact CUI tag is rejected");
        Reject<ArgumentException>(() => objCrm.Save(new Organization { Name = "Invalid", strClassification = null! }), "Null organization CUI tag is rejected");
        Reject<ArgumentException>(() => objCrm.Save(new Contact { FirstName = "Invalid", strClassification = "PUBLIC" }), "Unsupported CUI tag cannot downgrade a record");
        Reject<SqliteException>(() => Sql(objDatabase, "UPDATE contacts SET classification='PUBLIC' WHERE id=1"), "Database rejects invalid classification independently of the service");
        var objActive = lstContacts[0]; var objActiveOrg = lstOrganizations[0];
        using var secUser = TestPasswords.Secret("FICTIONAL user password!");
        var lngEditor = objUsers.CreateUser("phase2-editor", secUser, UserRole.Editor);
        var lngViewer = objUsers.CreateUser("phase2-viewer", secUser, UserRole.Viewer);
        objUsers.SetRecordAccess(lngEditor, RecordKind.Contact, objActive.Id, true, true);
        objUsers.SetRecordAccess(lngEditor, RecordKind.Organization, objActiveOrg.Id, true, true);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objActive.Id, true, true);
        using var objEditor = new AuthService(objDatabase).SignIn("phase2-editor", secUser);
        using var objViewer = new AuthService(objDatabase).SignIn("phase2-viewer", secUser);
        var objEditorCrm = new CrmService(objDatabase, objEditor); var objViewerCrm = new CrmService(objDatabase, objViewer);
        Check(objEditorCrm.Contacts().Count == 0 && objEditorCrm.Organizations().Count == 0 && objEditorCrm.TokenChoices().Tags.Count == 0, "Record grants cannot replace CUI authorization");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Interactions(objActive.Id), "Unapproved user cannot read related CUI history");
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Save(objActive), "Unapproved user cannot mutate even a writable grant");
        Reject<UnauthorizedAccessException>(() => new UserAdministrationService(objDatabase, objEditor).SetCuiAuthorization(lngEditor, true), "Editors cannot approve their own CUI authorization");
        objUsers.SetCuiAuthorization(lngEditor, true); objUsers.SetCuiAuthorization(lngViewer, true);
        Check(objEditorCrm.Contacts().Single().Id == objActive.Id, "CUI authorization plus record grant permits the assigned contact");
        Check(objViewerCrm.Organizations().Count == 0, "CUI authorization does not replace record grants");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.Save(objActive), "Viewer role still overrides a write grant after CUI authorization");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.SetArchived(RecordKind.Contact, objActive.Id, objActive.Revision, true), "Viewer cannot archive CUI");
        foreach (var objContact in lstContacts.Skip(1))
        {
            Reject<ArgumentException>(() => objCrm.AddInteraction(objContact.Id, "FICTIONAL note"), "Non-Active contact cannot receive a note");
            Reject<ArgumentException>(() => objCrm.AddContract(objContact.Id, new DateOnly(2026, 1, 1), 1m, "", ""), "Non-Active contact cannot receive a contract");
        }
        Sql(objDatabase, "CREATE TRIGGER fixture_deny_cui BEFORE INSERT ON audit_log WHEN NEW.action IN ('CuiAuthorized','CuiRevoked') BEGIN SELECT RAISE(ABORT,'Fictional unavailable audit'); END");
        Reject<SqliteException>(() => objUsers.SetCuiAuthorization(lngEditor, false), "CUI revocation fails when its audit fails");
        Check(objEditorCrm.Contacts().Count == 1, "Failed CUI revocation rolls back both flag and session changes");
        Sql(objDatabase, "DROP TRIGGER fixture_deny_cui");
        objUsers.SetCuiAuthorization(lngEditor, false);
        Reject<UnauthorizedAccessException>(() => objEditorCrm.Contacts(), "CUI revocation invalidates an existing service session");
        ArchiveChecks(objDatabase, objCrm, objActive, objActiveOrg, lstContacts[2]);
        // Corrupt fixture metadata by bypassing only the fixture CHECK constraints.
        Sql(objDatabase, "PRAGMA ignore_check_constraints=ON; UPDATE contacts SET classification='UNKNOWN' WHERE id=4; PRAGMA ignore_check_constraints=OFF;");
        Check(objCrm.Contacts(enStatus: RecordStatus.Deceased).Count == 0, "Unknown stored marking is excluded from lists");
        Reject<UnauthorizedAccessException>(() => objCrm.Contracts(lstContacts[3].Id), "Unknown stored marking denies direct history access");
        Check(Scalar(objDatabase, "SELECT count(*) FROM contacts") == 4 && Scalar(objDatabase, "SELECT count(*) FROM organizations") == 4, "Lifecycle and denial operations retain every business record");
    }

    private static void ArchiveChecks(Database objDatabase, CrmService objCrm, Contact objContact, Organization objOrganization, Contact objRetired)
    {
        Sql(objDatabase, "CREATE TRIGGER fixture_deny_archive BEFORE INSERT ON audit_log WHEN NEW.action='Archived' BEGIN SELECT RAISE(ABORT,'Fictional unavailable audit'); END");
        Reject<SqliteException>(() => objCrm.SetArchived(RecordKind.Contact, objContact.Id, objContact.Revision, true), "Contact archive fails closed when audit cannot append");
        Reject<SqliteException>(() => objCrm.SetArchived(RecordKind.Organization, objOrganization.Id, objOrganization.Revision, true), "Organization archive fails closed when audit cannot append");
        Check(objCrm.Contacts().Single().Revision == objContact.Revision && objCrm.Organizations().Count == 1, "Archive audit failure rolls back status, revision and visibility");
        Sql(objDatabase, "DROP TRIGGER fixture_deny_archive");
        objCrm.SetArchived(RecordKind.Contact, objContact.Id, objContact.Revision, true);
        objCrm.SetArchived(RecordKind.Organization, objOrganization.Id, objOrganization.Revision, true);
        Check(objCrm.Contacts().Count == 0 && objCrm.Contacts(bArchived: true, enStatus: RecordStatus.Inactive).Single().Status == RecordStatus.Inactive, "Archiving Active contact performs retained Inactive transition");
        Check(objCrm.Organizations(bArchived: true, enStatus: RecordStatus.Inactive).Count == 1, "Organization archive uses the same status transition");
        Reject<ArgumentException>(() => objCrm.SetArchived(RecordKind.Contact, objContact.Id, objContact.Revision, false), "Stale archive revision is rejected");
        objCrm.SetArchived(RecordKind.Contact, objContact.Id, objContact.Revision + 1, false);
        Check(objCrm.Contacts(enStatus: RecordStatus.Inactive).Any(objItem => objItem.Id == objContact.Id), "Restoring visibility does not reactivate a contact");
        objCrm.SetArchived(RecordKind.Contact, objRetired.Id, objRetired.Revision, true);
        objCrm.SetArchived(RecordKind.Contact, objRetired.Id, objRetired.Revision + 1, false);
        Check(objCrm.Contacts(enStatus: RecordStatus.Retired).Single().Status == RecordStatus.Retired, "Archive and restore preserve Retired status");
        var objCurrent = objCrm.Contacts(enStatus: RecordStatus.Inactive).Single(objItem => objItem.Id == objContact.Id);
        objCurrent.Archived = true;
        Reject<ArgumentException>(() => objCrm.Save(objCurrent), "Generic save cannot bypass the archive command");
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='Archived'") == 3, "Successful archive transitions have signed audit events only");
    }

    private static void MigrationChecks()
    {
        using var objLegacy = new Database(bVersionSix: true);
        using var objOldAdmin = new AuthService(objLegacy).CreateAdministratorForTest("legacy-admin", "FICTIONAL legacy password!");
        // The captured version 6 schema did not have these new lifecycle guards.
        Sql(objLegacy, "DROP TRIGGER retain_organizations_INSERT; DROP TRIGGER retain_organizations_UPDATE;");
        Sql(objLegacy, "INSERT INTO organizations(name,email,phone,address,services,tags,status,archived,created_at,updated_at) VALUES('FICTIONAL historical','','','','','','Active',1,'fixture','fixture'),('FICTIONAL deceased','','','','','','Deceased',1,'fixture','fixture')");
        var arrContactColumns = new[] { "first_name", "last_name", "email", "phone", "address", "skills", "spouse", "children", "family", "friends", "twitter", "instagram", "snapchat", "facebook" };
        Sql(objLegacy, $"INSERT INTO contacts({string.Join(',', arrContactColumns)},status,availability,created_at,updated_at) VALUES({string.Join(',', arrContactColumns.Select(strColumn => strColumn == "first_name" ? "'FICTIONAL legacy'" : "''"))},'Retired','Unknown','fixture','fixture')");
        Sql(objLegacy, "CREATE TRIGGER fixture_deny_upgrade BEFORE INSERT ON audit_log WHEN NEW.action='SchemaUpgraded' BEGIN SELECT RAISE(ABORT,'Fictional unavailable audit'); END");
        Reject<SqliteException>(objLegacy.Upgrade, "Classification migration fails closed if its audit cannot append");
        Check(Scalar(objLegacy, "SELECT version FROM schema_version") == 6 && Scalar(objLegacy, "SELECT count(*) FROM pragma_table_info('users') WHERE name='cui_authorized'") == 0,
            "Failed migration rolls back schema version and authorization columns");
        Sql(objLegacy, "DROP TRIGGER fixture_deny_upgrade"); objLegacy.Upgrade();
        Check(Scalar(objLegacy, "SELECT version FROM schema_version") == 8 && Scalar(objLegacy, "SELECT count(*) FROM organizations WHERE classification='CUI'") == 2, "Additive migration tags all retained organizations CUI");
        Check(Scalar(objLegacy, "SELECT count(*) FROM contacts WHERE classification='CUI' AND status='Retired'") == 1, "Migration tags existing contact CUI without changing its retained lifecycle");
        Check(Scalar(objLegacy, "SELECT count(*) FROM users WHERE cui_authorized=0") == 1, "Migration denies existing administrator CUI access by default");
        Check(Scalar(objLegacy, "SELECT count(*) FROM organizations WHERE status='Inactive' AND revision=1") == 1 && Scalar(objLegacy, "SELECT count(*) FROM organizations WHERE status='Deceased'") == 1,
            "Legacy archive normalization preserves Deceased status and invalidates stale Active revisions");
        Check(new CrmService(objLegacy, objOldAdmin).Organizations(bArchived: true, enStatus: null).Count == 0, "Pre-migration session cannot inherit CUI authority");
        var lngAudit = Scalar(objLegacy, "SELECT count(*) FROM audit_log"); objLegacy.Upgrade();
        Check(Scalar(objLegacy, "SELECT count(*) FROM audit_log") == lngAudit && Scalar(objLegacy, "SELECT count(*) FROM organizations") == 2, "Reopening version 8 repeats no migration and retains all records");
    }

    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception => SecurityPolicyChecks.Reject<T>(actCheck, strMessage);
    private static void Sql(Database objDatabase, string strSql) => SecurityPolicyChecks.Sql(objDatabase, strSql);
    private static long Scalar(Database objDatabase, string strSql) => SecurityPolicyChecks.Scalar(objDatabase, strSql);
}
