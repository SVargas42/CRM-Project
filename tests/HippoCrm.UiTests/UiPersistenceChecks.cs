#if DEBUG
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.TestSupport;

internal static class UiPersistenceChecks
{
    private static int iPassed;
    internal static int ProbeLease(string strPath)
    {
        try { using var stmLease = new FileStream(strPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return 0; }
        catch (IOException) { return 2; }
    }
    private static int ChildLease(string strPath)
    {
        var objStart = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        objStart.ArgumentList.Add(typeof(UiPersistenceChecks).Assembly.Location);
        objStart.ArgumentList.Add("--ui-lease-probe"); objStart.ArgumentList.Add(strPath);
        using var objChild = Process.Start(objStart) ?? throw new InvalidOperationException("Could not start the local lease probe.");
        if (!objChild.WaitForExit(10000)) { objChild.Kill(entireProcessTree: true); throw new TimeoutException("Lease probe timed out."); }
        return objChild.ExitCode;
    }
    internal static void Run()
    {
        var strRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-persistence-tests", Guid.NewGuid().ToString("N"), "UiDevelopment");
        var strCheckpoint = Path.Combine(strRoot, "checkpoint.json");
        var strLease = Path.Combine(strRoot, "workspace.lease");
        var strDatabase = Path.Combine(strRoot, "ui-development.db");
        using var objKeys = new EphemeralKeyStore();
        using var secPassword = TestPasswords.Secret("FICTIONAL persistent UI fixture!");
        long lngVersion;
        using (var objDatabase = Database.OpenUiDevelopmentWorkspace(strRoot, objKeys))
        {
            var objAuth = new AuthService(objDatabase);
            Check(objAuth.NeedsSetup, "First persistent launch requires account setup");
            using var objAdmin = objAuth.CreateAdministrator("persistent-ui", secPassword);
            new UserAdministrationService(objDatabase, objAdmin).SetCuiAuthorization(objAdmin.UserId, true);
            var objCrm = new CrmService(objDatabase, objAdmin);
            objCrm.Save(new Contact { FirstName = "FICTIONAL", LastName = "Persisted UI contact" });
            objCrm.Save(new Organization { Name = "FICTIONAL persisted UI organization" });
            objDatabase.SaveDarkTheme(true);
            objAuth.SignOut(objAdmin);
            Check(File.Exists(strDatabase) && File.Exists(strCheckpoint), "Database and matching checkpoint are both saved");
            var arrDatabase = File.ReadAllBytes(strDatabase);
            Check(!arrDatabase.AsSpan(0, 16).SequenceEqual("SQLite format 3\0"u8), "Persistent Debug database is SQLCipher-encrypted, not plaintext SQLite");
            Check(!Encoding.UTF8.GetString(arrDatabase).Contains("Persisted UI contact", StringComparison.Ordinal), "Fixture contact text is absent from raw database bytes");
            Check(!File.ReadAllText(strCheckpoint).Contains("FICTIONAL", StringComparison.Ordinal), "Checkpoint JSON contains no fixture record or password text");
            lngVersion = Version(strCheckpoint);
            Check(lngVersion > 0, "Committed audit progress has a persisted version stamp");
            Reject<IOException>(() => Database.OpenUiDevelopmentWorkspace(strRoot, objKeys), "Second workspace owner cannot acquire the live lease");
            Check(ChildLease(strLease) == 2, "Separate process cannot acquire the live workspace lease");
        }
        Check(ChildLease(strLease) == 0, "Another process can reacquire the lease after disposal");
        using (var objDatabase = Database.OpenUiDevelopmentWorkspace(strRoot, objKeys))
        {
            var objAuth = new AuthService(objDatabase);
            Check(!objAuth.NeedsSetup, "Restart reopens the existing account instead of requesting a new workspace");
            using var objAdmin = objAuth.SignIn("persistent-ui", secPassword);
            var objCrm = new CrmService(objDatabase, objAdmin);
            Check(objCrm.Contacts().Single().LastName == "Persisted UI contact", "Contact survives close and reopen");
            Check(objCrm.Organizations().Single().Name == "FICTIONAL persisted UI organization", "Organization survives close and reopen");
            Check(objDatabase.ReadDarkTheme(), "Workspace preferences survive close and reopen");
            Check(Version(strCheckpoint) > lngVersion, "Reopened workspace advances the original persisted checkpoint");
            var arrOlderCheckpoint = File.ReadAllBytes(strCheckpoint);
            objCrm.Contacts();
            var arrCurrentCheckpoint = File.ReadAllBytes(strCheckpoint);
            File.WriteAllBytes(strCheckpoint, arrOlderCheckpoint);
            Reject<AuditRecoveryRequiredException>(() => objCrm.Contacts(), "Replayed older authenticated checkpoint is rejected against current database history");
            File.WriteAllBytes(strCheckpoint, arrCurrentCheckpoint);
            using (var stmLocked = new FileStream(strCheckpoint, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject<IOException>(() => objCrm.Contacts(), "Locked checkpoint fails without resetting workspace data");
            Check(objCrm.Contacts().Count == 1, "A transient checkpoint file lock does not cause permanent lockout");
            objAuth.SignOut(objAdmin);
        }
        var arrValid = File.ReadAllBytes(strCheckpoint);
        File.WriteAllText(strCheckpoint, "{}");
        Reject<CryptographicException>(() => Database.OpenUiDevelopmentWorkspace(strRoot, objKeys), "Malformed checkpoint is rejected instead of replaced");
        Check(File.ReadAllText(strCheckpoint) == "{}", "Failed open preserves invalid checkpoint evidence");
        File.WriteAllBytes(strCheckpoint, arrValid);
        using (var objWrongKeys = new EphemeralKeyStore())
            Reject<CryptographicException>(() => Database.OpenUiDevelopmentWorkspace(strRoot, objWrongKeys), "Wrong protected-key material cannot authenticate the saved stamp");
        File.Move(strCheckpoint, strCheckpoint + ".saved");
        Reject<AuditRecoveryRequiredException>(() => Database.OpenUiDevelopmentWorkspace(strRoot, objKeys), "Missing checkpoint beside an existing database cannot trigger fresh enrollment");
        Check(!File.Exists(strCheckpoint), "Missing checkpoint is not silently regenerated");
        File.Move(strCheckpoint + ".saved", strCheckpoint);
        using (var objDatabase = Database.OpenUiDevelopmentWorkspace(strRoot, objKeys))
            Check(!new AuthService(objDatabase).NeedsSetup, "Valid restored state reopens after negative probes without losing accounts");
        Check(objKeys.arrLastRead is not null && objKeys.arrLastRead.All(bytValue => bytValue == 0), "Managed protected-key read buffers are cleared after use");
        Reject<ArgumentException>(() => Database.OpenUiDevelopmentWorkspace(Path.GetDirectoryName(strRoot)!, objKeys), "Persistent Debug factory requires a dedicated UiDevelopment directory");
        RunUiRestart();
        Console.WriteLine($"{iPassed} persistent Debug workspace checks passed.");
    }
    private static void RunUiRestart()
    {
        var strRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-persistence-tests", Guid.NewGuid().ToString("N"), "UiDevelopment");
        using var objKeys = new EphemeralKeyStore();
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)(() => Database.OpenUiDevelopmentWorkspace(strRoot, objKeys)));
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var vmFirst = new MainWindowViewModel();
        MainWindowViewModel? vmSecond = null;
        try
        {
            UiDevelopmentChecks.Complete(vmFirst.tskInitialization);
            Check(vmFirst.bSetup, "First UI lifecycle requests fixture account setup");
            vmFirst.strUsername = "persistent-ui-account";
            foreach (var chrValue in "FICTIONAL UI restart password!") { vmFirst.secPassword.AppendChar(chrValue); vmFirst.secConfirmation.AppendChar(chrValue); }
            UiDevelopmentChecks.Execute(vmFirst.cmdSignIn);
            UiDevelopmentChecks.Execute(vmFirst.cmdContacts); UiDevelopmentChecks.Execute(vmFirst.cmdNewContact);
            vmFirst.objContact!.FirstName = "FICTIONAL"; vmFirst.objContact.LastName = "UI restart retained";
            UiDevelopmentChecks.Execute(vmFirst.cmdSaveContact);
            Check(vmFirst.lstContacts.Any(objValue => objValue.LastName == "UI restart retained"), "First UI lifecycle saves a persistent fixture contact");
            UiDevelopmentChecks.Complete(vmFirst.CloseAsync()); UiDevelopmentChecks.Invoke("DisposeWorkspace");
            vmSecond = new MainWindowViewModel(); UiDevelopmentChecks.Complete(vmSecond.tskInitialization);
            Check(!vmSecond.bSetup && vmSecond.strPage == "Login", "Restarted UI returns to login without another workspace setup");
            foreach (var chrValue in "FICTIONAL UI restart password!") vmSecond.secPassword.AppendChar(chrValue);
            UiDevelopmentChecks.Execute(vmSecond.cmdSignIn); UiDevelopmentChecks.Execute(vmSecond.cmdContacts);
            Check(vmSecond.lstContacts.Any(objValue => objValue.LastName == "UI restart retained"), "Restarted UI authenticates the saved account and loads its prior contact");
        }
        finally
        {
            UiDevelopmentChecks.Complete(vmFirst.CloseAsync());
            if (vmSecond is not null) UiDevelopmentChecks.Complete(vmSecond.CloseAsync());
            UiDevelopmentChecks.Invoke("DisposeWorkspace");
        }
    }
    private static long Version(string strPath)
    { using var objJson = JsonDocument.Parse(File.ReadAllText(strPath)); return objJson.RootElement.GetProperty("objBody").GetProperty("objCheckpoint").GetProperty("lngVersion").GetInt64(); }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
    private static void Reject<T>(Action actOperation, string strMessage) where T : Exception
    { try { actOperation(); } catch (T) { Check(true, strMessage); return; } throw new InvalidOperationException("FAIL " + strMessage); }
}
#endif
