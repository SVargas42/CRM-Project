using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class TokenEfWriteChecks
{
    internal static void Run()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL EF dual write fixture!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("ef-links", secPassword);
        new UserAdministrationService(objDatabase, objAdmin).SetCuiAuthorization(objAdmin.UserId, true);
        using var dbContext = new CrmDbContext(objDatabase);
        var objContact = new Contact { FirstName = "Fixture", Skills = "Équipe", strTags = "Common" };
        var objOrganization = new Organization { Name = "Fixture organization", Tags = "common" };
        dbContext.dbContacts.Add(objContact); dbContext.dbOrganizations.Add(objOrganization); dbContext.SaveAuthorizedChanges(objAdmin);
        Check(Scalar(objDatabase, "SELECT count(*) FROM contact_skills WHERE status='Active'") == 1
            && Scalar(objDatabase, "SELECT count(*) FROM tags") == 1 && Scalar(objDatabase, "SELECT count(*) FROM organization_tags WHERE status='Active'") == 1,
            "Authorized EF creation dual-writes both parent kinds without changing mappings");
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objSaved = dbContext.ReadAuthorizedContacts(objAdmin).Single(); objSaved.Skills = "équipe";
        dbContext.dbContacts.Update(objSaved); dbContext.SaveAuthorizedChanges(objAdmin);
        Check(Scalar(objDatabase, "SELECT count(*) FROM skills") == 1, "Authorized EF update reuses Unicode word identity");
        var lngAudit = Scalar(objDatabase, "SELECT count(*) FROM audit_log");
        objSaved = dbContext.ReadAuthorizedContacts(objAdmin).Single(); objSaved.Skills = "Replacement"; objSaved.strTags = "Other";
        lngAudit = Scalar(objDatabase, "SELECT count(*) FROM audit_log");
        CuiCategoryChecks.Sql(objDatabase, "CREATE TRIGGER fixture_ef_link_failure BEFORE INSERT ON contact_tags BEGIN SELECT RAISE(ABORT,'PRIVATE_EF_VALUE'); END");
        dbContext.dbContacts.Update(objSaved); var bRejected = false;
        try { dbContext.SaveAuthorizedChanges(objAdmin); }
        catch (InvalidOperationException objError) { bRejected = objError.Message == "Link synchronization failed: contacts, record ID 1." && objError.InnerException is null; }
        Check(bRejected && Scalar(objDatabase, "SELECT count(*) FROM skills") == 1
            && Scalar(objDatabase, "SELECT count(*) FROM audit_log") == lngAudit,
            "EF link failure rolls back parent transaction, word insert and audit");
        dbContext.ChangeTracker.Clear();
        var lstMismatch = objCrm.CheckTokenConsistency();
        try { Check(lstMismatch.Count == 0, "EF rollback leaves source and Active links consistent"); }
        finally { CuiMemoryPolicy.Release(lstMismatch); }
    }
    private static long Scalar(Database objDatabase, string strSql)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = Database.Command(dbConnection, strSql); return Convert.ToInt64(cmdSql.ExecuteScalar()); }
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
}
