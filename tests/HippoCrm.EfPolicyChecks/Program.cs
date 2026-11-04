using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

internal static class EfPolicyChecks
{
    public static int Main()
    {
        try
        {
            PhaseTwoSecurityChecks.Run();
            RunEfChecks();
            Console.WriteLine($"{SecurityPolicyChecks.iCount} real EF/in-memory policy checks passed. No production startup gate was bypassed.");
            return 0;
        }
        catch (Exception objException) { Console.Error.WriteLine(objException); return 1; }
    }

    private static void RunEfChecks()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL EF policy password!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("ef-admin", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        using var dbContext = new CrmDbContext(objDatabase);
        Check(dbContext.ReadAuthorizedContacts(objAdmin).Count == 0, "EF administrator without CUI authorization reads no contacts");
        dbContext.dbContacts.Add(new Contact { FirstName = "Unapproved" });
        Reject<UnauthorizedAccessException>(() => dbContext.SaveAuthorizedChanges(objAdmin), "EF creation cannot bypass CUI authorization");
        dbContext.ChangeTracker.Clear(); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var lstContacts = new List<Contact>();
        foreach (var enStatus in Enum.GetValues<RecordStatus>())
        {
            var objContact = new Contact { FirstName = "FICTIONAL " + enStatus, Status = enStatus }; objCrm.Save(objContact); lstContacts.Add(objContact);
            using var dbStatus = new CrmDbContext(objDatabase, enStatus);
            Check(dbStatus.ReadAuthorizedContacts(objAdmin).Single().Id == objContact.Id, "EF lifecycle query isolates " + enStatus);
        }
        var objOrganization = new Organization { Name = "FICTIONAL restricted organization" }; objCrm.Save(objOrganization);
        var objContactActive = lstContacts[0]; objContactActive.lstOrganizationIds = [objOrganization.Id]; objCrm.Save(objContactActive);
        var lngEditor = objUsers.CreateUser("ef-editor", secPassword, UserRole.Editor);
        objUsers.SetRecordAccess(lngEditor, RecordKind.Contact, objContactActive.Id, true, true);
        using var objEditor = new AuthService(objDatabase).SignIn("ef-editor", secPassword);
        Check(dbContext.ReadAuthorizedContacts(objEditor).Count == 0, "EF record grant alone does not authorize CUI");
        objUsers.SetCuiAuthorization(lngEditor, true);
        Check(dbContext.ReadAuthorizedContacts(objEditor).Single().lngOrganizationId is null, "EF contact cannot disclose an ungranted organization ID");
        Check(dbContext.ReadAuthorizedOrganizations(objEditor).Count == 0, "EF CUI authorization does not replace organization grants");
        var objVisible = dbContext.ReadAuthorizedContacts(objEditor).Single();
        objVisible.Phone = "+1 (202) 555-0142"; dbContext.dbContacts.Update(objVisible);
        dbContext.SaveAuthorizedChanges(objEditor);
        Check(objCrm.Contacts().Single().Phone == "+1 (202) 555-0142", "EF authorized contact edit preserves record scope");
        Check(objCrm.Contacts().Single().lngOrganizationId == objOrganization.Id, "EF edit preserves the masked restricted organization reference in storage");
        Check(new CrmService(objDatabase, objEditor).Contacts().Single().lstOrganizationIds.Count == 0, "Service cannot disclose ungranted relationship IDs");
        objVisible = new CrmService(objDatabase, objEditor).Contacts().Single(); objVisible.Phone = "+1 (202) 555-0143";
        new CrmService(objDatabase, objEditor).Save(objVisible);
        Check(objCrm.Contacts().Single().lstOrganizationIds.Contains(objOrganization.Id), "Editing a visible contact retains its hidden organization links");
        var objInvalid = new Contact { FirstName = "Invalid", strClassification = null! }; dbContext.dbContacts.Add(objInvalid);
        Reject<ArgumentException>(() => dbContext.SaveAuthorizedChanges(objAdmin), "EF rejects a missing CUI marking"); dbContext.ChangeTracker.Clear();
        var objArchived = dbContext.ReadAuthorizedContacts(objAdmin).Single(); objArchived.Archived = true;
        dbContext.dbContacts.Update(objArchived);
        Reject<InvalidOperationException>(() => dbContext.SaveAuthorizedChanges(objAdmin), "EF generic updates cannot replace the audited retention command"); dbContext.ChangeTracker.Clear();
        Reject<UnauthorizedAccessException>(() => dbContext.dbContacts.IgnoreQueryFilters().ToArray(), "Direct EF reads cannot remove classification filters");
        Reject<UnauthorizedAccessException>(() => dbContext.dbContacts.ExecuteDelete(), "EF bulk deletion remains blocked");
        var lngViewer = objUsers.CreateUser("ef-viewer", secPassword, UserRole.Viewer);
        objUsers.SetCuiAuthorization(lngViewer, true);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContactActive.Id, true, true);
        using var objViewer = new AuthService(objDatabase).SignIn("ef-viewer", secPassword);
        var objReadOnly = dbContext.ReadAuthorizedContacts(objViewer).Single();
        objReadOnly.Phone = "+1 (202) 555-0199"; dbContext.dbContacts.Update(objReadOnly);
        Reject<UnauthorizedAccessException>(() => dbContext.SaveAuthorizedChanges(objViewer), "EF Viewer role overrides a writable grant and CUI authorization"); dbContext.ChangeTracker.Clear();
        objUsers.SetCuiAuthorization(lngEditor, false);
        Reject<UnauthorizedAccessException>(() => dbContext.ReadAuthorizedContacts(objEditor), "EF reused context observes revoked CUI authorization/session");
        var lngNoCui = objUsers.CreateUser("ef-no-cui", secPassword, UserRole.Editor);
        objUsers.SetRecordAccess(lngNoCui, RecordKind.Contact, objContactActive.Id, true, true);
        using var objNoCui = new AuthService(objDatabase).SignIn("ef-no-cui", secPassword);
        var arrDenied = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        Reject<UnauthorizedAccessException>(() => new ProfileImageService(objDatabase, objNoCui).SaveContactPicture(objContactActive.Id, arrDenied), "Image ingestion cannot bypass CUI authorization");
        Check(arrDenied.All(bytValue => bytValue == 0), "CUI image denial wipes imported buffers");
        Check(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM profile_images") == 0, "Denied image authorization writes no BLOB");
        objCrm.SetArchived(RecordKind.Contact, objContactActive.Id, objCrm.Contacts().Single().Revision, true);
        Check(dbContext.ReadAuthorizedContacts(objAdmin).Count == 0, "EF excludes archived and lifecycle-inactive records");
        SecurityPolicyChecks.Sql(objDatabase, "PRAGMA ignore_check_constraints=ON; UPDATE contacts SET classification='UNKNOWN' WHERE id=2; PRAGMA ignore_check_constraints=OFF;");
        using var dbInactive = new CrmDbContext(objDatabase, RecordStatus.Inactive);
        Check(dbInactive.ReadAuthorizedContacts(objAdmin).Count == 0, "EF excludes invalid stored classification even for administrators");
    }
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception => SecurityPolicyChecks.Reject<T>(actCheck, strMessage);
}
