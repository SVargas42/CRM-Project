using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Desktop.Views;

internal static class OrganizationLifecycleViewChecks
{
    internal static void Run()
    {
#if DEBUG
        RunDebug();
#else
        throw new InvalidOperationException("Use the isolated Debug fixture for organization lifecycle UI checks.");
#endif
    }
#if DEBUG
    private static void RunDebug()
    {
        var iChecks = 0;
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        using var objDatabase = Database.CreateUiDevelopmentWorkspace();
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)(() => objDatabase));
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var objAuth = new AuthService(objDatabase);
        using var secPassword = new System.Security.SecureString();
        foreach (var chrValue in "FICTIONAL lifecycle UI password!") secPassword.AppendChar(chrValue);
        using var objAdmin = objAuth.CreateAdministrator("lifecycle-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objRestored = new Organization { Name = "FICTIONAL restored visible" }; objCrm.Save(objRestored);
        var objArchived = new Organization { Name = "FICTIONAL archived visible" }; objCrm.Save(objArchived);
        var objHidden = new Organization { Name = "FICTIONAL hidden active" }; objCrm.Save(objHidden);
        var objHiddenArchived = new Organization { Name = "FICTIONAL hidden archived" }; objCrm.Save(objHiddenArchived);
        Archive(objRestored, true); Archive(objRestored, false); Archive(objArchived, true); Archive(objHiddenArchived, true);
        var lngViewer = objUsers.CreateUser("lifecycle-viewer", secPassword, UserRole.Viewer);
        objUsers.SetCuiAuthorization(lngViewer, true);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objRestored.Id, true, false);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objArchived.Id, true, false);
        var vmWorkspace = new MainWindowViewModel(); Complete(vmWorkspace.tskInitialization);
        vmWorkspace.strUsername = "lifecycle-viewer";
        foreach (var chrValue in "FICTIONAL lifecycle UI password!") vmWorkspace.secPassword.AppendChar(chrValue);
        Execute(vmWorkspace.cmdSignIn);
        var frmMain = new MainWindow(vmWorkspace); frmMain.Show();
        try
        {
            Check(vmWorkspace.objDashboard?.Organizations == 1, "Overview counts the restored authorized organization as Active");
            Execute(vmWorkspace.cmdOrganizations);
            var vmOrganizations = (OrganizationsPageViewModel)vmWorkspace.vmShell.vmCurrentPage; Ready();
            Check(vmOrganizations.iVisibilityFilter == 0 && vmWorkspace.lstOrganizations.Single().Id == objRestored.Id, "Default Active UI includes restored and excludes archived organizations");
            Check(vmOrganizations.lstRows.Single().strLifecycle == "Active", "Restored organization displays Active despite stored Inactive");
            var ctlLookup = new OrganizationsRootView();
            var cboVisibility = ctlLookup.FindControl<ComboBox>("cboOrganizationVisibilityFilter")!;
            Check(cboVisibility.Items.Cast<ComboBoxItem>().Select(objItem => objItem.Content?.ToString()).SequenceEqual(new[] { "Active", "Archived" })
                && AutomationProperties.GetName(cboVisibility) == "Organization Active or Archived filter", "Accessible organization filter has exactly Active and Archived");
            Check(new OrganizationEditorView().FindControl<ComboBox>("cboOrganizationStatus") is null
                && new EditOrganizationView().FindControl<ComboBox>("cboEditOrganizationStatus") is null, "Organization forms expose no stored-status selectors");
            Check(vmWorkspace.lstOrganizations.All(objOrg => objOrg.Id != objHidden.Id && objOrg.Id != objHiddenArchived.Id), "Active lookup excludes organizations without record access");
            vmOrganizations.iVisibilityFilter = 1; Ready();
            Check(vmWorkspace.lstOrganizations.Single().Id == objArchived.Id && vmOrganizations.lstRows.Single().strLifecycle == "Archived", "Archived UI includes only authorized archived organizations and labels them Archived");
            Check(vmWorkspace.lstOrganizations.All(objOrg => objOrg.Id != objHidden.Id && objOrg.Id != objHiddenArchived.Id), "Archived lookup also excludes organizations without record access");
            var cmdProfile = (AsyncRelayCommand)vmOrganizations.lstRows.Single().cmdViewProfile;
            cmdProfile.Execute(null); Complete(cmdProfile.tskExecution);
            Check(vmOrganizations.vmProfile?.strLifecycle == "Archived", "Organization profile uses archive-derived lifecycle text");
            Execute(vmWorkspace.cmdOverview);
            var vmDashboard = (DashboardPageViewModel)vmWorkspace.vmShell.vmCurrentPage;
            vmDashboard.iSelectedTab = 2; Complete(((AsyncRelayCommand)vmDashboard.vmMasterSearch.cmdSearch).tskExecution);
            var vmSearch = vmDashboard.vmMasterSearch;
            Check(vmSearch.lstResults.Where(vmRow => vmRow.strType == "Organization").Single().lngRecordId == objRestored.Id, "Master Search default includes restored and hides ungranted organizations");
            Check(vmSearch.lstResults.Single().strStatus == "Active", "Master Search displays restored organization as Active");
            vmSearch.iOrganizationVisibility = 1; vmSearch.cmdSearch.Execute(null); Complete(((AsyncRelayCommand)vmSearch.cmdSearch).tskExecution);
            Check(vmSearch.lstResults.Single().lngRecordId == objArchived.Id && vmSearch.lstResults.Single().strStatus == "Archived", "Master Search Archived choice keeps grants and archive labeling");
            Execute(vmWorkspace.cmdLock);
            Check(vmWorkspace.lstOrganizations.Count == 0 && vmSearch.lstResults.Count == 0, "Lock still clears organization and Master Search presentation");
            Console.WriteLine($"{iChecks} organization lifecycle UI checks passed.");
            void Ready()
            { Complete(vmWorkspace.tskPendingOperation); Complete(vmOrganizations.tskSearch); Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout(); }
        }
        finally { Complete(vmWorkspace.CloseAsync()); frmMain.Close(); objAuth.SignOut(objAdmin); }
        void Archive(Organization objOrganization, bool bArchive)
        {
            var objCurrent = objCrm.OrganizationRecord(objOrganization.Id);
            objCrm.SetArchived(RecordKind.Organization, objCurrent.Id, objCurrent.Revision, bArchive);
            CuiMemoryPolicy.Release(objCurrent);
        }
        void Execute(System.Windows.Input.ICommand cmdAction)
        { cmdAction.Execute(null); Complete(((AsyncRelayCommand)cmdAction).tskExecution); Complete(vmWorkspace.tskPendingOperation); }
        void Check(bool bCondition, string strDescription)
        { if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription); iChecks++; Console.WriteLine("PASS " + strDescription); }
    }
    private static void Complete(Task tskWork) => UiDevelopmentChecks.Complete(tskWork);
#endif
}