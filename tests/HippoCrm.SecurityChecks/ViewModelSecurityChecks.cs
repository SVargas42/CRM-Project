using System.Security;
using HippoCrm.Core;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.TestSupport;

internal static class ViewModelSecurityChecks
{
    internal static void RunUnprovisioned()
    {
        using var objDatabase = new Database();
        var vmWorkspace = new MainWindowViewModel(objDatabase);
        try
        {
            vmWorkspace.tskInitialization.GetAwaiter().GetResult();
            SecurityPolicyChecks.Assert(vmWorkspace.strPage == "Error" && !vmWorkspace.bCanCreate,
                "Unprovisioned ViewModel startup fails closed even with an injected fixture database");
            SecurityPolicyChecks.Assert(vmWorkspace.lstContacts.Count == 0 && vmWorkspace.objDashboard is null,
                "Failed startup never exposes authenticated workspace data");
            SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM users") == 0,
                "Failed startup never creates an administrator");
        }
        finally { vmWorkspace.CloseAsync().GetAwaiter().GetResult(); }
    }

    internal static void Run()
    {
        using var objDatabase = new Database();
        var vmWorkspace = new MainWindowViewModel(objDatabase);
        vmWorkspace.tskInitialization.GetAwaiter().GetResult();
        Fill(vmWorkspace.secPassword, "Fictional workspace password!");
        Fill(vmWorkspace.secConfirmation, "Fictional workspace password!");
        var secSetupBuffer = vmWorkspace.secPassword;
        vmWorkspace.strUsername = "fixture-workspace";
        vmWorkspace.cmdSignIn.Execute(null);
        vmWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmWorkspace.strPage == "Overview" && vmWorkspace.bCanCreate, "View-model bootstrap creates an administrator workspace");
        SecurityPolicyChecks.Assert(vmWorkspace.secPassword.Length == 0 && vmWorkspace.secConfirmation.Length == 0, "Successful submission replaces both password buffers with empty buffers");
        SecurityPolicyChecks.Reject<ObjectDisposedException>(() => secSetupBuffer.Copy(), "Successful submission disposes the old SecureString");
        vmWorkspace.cmdLock.Execute(null);
        ((AsyncRelayCommand)vmWorkspace.cmdLock).tskExecution.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmWorkspace.strPage == "Login" && !vmWorkspace.bCanCreate && vmWorkspace.lstContacts.Count == 0 && vmWorkspace.objDashboard is null,
            "Lock clears authenticated data and permissions before displaying login");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM sessions WHERE status='Active'") == 0, "Workspace lock revokes the authenticated session");
        vmWorkspace.strUsername = "fixture-workspace"; Fill(vmWorkspace.secPassword, "Incorrect fictional password!");
        var secFailedBuffer = vmWorkspace.secPassword;
        vmWorkspace.cmdSignIn.Execute(null);
        vmWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmWorkspace.strPage == "Login" && vmWorkspace.secPassword.Length == 0 && vmWorkspace.strMessage == "Invalid Password", "Failed single-account password submissions clear password buffers and show password-specific feedback");
        SecurityPolicyChecks.Reject<ObjectDisposedException>(() => secFailedBuffer.Copy(), "Failed submission disposes the old SecureString");
        vmWorkspace.strUsername = "fixture-workspace"; Fill(vmWorkspace.secPassword, "Fictional workspace password!");
        vmWorkspace.cmdSignIn.Execute(null);
        vmWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        using var objAdministrator = new AuthService(objDatabase).SignInForTest("fixture-workspace", "Fictional workspace password!");
        using var secViewerPassword = TestPasswords.Secret("Fictional viewer password!");
        var objAdministration = new UserAdministrationService(objDatabase, objAdministrator);
        var lngViewerId = objAdministration.CreateUser("fixture-vm-viewer", secViewerPassword, UserRole.Viewer);
        var objViewerWorkspace = new MainWindowViewModel(objDatabase);
        objViewerWorkspace.tskInitialization.GetAwaiter().GetResult();
        Fill(objViewerWorkspace.secPassword, "Fictional viewer password!");
        objViewerWorkspace.cmdSignIn.Execute(null);
        objViewerWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(objViewerWorkspace.strPage == "Login" && !objViewerWorkspace.bCanCreate,
            "Multi-account UI rejects login without an explicit username");
        objViewerWorkspace.strUsername = "fixture-vm-viewer"; Fill(objViewerWorkspace.secPassword, "Fictional viewer password!");
        objViewerWorkspace.cmdSignIn.Execute(null);
        objViewerWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(objViewerWorkspace.strPage == "Overview" && !objViewerWorkspace.bCanCreate,
            "Multi-account UI accepts explicit usernames while preserving viewer restrictions");
        objViewerWorkspace.CloseAsync().GetAwaiter().GetResult();
        using var objViewerDatabase = new Database();
        using var objViewerBootstrap = new AuthService(objViewerDatabase).CreateAdministratorForTest("sole-viewer", "Fictional viewer password!");
        SecurityPolicyChecks.Sql(objViewerDatabase, "UPDATE users SET role='Viewer'");
        RevokeFixtureSessions(objViewerDatabase);
        var vmViewer = new MainWindowViewModel(objViewerDatabase);
        vmViewer.tskInitialization.GetAwaiter().GetResult();
        Fill(vmViewer.secPassword, "Fictional viewer password!");
        vmViewer.cmdSignIn.Execute(null);
        vmViewer.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmViewer.strPage == "Overview" && !vmViewer.bCanCreate, "Password-only viewer workspace preserves record-creation restrictions");
        RevokeFixtureSessions(objViewerDatabase);
        vmViewer.cmdContacts.Execute(null);
        vmViewer.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmViewer.strPage == "Login" && vmViewer.objDashboard is null,
            "Revoked authentication clears cached UI data on the next operation");
        vmViewer.CloseAsync().GetAwaiter().GetResult();
        SecurityPolicyChecks.Sql(objDatabase, "CREATE TRIGGER test_block_audit BEFORE INSERT ON audit_log BEGIN SELECT RAISE(ABORT,'Fixture audit unavailable'); END");
        vmWorkspace.cmdLock.Execute(null);
        ((AsyncRelayCommand)vmWorkspace.cmdLock).tskExecution.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmWorkspace.strPage == "Login" && vmWorkspace.objDashboard is null && !vmWorkspace.bCanCreate,
            "Audit failure during sign-out still obscures the UI and drops local credentials");
        vmWorkspace.CloseAsync().GetAwaiter().GetResult();
    }
    private static void RevokeFixtureSessions(Database objDatabase)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var lstUsers = new List<long>();
        using (var cmdUsers = new Microsoft.Data.Sqlite.SqliteCommand("SELECT id FROM users", dbConnection, txOperation))
        using (var rdrUsers = cmdUsers.ExecuteReader())
            while (rdrUsers.Read()) lstUsers.Add(rdrUsers.GetInt64(0));
        foreach (var lngUser in lstUsers) RetentionStatements.RevokeUserSessions(dbConnection, txOperation, lngUser);
        AuthenticationAudit.Append(dbConnection, txOperation, null, AuditEntity.Sessions, 0, AuditAction.SignedOut);
        txOperation.Commit();
    }
    private static void Fill(SecureString secPassword, string strFictionalPassword)
    {
        foreach (var chrCharacter in strFictionalPassword) secPassword.AppendChar(chrCharacter);
    }
}
