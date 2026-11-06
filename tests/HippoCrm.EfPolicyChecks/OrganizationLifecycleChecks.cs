using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class OrganizationLifecycleChecks
{
    internal static void Run()
    {
        var iChecks = 0;
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL organization lifecycle!");
        var objAuth = new AuthService(objDatabase);
        using var objAdmin = objAuth.CreateAdministrator("organization-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objRestored = new Organization { Name = "FICTIONAL restored", Tags = "RestoredTag", Services = "RestoredService" }; objCrm.Save(objRestored);
        var objArchived = new Organization { Name = "FICTIONAL archived", Tags = "ArchivedTag", Services = "ArchivedService" }; objCrm.Save(objArchived);
        var objHidden = new Organization { Name = "FICTIONAL hidden", Tags = "HiddenTag", Services = "HiddenService" }; objCrm.Save(objHidden);
        var objHiddenArchived = new Organization { Name = "FICTIONAL hidden archived" }; objCrm.Save(objHiddenArchived);
        var objContact = new Contact { FirstName = "FICTIONAL placement", Skills = "ContactSkill", lstOrganizationIds = [objRestored.Id] }; objCrm.Save(objContact);
        Archive(objRestored, true);
        Check(!objCrm.Contacts().Single().lstOrganizationIds.Contains(objRestored.Id), "Archived organization is excluded from active memberships");
        Archive(objRestored, false); Archive(objArchived, true); Archive(objHiddenArchived, true);
        var objStored = objCrm.OrganizationRecord(objRestored.Id);
        Check(!objStored.Archived && objStored.Status == RecordStatus.Inactive, "Restore preserves valid unarchived/Inactive storage and passes retention triggers");
        Check(objCrm.Organizations().Any(objOrg => objOrg.Id == objRestored.Id) && !objCrm.Organizations().Any(objOrg => objOrg.Id == objArchived.Id), "Default Active service view includes restored and excludes archived organizations");
        Check(objCrm.Organizations(bArchived: true).Any(objOrg => objOrg.Id == objArchived.Id), "Archived service choice includes archived organizations");
        Check(objCrm.AllOrganizations().Any(objOrg => objOrg.Id == objRestored.Id), "Restored organization remains an editor choice");
        Check(objCrm.ContactEditor(objContact.Id).Organizations.Any(objOrg => objOrg.Id == objRestored.Id), "Contact editor employer choices include restored organizations");
        Check(objCrm.Contacts().Single().lstOrganizationIds.Contains(objRestored.Id) && objCrm.ContactRecord(objContact.Id).lstOrganizationIds.Contains(objRestored.Id), "Restored memberships appear in list and detail reads");
        Check(objCrm.Dashboard().Organizations == 2 && objCrm.Dashboard(RecordStatus.Deceased).Organizations == 2, "Organization counts use archive state independently of contact status");
        var objCatalog = objCrm.TokenChoices();
        try { Check(objCatalog.Services.Contains("RestoredService") && objCatalog.Tags.Contains("RestoredTag") && !objCatalog.Services.Contains("ArchivedService") && !objCatalog.Tags.Contains("ArchivedTag"), "Organization catalogs include restored and exclude archived data"); }
        finally { CuiMemoryPolicy.Release(objCatalog); }
        objCrm.AddEmployment(objContact.Id, objRestored.Id, "FICTIONAL role", new DateOnly(2020, 1, 1), new DateOnly(2020, 2, 1));
        Check(objCrm.EmploymentHistory(objContact.Id).Count == 1, "Employment accepts a restored organization");
        var bRejected = false;
        try { objCrm.AddEmployment(objContact.Id, objArchived.Id, "Denied", new DateOnly(2021, 1, 1), null); }
        catch (ArgumentException) { bRejected = true; }
        Check(bRejected && objCrm.EmploymentHistory(objContact.Id).Count == 1, "Employment rejects archived organizations without writing a placement");
        using (var dbActive = new CrmDbContext(objDatabase))
            Check(dbActive.ReadAuthorizedOrganizations(objAdmin).Any(objOrg => objOrg.Id == objRestored.Id), "EF default organization filter includes restored Inactive storage");
        using (var dbArchived = new CrmDbContext(objDatabase, bOrganizationsArchived: true))
            Check(dbArchived.ReadAuthorizedOrganizations(objAdmin).All(objOrg => objOrg.Archived), "EF archive selection is independent of contact status setting");
        var lngViewer = objUsers.CreateUser("organization-viewer", secPassword, UserRole.Viewer);
        objUsers.SetCuiAuthorization(lngViewer, true);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objRestored.Id, true, false);
        using var objViewer = objAuth.SignIn("organization-viewer", secPassword);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        Check(objViewerCrm.Organizations().Single().Id == objRestored.Id && objViewerCrm.Organizations(bArchived: true).Count == 0, "Missing grants hide organizations in both lifecycle choices");
        using (var dbActive = new CrmDbContext(objDatabase))
            Check(dbActive.ReadAuthorizedOrganizations(objViewer).Single().Id == objRestored.Id, "EF Active query retains record authorization after restore");
        using (var dbArchived = new CrmDbContext(objDatabase, bOrganizationsArchived: true))
            Check(dbArchived.ReadAuthorizedOrganizations(objViewer).Count == 0, "EF Archived query cannot reveal ungranted records");
        foreach (var enStatus in Enum.GetValues<RecordStatus>())
        {
            var objStatusContact = new Contact { FirstName = "FICTIONAL " + enStatus, Status = enStatus }; objCrm.Save(objStatusContact);
            using var dbStatus = new CrmDbContext(objDatabase, enStatus);
            Check(objCrm.Contacts(enStatus: enStatus).All(objRow => objRow.Status == enStatus) && dbStatus.ReadAuthorizedContacts(objAdmin).All(objRow => objRow.Status == enStatus), "Contact SQL and EF filters still isolate " + enStatus);
        }
        Console.WriteLine($"{iChecks} organization lifecycle checks passed.");
        void Archive(Organization objOrganization, bool bArchive)
        {
            var objCurrent = objCrm.OrganizationRecord(objOrganization.Id);
            objCrm.SetArchived(RecordKind.Organization, objCurrent.Id, objCurrent.Revision, bArchive);
            CuiMemoryPolicy.Release(objCurrent);
        }
        void Check(bool bCondition, string strDescription)
        { if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription); iChecks++; Console.WriteLine("PASS " + strDescription); }
    }
}