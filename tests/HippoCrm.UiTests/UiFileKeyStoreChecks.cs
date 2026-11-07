#if DEBUG
using System.Reflection;
using System.Security.Cryptography;
using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class UiFileKeyStoreChecks
{
    private static int iPassed;
    internal static void Run()
    {
        var strParent = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-file-key-checks", Guid.NewGuid().ToString("N"));
        var strRoot = Path.Combine(strParent, "UiDevelopment");
        Directory.CreateDirectory(strRoot);
        var strDatabase = Path.Combine(strRoot, "ui-development.db");
        var strMaster = strDatabase + ".audit-master";
        var mthFactory = typeof(Database).GetMethod("CreateUiDevelopmentKeyStore", BindingFlags.Static | BindingFlags.NonPublic)!;
        IDatabaseKeyStore Select(bool bMac, bool bWindows, string? strDirectory = null) => (IDatabaseKeyStore)mthFactory.Invoke(null, [strDirectory ?? strRoot, bMac, bWindows])!;
        var objKeys = Select(true, false);
        Check(objKeys.GetType().Name == "UiDevelopmentFileKeyStore", "Simulated macOS Debug selects the file store, never a Keychain adapter");
        Check(Select(false, true) is WindowsCredentialKeyStore, "Windows Debug retains its credential adapter without opening credentials");
        objKeys.EnsureKey(strDatabase, true); objKeys.EnsureKey(strMaster, true);
        var arrFirst = objKeys.ReadKey(strDatabase); var arrSecond = Select(true, false).ReadKey(strDatabase); var arrMaster = objKeys.ReadKey(strMaster);
        try
        {
            objKeys.EnsureKey(strDatabase, true);
            var arrAgain = objKeys.ReadKey(strDatabase);
            try { Check(arrFirst.Length == 32 && arrFirst.SequenceEqual(arrSecond) && arrFirst.SequenceEqual(arrAgain) && !arrFirst.SequenceEqual(arrMaster), "Each identity has its own 32-byte key, reused across store instances and Ensure calls"); }
            finally { CryptographicOperations.ZeroMemory(arrAgain); }
        }
        finally { CryptographicOperations.ZeroMemory(arrFirst); CryptographicOperations.ZeroMemory(arrSecond); CryptographicOperations.ZeroMemory(arrMaster); }
        if (OperatingSystem.IsWindows()) Console.WriteLine("SKIP Unix owner-only mode assertion on Windows; atomic publication and recovery checks still run.");
        else
        {
            foreach (var strFile in Directory.EnumerateFiles(strRoot))
                Check(File.GetUnixFileMode(strFile) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Key file is owner-read/write only");
        }
        Check(Directory.EnumerateFiles(strRoot).Select(Path.GetFileName).Order().SequenceEqual(new[] { "audit-master.key", "workspace.key" }), "Keys publish atomically in separate files with no temporary leftovers");
        Reject<AuditRecoveryRequiredException>(() => objKeys.EnsureKey(Path.Combine(strParent, "outside.db"), true), "Foreign key identity cannot create a file outside the dedicated workspace");
        Check(!File.Exists(Path.Combine(strParent, "outside.db")) && Directory.EnumerateFiles(strParent, "*", SearchOption.AllDirectories).All(strFile => Path.GetDirectoryName(strFile) == strRoot), "All generated key files remain inside the workspace folder");

        var strBadRoot = Path.Combine(strParent, "invalid", "UiDevelopment"); Directory.CreateDirectory(strBadRoot);
        var objBad = Select(true, false, strBadRoot); var strBadIdentity = Path.Combine(strBadRoot, "ui-development.db");
        // Malformed fixture bytes are not a usable key or database.
        File.WriteAllBytes(Path.Combine(strBadRoot, "workspace.key"), [0]);
        Reject<AuditRecoveryRequiredException>(() => objBad.ReadKey(strBadIdentity), "Wrong-length key is rejected with the existing neutral recovery message");
        foreach (var strExisting in new[] { "ui-development.db", "checkpoint.json" })
        {
            var strMissingRoot = Path.Combine(strParent, strExisting.Replace('.', '-'), "UiDevelopment"); Directory.CreateDirectory(strMissingRoot);
            File.WriteAllBytes(Path.Combine(strMissingRoot, strExisting), []);
            var objMissing = Select(true, false, strMissingRoot);
            Reject<AuditRecoveryRequiredException>(() => objMissing.EnsureKey(Path.Combine(strMissingRoot, "ui-development.db"), true), "Existing database or checkpoint cannot cause missing credentials to be regenerated");
            Check(!Directory.EnumerateFiles(strMissingRoot, "*.key").Any(), "Recovery failure creates no key file");
        }
        using (var objDatabase = Database.OpenUiDevelopmentWorkspace(strRoot, objKeys))
        {
            using var secPassword = TestPasswords.Secret("FICTIONAL file key test only!");
            using var objSession = new AuthService(objDatabase).CreateAdministrator("file-key-fixture", secPassword);
            new CrmService(objDatabase, objSession).CuiEpoch();
        }
        using (var objDatabase = Database.OpenUiDevelopmentWorkspace(strRoot, Select(true, false)))
            Check(!new AuthService(objDatabase).GetLoginRequirements().bNeedsSetup, "Encrypted Debug database and signed checkpoint reopen using both file keys without Keychain construction");
        Console.WriteLine($"{iPassed} Debug file-key checks passed; only fictional disposable artifacts used.");
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception
    {
        try { actCheck(); }
        catch (T objException)
        { Check(objException.Message == "Workspace recovery is required. Contact your local administrator.", strMessage); return; }
        throw new InvalidOperationException("FAIL " + strMessage);
    }
}
#endif
