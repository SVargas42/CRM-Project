using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class LookupPaginationChecks
{
    internal static void Run()
    {
        var strTestRoot = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        var strDirectory = Path.Combine(strTestRoot, "HippoCrmLookupPagination", Guid.NewGuid().ToString("N"), "UiDevelopment");
        using var objKeys = new EphemeralKeyStore();
        using var objDatabase = Database.OpenUiDevelopmentWorkspace(strDirectory, objKeys);
        var objAuthentication = new AuthService(objDatabase);
        using var objSession = objAuthentication.CreateAdministratorForTest("fixture", "Fictional pagination password!");
        new UserAdministrationService(objDatabase, objSession).SetCuiAuthorization(objSession.UserId, true);
        var objCrm = new CrmService(objDatabase, objSession);
        var iChecks = 0;
        try
        {
            foreach (var iIndex in Enumerable.Range(1, CuiMemoryPolicy.iPageSize))
            {
                objCrm.Save(new Contact { FirstName = $"ContactPage-{iIndex:000}", LastName = "Fixture" });
                objCrm.Save(new Organization { Name = $"OrganizationPage-{iIndex:000}" });
            }

            var lstContacts = objCrm.Contacts("ContactPage-", iLimit: CuiMemoryPolicy.iPageSize);
            Check(lstContacts.Count == CuiMemoryPolicy.iPageSize, "Contacts returns a complete page of exactly 128 results");
            Check(!objCrm.HasMoreContacts("ContactPage-", iOffset: CuiMemoryPolicy.iPageSize),
                "Contacts reports no next page when exactly 128 matches exist");
            objCrm.Save(new Contact { FirstName = "ContactPage-129", LastName = "Fixture" });
            Check(objCrm.HasMoreContacts("ContactPage-", iOffset: CuiMemoryPolicy.iPageSize),
                "Contacts reports a next page when a 129th match exists");
            Check(objCrm.Contacts("ContactPage-", iLimit: CuiMemoryPolicy.iPageSize,
                iOffset: CuiMemoryPolicy.iPageSize).Count == 1
                && !objCrm.HasMoreContacts("ContactPage-", iOffset: 2 * CuiMemoryPolicy.iPageSize),
                "Contacts reports the one-record final page and no further page");

            var lstOrganizations = objCrm.Organizations("OrganizationPage-", iLimit: CuiMemoryPolicy.iPageSize);
            Check(lstOrganizations.Count == CuiMemoryPolicy.iPageSize, "Organizations returns a complete page of exactly 128 results");
            Check(!objCrm.HasMoreOrganizations("OrganizationPage-", iOffset: CuiMemoryPolicy.iPageSize),
                "Organizations reports no next page when exactly 128 matches exist");
            objCrm.Save(new Organization { Name = "OrganizationPage-129" });
            Check(objCrm.HasMoreOrganizations("OrganizationPage-", iOffset: CuiMemoryPolicy.iPageSize),
                "Organizations reports a next page when a 129th match exists");
            Check(objCrm.Organizations("OrganizationPage-", iLimit: CuiMemoryPolicy.iPageSize,
                iOffset: CuiMemoryPolicy.iPageSize).Count == 1
                && !objCrm.HasMoreOrganizations("OrganizationPage-", iOffset: 2 * CuiMemoryPolicy.iPageSize),
                "Organizations reports the one-record final page and no further page");

            Check(!objCrm.HasMoreContacts("No pagination matches", iOffset: 0)
                && !objCrm.HasMoreOrganizations("No pagination matches", iOffset: 0),
                "Empty lookup results do not advertise a next page");
            Console.WriteLine($"{iChecks} lookup pagination checks passed.");
        }
        finally
        {
            objAuthentication.SignOut(objSession);
            if (Directory.Exists(strDirectory)) Directory.Delete(strDirectory, recursive: true);
        }

        void Check(bool bCondition, string strDescription)
        {
            if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
            iChecks++; Console.WriteLine("PASS " + strDescription);
        }
    }
}
