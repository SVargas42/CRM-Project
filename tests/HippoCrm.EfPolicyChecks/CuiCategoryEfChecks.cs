using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

internal static class CuiCategoryEfChecks
{
    internal static void Run()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL CTI EF and image policy!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("cti-ef-admin", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        objUsers.SetCuiRecipientKind(objAdmin.UserId, CuiRecipientKind.DoD, "FICTIONAL verified DoD identity");
        foreach (var strLetter in new[] { "B", "C", "F" }) objUsers.SetCuiPolicyAccess(objAdmin.UserId, CuiPolicy.strCtiMarking, strLetter, true, "FICTIONAL authority " + strLetter);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objContact = CuiPolicy.Apply(new Contact { FirstName = "FICTIONAL CTI EF contact" }, CuiCategoryChecks.Designation("B"));
        var objOrganization = CuiPolicy.Apply(new Organization { Name = "FICTIONAL CTI EF organization" }, CuiCategoryChecks.Designation("C"));
        using var dbContext = new CrmDbContext(objDatabase);
        dbContext.dbContacts.Add(objContact); dbContext.dbOrganizations.Add(objOrganization); dbContext.SaveAuthorizedChanges(objAdmin);
        Check(dbContext.ReadAuthorizedContacts(objAdmin).Single().objCuiDesignation == CuiCategoryChecks.Designation("B"), "EF persists inherited contact CTI fields");
        Check(dbContext.ReadAuthorizedOrganizations(objAdmin).Single().objCuiDesignation == CuiCategoryChecks.Designation("C"), "EF persists independent organization CTI fields");
        var lngEditor = objUsers.CreateUser("cti-ef-editor", secPassword, UserRole.Editor); objUsers.SetCuiAuthorization(lngEditor, true);
        objUsers.SetCuiRecipientKind(lngEditor, CuiRecipientKind.UsGovernment, "FICTIONAL verified Government identity");
        objUsers.SetRecordAccess(lngEditor, RecordKind.Contact, objContact.Id, true, true);
        objUsers.SetRecordAccess(lngEditor, RecordKind.Organization, objOrganization.Id, true, true);
        using var objEditor = new AuthService(objDatabase).SignIn("cti-ef-editor", secPassword);
        Check(dbContext.ReadAuthorizedContacts(objEditor).Count == 0 && dbContext.ReadAuthorizedOrganizations(objEditor).Count == 0, "EF record grants cannot replace category approval");
        objUsers.SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "B", true, "FICTIONAL B approval");
        Check(dbContext.ReadAuthorizedContacts(objEditor).Count == 1 && dbContext.ReadAuthorizedOrganizations(objEditor).Count == 0, "EF scopes distributions independently within a reused context");
        // An unverified recipient cannot inherit previously stored CTI approvals.
        CuiCategoryChecks.Sql(objDatabase, "UPDATE users SET cui_recipient_kind='Unverified' WHERE id=$user", ("$user", lngEditor));
        Check(dbContext.ReadAuthorizedContacts(objEditor).Count == 0, "EF checks live recipient eligibility rather than trusting stored category approvals alone");
        CuiCategoryChecks.Sql(objDatabase, "UPDATE users SET cui_recipient_kind='UsGovernment' WHERE id=$user", ("$user", lngEditor));
        var objDraft = dbContext.ReadAuthorizedContacts(objEditor).Single(); CuiPolicy.Apply(objDraft, new CuiDesignation(CuiPolicy.strMarking));
        dbContext.dbContacts.Update(objDraft);
        Reject<InvalidOperationException>(() => dbContext.SaveAuthorizedChanges(objEditor), "Disconnected EF update cannot downgrade CTI policy"); dbContext.ChangeTracker.Clear();
        var lngOtherAdmin = objUsers.CreateUser("cti-basic-admin", secPassword, UserRole.Administrator); objUsers.SetCuiAuthorization(lngOtherAdmin, true);
        using var objBasicAdmin = new AuthService(objDatabase).SignIn("cti-basic-admin", secPassword);
        Check(dbContext.ReadAuthorizedContacts(objBasicAdmin).Count == 0, "EF Administrator role does not bypass category policy");
        dbContext.dbContacts.Add(CuiPolicy.Apply(new Contact { FirstName = "Denied CTI" }, CuiCategoryChecks.Designation("B")));
        Reject<UnauthorizedAccessException>(() => dbContext.SaveAuthorizedChanges(objBasicAdmin), "EF CTI creation requires the target policy approval"); dbContext.ChangeTracker.Clear();

