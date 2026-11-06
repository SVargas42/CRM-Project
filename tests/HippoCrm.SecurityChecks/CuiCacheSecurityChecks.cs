using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class CuiCacheSecurityChecks
{
    internal static void Run()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL bounded lookup policy!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("bounded-admin", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        for (var iRow = 0; iRow < CuiMemoryPolicy.iPageSize + 2; iRow++)
            objCrm.Save(new Contact { FirstName = "FICTIONAL " + iRow, Address = new string('x', 900), Spouse = "Never project this field", Status = (RecordStatus)(iRow % 4) });
        var objFirst = objCrm.LookupPage(); var objSecond = objCrm.LookupPage(CuiMemoryPolicy.iPageSize);
        Check(objFirst.lstContacts.Count == 128 && objSecond.lstContacts.Count == 2, "Lookup pages are SQL-bounded to 128 with a stable continuation offset");
        Check(!objFirst.lstContacts.Select(objContact => objContact.Id).Intersect(objSecond.lstContacts.Select(objContact => objContact.Id)).Any(), "Successive lookup pages do not duplicate records");
        Check(objFirst.lstContacts.All(objContact => objContact.Address.Length == CuiMemoryPolicy.iMaximumFieldLength && objContact.Spouse == ""), "Lookup projects bounded fields and omits family data");
        Check(Enum.GetValues<RecordStatus>().All(enStatus => objFirst.lstContacts.Any(objContact => objContact.Status == enStatus)), "Paged lookup retains the four validated lifecycle states");
        Reject<ArgumentOutOfRangeException>(() => objCrm.LookupPage(-1), "Negative page offset is rejected");
        using var objCanceled = new CancellationTokenSource(); objCanceled.Cancel();
        Reject<OperationCanceledException>(() => objCrm.LookupPage(ctCancellation: objCanceled.Token), "Canceled lookup returns no page");
        var lngEpoch = objCrm.CuiEpoch(); _ = objCrm.LookupPage();
        Check(objCrm.CuiEpoch() == lngEpoch, "Audited reads do not renew or change the permission epoch");
        var lngViewer = objUsers.CreateUser("bounded-viewer", secPassword, UserRole.Viewer);
        using var objViewer = new AuthService(objDatabase).SignIn("bounded-viewer", secPassword);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objFirst.lstContacts[0].Id, true, false);
        Check(objViewerCrm.LookupPage().lstContacts.Count == 0, "Record grant alone cannot authorize paged CUI projections");
        objUsers.SetCuiAuthorization(lngViewer, true);
        Check(objViewerCrm.LookupPage().lstContacts.Single().Id == objFirst.lstContacts[0].Id, "Paged CUI projection still requires the exact record grant");
        Check(objViewerCrm.ContactRecord(objFirst.lstContacts[0].Id).Spouse == "Never project this field", "Authorized detail lookup restores full fields before editing a minimal projection");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.ContactRecord(objFirst.lstContacts[1].Id), "Detail lookup cannot use a projection ID to bypass record grants");
        lngEpoch = objViewerCrm.CuiEpoch();
        using var objLease = objViewer.CreateCuiMemoryLease(lngEpoch);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objFirst.lstContacts[0].Id, false, false);
        Check(objViewerCrm.CuiEpoch() > lngEpoch && objLease.NeedsRefresh, "Committed grant revocation changes the epoch and requires cache revalidation");
        Check(objViewerCrm.LookupPage().lstContacts.Count == 0, "Requery after revocation cannot return the old projection");
        Check(objCrm.Contacts(enStatus: null, iLimit: 128, iOffset: 128).Count == 2, "Contact lookup continuation is applied in SQL after lifecycle authorization");
        Check(objCrm.Contacts(enStatus: null, iLimit: 128).All(objContact => objContact.Spouse == "" && objContact.Address.Length == 512),
            "Primary contact browsing also omits family fields and bounds projected text");
        var objOrganization = new Organization { Name = "FICTIONAL linked page", Services = new string('x', 900) }; objCrm.Save(objOrganization);
        Check(objCrm.Organizations(iLimit: 128).Single().Services.Length == 512 && objCrm.OrganizationRecord(objOrganization.Id).Services.Length == 900,
            "Organization browsing shortens text but detail reload preserves the full stored value");
        objCrm.Save(new Contact { FirstName = "FICTIONAL beyond first page", lstOrganizationIds = [objOrganization.Id] });
        Check(objCrm.LookupPage(lngOrganizationId: objOrganization.Id).lstContacts.Single().FirstName == "FICTIONAL beyond first page",
            "Organization-related projections filter in SQL before paging rather than scanning the first global page");
        Reject<UnauthorizedAccessException>(() => objViewerCrm.LookupPage(lngOrganizationId: objOrganization.Id), "Related-page lookup requires organization authorization too");
        CuiMemoryPolicy.Release(objFirst); CuiMemoryPolicy.Release(objSecond);
        Check(objFirst.lstContacts.Count == 0, "Owned projection collections release their record references");
        using var objPasswordLease = objAdmin.CreateCuiMemoryLease(objCrm.CuiEpoch());
        new AuthService(objDatabase).ChangePassword(objAdmin, secPassword, secPassword);
        Check(objPasswordLease.NeedsRefresh, "Committed password change marks CUI authorization stale immediately");
        Reject<UnauthorizedAccessException>(() => objCrm.ValidateCuiAccess(null, false, null, false), "Password-revoked credentials cannot renew CUI authorization");
    }
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception => SecurityPolicyChecks.Reject<T>(actCheck, strMessage);
}
