using System.Security;
using HippoCrm.Core;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.TestSupport;

internal static class ViewModelSecurityChecks
{
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
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM sessions") == 0, "Workspace lock revokes the authenticated session");
        vmWorkspace.strUsername = "fixture-workspace"; Fill(vmWorkspace.secPassword, "Incorrect fictional password!");
        var secFailedBuffer = vmWorkspace.secPassword;
        vmWorkspace.cmdSignIn.Execute(null);
        vmWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmWorkspace.strPage == "Login" && vmWorkspace.secPassword.Length == 0 && vmWorkspace.strMessage == "Invalid username or password.", "Failed submissions clear password buffers and return a generic authentication message");
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
        objViewerWorkspace.strUsername = "fixture-vm-viewer"; Fill(objViewerWorkspace.secPassword, "Fictional viewer password!");
        objViewerWorkspace.cmdSignIn.Execute(null);
        objViewerWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(objViewerWorkspace.strPage == "Overview" && !objViewerWorkspace.bCanCreate, "Viewer workspace exposes no record-creation permission");
        objAdministration.SetRole(lngViewerId, UserRole.Editor);
        objViewerWorkspace.cmdContacts.Execute(null);
        objViewerWorkspace.tskPendingOperation.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(objViewerWorkspace.strPage == "Login" && objViewerWorkspace.objDashboard is null,
            "Revoked authentication clears cached UI data on the next operation");
        objViewerWorkspace.CloseAsync().GetAwaiter().GetResult();
        SecurityPolicyChecks.Sql(objDatabase, "CREATE TRIGGER test_block_audit BEFORE INSERT ON audit_log BEGIN SELECT RAISE(ABORT,'Fixture audit unavailable'); END");
        vmWorkspace.cmdLock.Execute(null);
        ((AsyncRelayCommand)vmWorkspace.cmdLock).tskExecution.GetAwaiter().GetResult();
        SecurityPolicyChecks.Assert(vmWorkspace.strPage == "Login" && vmWorkspace.objDashboard is null && !vmWorkspace.bCanCreate,
            "Audit failure during sign-out still obscures the UI and drops local credentials");
        vmWorkspace.CloseAsync().GetAwaiter().GetResult();
    }
    private static void Fill(SecureString secPassword, string strFictionalPassword)
    {
        foreach (var chrCharacter in strFictionalPassword) secPassword.AppendChar(chrCharacter);
    }
}