        var objImages = new ProfileImageService(objDatabase, objAdmin);
        var arrImported = Picture(); objImages.SaveContactPicture(objContact.Id, arrImported);
        Check(arrImported.All(bytValue => bytValue == 0), "CTI ingestion still wipes original image bytes");
        objImages.SaveContactPicture(objContact.Id, Picture(40)); objImages.SaveOrganizationLogo(objOrganization.Id, Picture());
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images WHERE entity='Contact' AND cui_category='CUI//SP-CTI' AND distribution_statement='B'") == 2, "Active and retained contact images inherit the parent CTI policy");
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images WHERE entity='Organization' AND distribution_statement='C'") == 1, "Organization image inherits its independent policy");
        using (var objImage = new ProfileImageService(objDatabase, objEditor).ReadContactPicture(objContact.Id))
        {
            Check(objImage is not null && objImage.objCuiDesignation == CuiCategoryChecks.Designation("B") && objImage.arrPng.Length > 0
                && objImage.lngUserId == objAdmin.UserId && !string.IsNullOrWhiteSpace(objImage.strCreatedAt), "Image read checks record/category policy and retains attribution before transferring BLOB ownership");
            var arrOwned = objImage!.arrPng; objImage.Dispose();
            Check(arrOwned.All(bytValue => bytValue == 0), "Disposing a returned image wipes its original BLOB buffer");
        }
        Reject<UnauthorizedAccessException>(() => new ProfileImageService(objDatabase, objEditor).ReadOrganizationLogo(objOrganization.Id), "Image retrieval cannot cross from approved B into unapproved C");
        var arrDenied = Picture();
        Reject<UnauthorizedAccessException>(() => new ProfileImageService(objDatabase, objBasicAdmin).SaveContactPicture(objContact.Id, arrDenied), "Administrator image ingestion cannot bypass CTI approval");
        Check(arrDenied.All(bytValue => bytValue == 0), "Category-denied image ingestion wipes imported bytes");
        Reject<SqliteException>(() => CuiCategoryChecks.Sql(objDatabase, "UPDATE profile_images SET cui_category='CUI',distribution_statement='',controlling_office='',determination_date='',distribution_notice='' WHERE entity='Contact'"), "SQLite prevents image downgrade independent of its CTI parent");
        Check(dbContext.Model.FindEntityType(typeof(ProfileImage))!.FindProperty(nameof(ProfileImage.strCuiCategory))!.GetColumnName() == "cui_category", "EF maps image category labels into SQLite");
        Reject<UnauthorizedAccessException>(() => dbContext.dbProfileImages.IgnoreQueryFilters().ToArray(), "Direct EF image query cannot bypass the authorized gateway");
        var objCurrent = objCrm.ContactRecord(objContact.Id);
        CuiCategoryChecks.Sql(objDatabase, "CREATE TRIGGER fixture_image_policy_audit BEFORE INSERT ON audit_log WHEN NEW.action='CuiDesignationChanged' BEGIN SELECT RAISE(ABORT,'FICTIONAL audit failure'); END");
        Reject<SqliteException>(() => objUsers.SetRecordCuiDesignation(RecordKind.Contact, objCurrent.Id, objCurrent.Revision, CuiCategoryChecks.Designation("F")), "Parent/image reclassification fails closed on audit failure");
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images WHERE entity='Contact' AND distribution_statement='B'") == 2, "Failed designation change rolls back every inherited image policy");
        CuiCategoryChecks.Sql(objDatabase, "DROP TRIGGER fixture_image_policy_audit");
        objUsers.SetRecordCuiDesignation(RecordKind.Contact, objCurrent.Id, objCurrent.Revision, CuiCategoryChecks.Designation("F"));
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images WHERE entity='Contact' AND distribution_statement='F'") == 2, "Authorized parent redesignation propagates atomically to all retained image versions");
        Check(dbContext.ReadAuthorizedContacts(objEditor).Count == 0, "EF observes changed parent policy without reusing a prior actor's model");
        Reject<UnauthorizedAccessException>(() => new ProfileImageService(objDatabase, objEditor).ReadContactPicture(objContact.Id), "Formerly approved image reader loses access after parent redesignation");
        objUsers.SetCuiPolicyAccess(lngEditor, CuiPolicy.strCtiMarking, "F", true, "FICTIONAL specific controlling office direction");
        using var objApprovedImage = new ProfileImageService(objDatabase, objEditor).ReadContactPicture(objContact.Id);
        Check(objApprovedImage!.strDistributionStatement == "F", "Explicit F approval restores only the authorized image distribution");
    }

    private static byte[] Picture(byte bytRed = 10)
    {
        using var imgFixture = new Image<Rgba32>(2, 2, new Rgba32(bytRed, 20, 30));
        using var stmFixture = new MemoryStream(); imgFixture.SaveAsPng(stmFixture); return stmFixture.ToArray();
    }
    private static long Scalar(Database objDatabase, string strSql) => SecurityPolicyChecks.Scalar(objDatabase, strSql);
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception => SecurityPolicyChecks.Reject<T>(actCheck, strMessage);
}
