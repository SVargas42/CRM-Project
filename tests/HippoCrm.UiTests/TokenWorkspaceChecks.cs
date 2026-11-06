#if DEBUG
using System.Reflection;
using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class TokenWorkspaceChecks
{
    internal static void Run()
    {
        using (var objMemory = Database.CreateUiDevelopmentWorkspace()) Verify(objMemory, "memory");
        // Encrypted fictional fixture only; never use the application's default Debug/production paths.
        var strRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "token-workspace-checks", Guid.NewGuid().ToString("N"), "UiDevelopment");
        using var objKeys = new EphemeralKeyStore();
        using (var objPersistent = Database.OpenUiDevelopmentWorkspace(strRoot, objKeys)) Verify(objPersistent, "persistent");
    }
    private static void Verify(Database objDatabase, string strWorkspace)
    {
        using var secPassword = TestPasswords.Secret("FICTIONAL workspace link fixture!");
        var objAuth = new AuthService(objDatabase);
        using var objAdmin = objAuth.CreateAdministrator("workspace-links", secPassword);
        new UserAdministrationService(objDatabase, objAdmin).SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objContact = new Contact { FirstName = "Fixture", Skills = "Équipe", strTags = "Shared" }; objCrm.Save(objContact);
        objCrm.Save(new Organization { Name = "Fixture", Tags = "shared", Services = "SERVICE_ONLY" });
        objContact.Skills = "équipe"; objCrm.Save(objContact);
        using var dbConnection = (SqliteConnection)typeof(Database).GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(objDatabase, null)!;
        using var cmdCheck = dbConnection.CreateCommand();
        cmdCheck.CommandText = "SELECT (SELECT count(*) FROM skills)=1 AND (SELECT count(*) FROM tags)=1 AND (SELECT count(*) FROM contact_skills WHERE status='Active')=1 AND (SELECT count(*) FROM contact_tags WHERE status='Active')=1 AND (SELECT count(*) FROM organization_tags WHERE status='Active')=1";
        if (Convert.ToInt64(cmdCheck.ExecuteScalar()) != 1) throw new InvalidOperationException("FAIL Debug " + strWorkspace + " workspace dual-write");
        Console.WriteLine("PASS Debug " + strWorkspace + " workspace dual-writes skills and shared tags");
    }
}
#endif
