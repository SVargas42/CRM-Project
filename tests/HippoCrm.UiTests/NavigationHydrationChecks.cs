using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using Microsoft.Data.Sqlite;

internal static class NavigationHydrationChecks
{
    private static int iPassed;
    internal static void Run()
    {
        var bDenied = false;
        try { StartupSecurity.Verify(); } catch (CryptographicException) { bDenied = true; }
        Check(bDenied, "Navigation fixtures retain normal production provisioning denial");
#if DEBUG
        RunDebug();
#endif
        Console.WriteLine($"{iPassed} asynchronous navigation/hydration checks passed.");
    }

#if DEBUG
    private static void RunDebug()
    {
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)Database.CreateUiDevelopmentWorkspace);
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var vmWorkspace = new MainWindowViewModel(); Complete(vmWorkspace.tskInitialization);
        var frmMain = new MainWindow(vmWorkspace); frmMain.Show();
        var objDatabase = Field<Database>(vmWorkspace, "objDatabase");
        var semWork = Field<SemaphoreSlim>(vmWorkspace, "semDatabaseWork");
        var bGateHeld = false;
        try
        {
            vmWorkspace.strAccount = "navigation-admin";
            Password(vmWorkspace, true); Execute(vmWorkspace.cmdSignIn);
            using var secPassword = new System.Security.SecureString();
            foreach (var chrValue in "FICTIONAL asynchronous navigation!") secPassword.AppendChar(chrValue);
            using var objAdmin = new AuthService(objDatabase).SignIn(vmWorkspace.strSignedInUsername, secPassword);
            var objUsers = new UserAdministrationService(objDatabase, objAdmin);
            objUsers.SetCuiRecipientKind(objAdmin.UserId, CuiRecipientKind.DoD, "FICTIONAL verified affiliation");
            objUsers.SetCuiPolicyAccess(objAdmin.UserId, CuiPolicy.strCtiMarking, "B", true, "FICTIONAL authority");
            var objCrm = new CrmService(objDatabase, objAdmin);
            var objVisible = new Contact { FirstName = "FICTIONAL visible navigation record" }; objCrm.Save(objVisible);
            var objRestricted = new Contact { FirstName = "FICTIONAL restricted CTI", strCuiCategory = CuiPolicy.strCtiMarking,
                strDistributionStatement = "B", strControllingOffice = "FICTIONAL office", strDeterminationDate = "2026-11-05",
                strDistributionNotice = "Distribution Statement B. FICTIONAL approved government audience, determination 2026-11-05, FICTIONAL office." };
            objCrm.Save(objRestricted);
            var objOrganization = new Organization { Name = "FICTIONAL navigation organization" }; objCrm.Save(objOrganization);
            objVisible.lstOrganizationIds = [objOrganization.Id]; objCrm.Save(objVisible);
            var lngViewer = objUsers.CreateUser("navigation-viewer", secPassword, UserRole.Viewer);
            objUsers.SetCuiAuthorization(lngViewer, true);
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objVisible.Id, true, false);
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objRestricted.Id, true, false);
            objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objOrganization.Id, true, false);
            EditBatchChecks(vmWorkspace, objDatabase, objVisible.Id, objOrganization.Id);
            Execute(vmWorkspace.cmdLock);
            vmWorkspace.strAccount = "navigation-viewer"; Password(vmWorkspace, false); Execute(vmWorkspace.cmdSignIn);

            foreach (var (strRoute, cmdRoute) in new[] { ("Contacts", vmWorkspace.cmdContacts), ("Organizations", vmWorkspace.cmdOrganizations), ("Overview", vmWorkspace.cmdOverview) })
            {
                var lngBefore = AuditReads(objDatabase);
                var swLoad = Stopwatch.StartNew(); Execute(cmdRoute); ReadyRows(vmWorkspace); swLoad.Stop();
                var lngReads = AuditReads(objDatabase)-lngBefore;
                Check(lngReads <= (strRoute == "Contacts" ? 4 : strRoute == "Organizations" ? 3 : 2), "Navigation bounds protected reads for " + strRoute);
                Console.WriteLine($"Navigation measurement {strRoute}: {AuditReads(objDatabase)-lngBefore} protected reads; {swLoad.Elapsed.TotalMilliseconds:F1} ms (isolated fixture).");
            }
            Execute(vmWorkspace.cmdContacts); ReadyRows(vmWorkspace);
            var objOldProjection = vmWorkspace.lstContacts.Single();
            Execute(vmWorkspace.cmdOverview);
            Check(objOldProjection.FirstName == "", "Leaving a page clears owned CUI projection fields");
            // Hold the real worker queue, not a fake delay in routing. UI selection
            // and a complete native-headless layout must precede queue release.
            Check(semWork.Wait(0), "Hydration test acquires the idle worker queue"); bGateHeld = true;
            var swRoute = Stopwatch.StartNew(); vmWorkspace.cmdContacts.Execute(null);
            var tsRoute = swRoute.Elapsed;
            var tskFirst = ((AsyncRelayCommand)vmWorkspace.cmdContacts).tskExecution;
            Check(vmWorkspace.strPage == "Contacts" && vmWorkspace.vmShell.bContacts && tsRoute < TimeSpan.FromMilliseconds(100),
                $"Blocked queue does not delay route selection ({tsRoute.TotalMilliseconds:F2} ms)");
            Check(vmWorkspace.bPageLoading && !vmWorkspace.bBusy && !tskFirst.IsCompleted, "Navigation uses local loading state without the global busy gate");
            Check(vmWorkspace.lstContacts.Count == 0 && vmWorkspace.objContact is null, "Empty destination exposes no previous or unverified CUI");
            Check(vmWorkspace.cmdOrganizations.CanExecute(null) && vmWorkspace.cmdLock.CanExecute(null), "Sidebar navigation and locking remain available while loading");
            Check(!vmWorkspace.cmdNewContact.CanExecute(null), "Page actions remain guarded while hydration is incomplete");
            Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
            Check(frmMain.GetLogicalDescendants().OfType<HippoCrm.Desktop.Views.ContactsRootView>().Any(), "Destination view is constructed and laid out while the database is blocked");
            Check(frmMain.FindControl<Border>("bdrPageLoading")!.IsVisible && !frmMain.FindControl<ContentControl>("ctrCurrentPage")!.IsEnabled,
                "Page-local spinner covers and guards the empty content");
            var bHeartbeat = false; Dispatcher.UIThread.Post(() => bHeartbeat = true); Dispatcher.UIThread.RunJobs();
            Check(bHeartbeat, "Dispatcher services callbacks during hydration wait");
            vmWorkspace.cmdOrganizations.Execute(null); var tskSecond = ((AsyncRelayCommand)vmWorkspace.cmdOrganizations).tskExecution;
            Check(vmWorkspace.strPage == "Organizations" && vmWorkspace.vmShell.bOrganizations, "A second destination switches before the first request completes");
            vmWorkspace.cmdOverview.Execute(null);
            vmWorkspace.cmdContacts.Execute(null); var tskLatest = ((AsyncRelayCommand)vmWorkspace.cmdContacts).tskExecution;
            Check(vmWorkspace.strPage == "Contacts" && vmWorkspace.lstOrganizations.Count == 0, "Rapid A-B-C-A routing retains only the latest empty destination");
            semWork.Release(); bGateHeld = false; Complete(tskLatest); Complete(tskFirst); Complete(tskSecond); ReadyRows(vmWorkspace);
            Check(!vmWorkspace.bPageLoading && !vmWorkspace.bPageLoadFailed && !vmWorkspace.bBusy, "Latest authorized hydration completes without global busy state");
            Check(vmWorkspace.lstContacts.Single().Id == objVisible.Id && vmWorkspace.lstContacts.All(objContact => objContact.Id != objRestricted.Id),
                "D06 category/distribution checks withhold CTI despite an explicit record grant");
            Check(!frmMain.FindControl<Border>("bdrPageLoading")!.IsVisible && frmMain.FindControl<ContentControl>("ctrCurrentPage")!.IsEnabled,
                "Authorized completion removes the local spinner and enables the page");

            ProfileChecks(vmWorkspace, semWork, objVisible.Id, objOrganization.Id);
            OrganizationFinalEpochChecks(vmWorkspace, semWork, objDatabase, objUsers, lngViewer, objVisible.Id, objOrganization.Id);
            Execute(vmWorkspace.cmdContacts); ReadyRows(vmWorkspace);

            FinalEpochChecks(vmWorkspace, semWork, objDatabase, objUsers, lngViewer, objVisible.Id);
            LateWorkerChecks(vmWorkspace);
            Execute(vmWorkspace.cmdContacts); ReadyRows(vmWorkspace);
            Sql(objDatabase, "CREATE TRIGGER fixture_navigation_audit BEFORE INSERT ON audit_log WHEN NEW.action='AccessVerified' BEGIN SELECT RAISE(ABORT,'FICTIONAL local audit unavailable'); END");
            Execute(vmWorkspace.cmdOrganizations);
            Check(vmWorkspace.bPageLoadFailed && !vmWorkspace.bPageLoading && vmWorkspace.lstOrganizations.Count == 0, "Audit failure shows local error without publishing unverified data");
            Check(vmWorkspace.cmdContacts.CanExecute(null), "Hydration failure does not freeze other navigation");
            Sql(objDatabase, "DROP TRIGGER fixture_navigation_audit"); Execute(vmWorkspace.cmdReloadPage);
            ReadyRows(vmWorkspace);
            Check(!vmWorkspace.bPageLoadFailed && vmWorkspace.lstOrganizations.Single().Id == objOrganization.Id, "Reload retries through live authorization and audited reads");

            Check(semWork.Wait(0), "Lock fixture acquires the worker queue"); bGateHeld = true;
            vmWorkspace.cmdContacts.Execute(null); vmWorkspace.cmdLock.Execute(null);
            Check(vmWorkspace.strPage == "Login" && !vmWorkspace.bPageLoading && vmWorkspace.lstContacts.Count == 0,
                "Lock immediately invalidates route loading and clears the workspace");
            semWork.Release(); bGateHeld = false; Complete(((AsyncRelayCommand)vmWorkspace.cmdLock).tskExecution);
            Check(vmWorkspace.strPage == "Login" && vmWorkspace.lstContacts.Count == 0, "Canceled route cannot repopulate a signed-out workspace");
            vmWorkspace.strAccount = "navigation-viewer"; Password(vmWorkspace, false); Execute(vmWorkspace.cmdSignIn);
            Check(semWork.Wait(0), "Revocation fixture acquires the worker queue"); bGateHeld = true;
            vmWorkspace.cmdContacts.Execute(null);
            objUsers.SetCuiAuthorization(lngViewer, false);
            semWork.Release(); bGateHeld = false; Complete(((AsyncRelayCommand)vmWorkspace.cmdContacts).tskExecution);
            PumpUntil(() => !vmWorkspace.bLocking && vmWorkspace.strPage is "Login" or "Error");
            Check(!vmWorkspace.CanReadCui() && vmWorkspace.lstContacts.Count == 0, "Revocation during hydration locks rather than publishing the pending page");
        }
        finally
        {
            if (bGateHeld) semWork.Release();
            Complete(vmWorkspace.CloseAsync()); frmMain.Close(); UiDevelopmentChecks.Invoke("DisposeWorkspace");
        }
    }

    private static void EditBatchChecks(MainWindowViewModel vmWorkspace, Database objDatabase, long lngContactId, long lngOrganizationId)
    {
        Execute(vmWorkspace.cmdContacts); ReadyRows(vmWorkspace);
        var vmContacts = (ContactsPageViewModel)vmWorkspace.vmShell.vmCurrentPage;
        Execute(vmContacts.lstRows.Single(vmRow => vmRow.strFirstName == "FICTIONAL visible navigation record").cmdViewProfile);
        var lngBefore = AuditReads(objDatabase);
        Execute(vmContacts.vmProfile!.cmdEdit);
        Check(vmContacts.bEditing && vmWorkspace.objContact?.Id == lngContactId && !vmWorkspace.bBusy,
            "Contact edit preserves the authorized draft after batched features");
        // lstContactOrganizations notification also refreshes the lookup rows.
        // Await its unchanged audited validator so it cannot leak into the next measurement.
        ReadyRows(vmWorkspace);
        Console.WriteLine($"Profile measurement Contact edit: {AuditReads(objDatabase)-lngBefore} protected reads (including row filtering).");
        Check(AuditReads(objDatabase) - lngBefore == 7, "Contact edit retains six protected load reads plus the row-filter validation");
        Execute(vmWorkspace.cmdOrganizations); ReadyRows(vmWorkspace);
        var vmOrganizations = (OrganizationsPageViewModel)vmWorkspace.vmShell.vmCurrentPage;
        lngBefore = AuditReads(objDatabase);
        Execute(vmOrganizations.lstRows.Single().cmdViewProfile);
        Complete(vmOrganizations.vmProfile!.tskContactSearch);
        Console.WriteLine($"Profile measurement Organization view: {AuditReads(objDatabase)-lngBefore} protected reads (including member filtering).");
        Check(vmWorkspace.objOrganization?.Id == lngOrganizationId && !vmWorkspace.bPageLoadFailed,
            "Organization view preserves authorized detail after batched member pages");
        lngBefore = AuditReads(objDatabase);
        Execute(vmOrganizations.vmProfile.cmdEdit);
        Check(vmOrganizations.vmProfile.bEditing && vmWorkspace.objOrganization?.Id == lngOrganizationId && !vmWorkspace.bBusy,
            "Organization edit preserves the authorized draft after both batches");
        Console.WriteLine($"Profile measurement Organization edit: {AuditReads(objDatabase)-lngBefore} protected reads.");
        Check(AuditReads(objDatabase) - lngBefore == 8, "Organization edit retains epoch checks with eight protected reads");
    }

    private static void OrganizationFinalEpochChecks(MainWindowViewModel vmWorkspace, SemaphoreSlim semWork,
        Database objDatabase, UserAdministrationService objUsers, long lngViewer, long lngContactId, long lngOrganizationId)
    {
        Execute(vmWorkspace.cmdOrganizations); ReadyRows(vmWorkspace);
        var vmOrganizations = (OrganizationsPageViewModel)vmWorkspace.vmShell.vmCurrentPage;
        var cmdProfile = (AsyncRelayCommand)vmOrganizations.lstRows.Single().cmdViewProfile;
        var lngBefore = AuditReads(objDatabase);
        PumpUntil(() => semWork.Wait(0));
        var bHeld = true;
        try
        {
            cmdProfile.Execute(null); Dispatcher.UIThread.RunJobs();
            // Queue one gate behind each of the three data reads, then change permissions
            // while the existing final epoch/record-grant validator is waiting.
            for (var iRead = 0; iRead < 3; iRead++)
            {
                var tskGate = semWork.WaitAsync();
                semWork.Release(); bHeld = false;
                PumpUntil(() => tskGate.IsCompletedSuccessfully); bHeld = true;
                Dispatcher.UIThread.RunJobs();
            }
            Check(AuditReads(objDatabase) - lngBefore == 3 && vmWorkspace.objOrganization?.Name == ""
                && vmWorkspace.lstOrganizationContacts.Count == 0,
                "Organization profile stages three audited reads without publishing before final validation");
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, lngContactId, false, false);
            semWork.Release(); bHeld = false; Complete(cmdProfile.tskExecution);
            Check(vmWorkspace.bPageLoadFailed && vmWorkspace.objOrganization?.Name == "" && vmWorkspace.lstOrganizationContacts.Count == 0,
                "Organization final validation rejects a member permission epoch change before publication");
        }
        finally
        {
            if (bHeld) semWork.Release();
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, lngContactId, true, false);
        }
        Execute(vmWorkspace.cmdReloadPage);
        Check(!vmWorkspace.bPageLoadFailed && vmWorkspace.objOrganization?.Id == lngOrganizationId,
            "Organization profile reload succeeds after epoch rejection");
        // Supersede an organization request before its first read; no late member
        // page or record can refill the destination after the route changes.
        PumpUntil(() => semWork.Wait(0));
        try
        {
            vmWorkspace.cmdReloadPage.Execute(null); Dispatcher.UIThread.RunJobs();
            vmWorkspace.cmdContacts.Execute(null);
        }
        finally { semWork.Release(); }
        Complete(((AsyncRelayCommand)vmWorkspace.cmdContacts).tskExecution);
        Complete(((AsyncRelayCommand)vmWorkspace.cmdReloadPage).tskExecution); ReadyRows(vmWorkspace);
        Check(vmWorkspace.strPage == "Contacts" && vmWorkspace.objOrganization is null && vmWorkspace.lstOrganizationContacts.Count == 0,
            "Superseded organization hydration cannot publish its record or member pages");
    }

    private static void FinalEpochChecks(MainWindowViewModel vmWorkspace, SemaphoreSlim semWork, Database objDatabase,
        UserAdministrationService objUsers, long lngViewer, long lngContactId)
    {
        Execute(vmWorkspace.cmdOverview);
        var lngBefore = AuditReads(objDatabase);
        Check(semWork.Wait(0), "Final-validation fixture holds the worker before navigation");
        var bHeld = true;
        try
        {
            vmWorkspace.cmdContacts.Execute(null);
            Dispatcher.UIThread.RunJobs();
            // Queue behind each real staged read. These gates do not change
            // production code and stop the route immediately before validation.
            var tskAfterChoices = semWork.WaitAsync();
            semWork.Release(); bHeld = false;
            PumpUntil(() => tskAfterChoices.IsCompletedSuccessfully); bHeld = true;
            Dispatcher.UIThread.RunJobs();
            var tskAfterRows = semWork.WaitAsync();
            semWork.Release(); bHeld = false;
            PumpUntil(() => tskAfterRows.IsCompletedSuccessfully); bHeld = true;
            Dispatcher.UIThread.RunJobs();
            Check(AuditReads(objDatabase) - lngBefore == 2,
                "Staged Contacts hydration audits its two data reads without intermediate Work epoch reads");
            Check(vmWorkspace.bPageLoading && vmWorkspace.lstContacts.Count == 0,
                "Rows remain unpublished while final validation waits");
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, lngContactId, false, false);
            semWork.Release(); bHeld = false;
            Complete(((AsyncRelayCommand)vmWorkspace.cmdContacts).tskExecution);
            ReadyRows(vmWorkspace);
            Check(vmWorkspace.bPageLoadFailed && !vmWorkspace.bPageLoading && vmWorkspace.lstContacts.Count == 0
                && vmWorkspace.objContact is null && vmWorkspace.objDashboard is null,
                "Final audited epoch validation rejects a permission change after data hydration without publishing stale data");
        }
        finally
        {
            if (bHeld) semWork.Release();
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, lngContactId, true, false);
        }
        Execute(vmWorkspace.cmdReloadPage); ReadyRows(vmWorkspace);
        Check(!vmWorkspace.bPageLoadFailed && vmWorkspace.lstContacts.Any(objRow => objRow.Id == lngContactId),
            "Fresh authorized reload succeeds after the final-validation rejection");
    }

    private static void LateWorkerChecks(MainWindowViewModel vmWorkspace)
    {
        var objCancellation = Field<CancellationTokenSource>(vmWorkspace, "objPageCancellation");
        using var objStarted = new ManualResetEventSlim(); using var objRelease = new ManualResetEventSlim();
        var objLate = new Contact { FirstName = "FICTIONAL canceled native result" };
        var mthWork = typeof(MainWindowViewModel).GetMethod("WorkWithCancellationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(Contact));
        var tskWork = (Task<Contact>)mthWork.Invoke(vmWorkspace, [(Func<Contact>)(() =>
        { objStarted.Set(); if (!objRelease.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return objLate; }), objCancellation.Token])!;
        try
        {
            PumpUntil(() => objStarted.IsSet);
            vmWorkspace.cmdOrganizations.Execute(null);
            Check(vmWorkspace.strPage == "Organizations" && !vmWorkspace.bBusy, "Uninterruptible old work does not delay a new route");
        }
        finally { objRelease.Set(); }
        var bCanceled = false;
        try { Complete(tskWork); } catch (OperationCanceledException) { bCanceled = true; }
        Check(bCanceled && objLate.FirstName == "", "Late canceled worker result is released before ownership can transfer");
        Complete(((AsyncRelayCommand)vmWorkspace.cmdOrganizations).tskExecution);
        Check(vmWorkspace.lstContacts.Count == 0, "Late result cannot refill a different route's CUI cache");
    }

    private static void ProfileChecks(MainWindowViewModel vmWorkspace, SemaphoreSlim semWork, long lngContactId, long lngOrganizationId)
    {
        var vmContacts = (ContactsPageViewModel)vmWorkspace.vmShell.vmCurrentPage;
        var cmdProfile = (AsyncRelayCommand)vmContacts.lstRows.Single().cmdViewProfile;
        PumpUntil(() => semWork.Wait(0));
        try
        {
            cmdProfile.Execute(null);
            Check(vmContacts.bProfile && vmWorkspace.bPageLoading && !vmWorkspace.bBusy, "Contact profile shell switches before its database hydration");
            Check(vmWorkspace.objContact?.FirstName == "" && vmWorkspace.lstInteractions.Count == 0, "Profile skeleton contains no unverified detail or history");
        }
        finally { semWork.Release(); }
        Complete(cmdProfile.tskExecution);
        Check(vmWorkspace.objContact?.Id == lngContactId && vmWorkspace.objContact.FirstName == "FICTIONAL visible navigation record", "Contact profile publishes only the authorized full detail");
        var cmdBack = (AsyncRelayCommand)vmContacts.vmProfile!.cmdBack;
        PumpUntil(() => semWork.Wait(0));
        try
        {
            cmdBack.Execute(null);
            Check(vmContacts.bLookup && vmWorkspace.bPageLoading && !vmWorkspace.bBusy, "Back selects an empty lookup immediately while reloading");
        }
        finally { semWork.Release(); }
        Complete(cmdBack.tskExecution); ReadyRows(vmWorkspace);
        Execute(vmWorkspace.cmdOrganizations); ReadyRows(vmWorkspace);
        var vmOrganizations = (OrganizationsPageViewModel)vmWorkspace.vmShell.vmCurrentPage;
        var cmdOrganization = (AsyncRelayCommand)vmOrganizations.lstRows.Single().cmdViewProfile;
        PumpUntil(() => semWork.Wait(0));
        try
        {
            cmdOrganization.Execute(null);
            Check(vmOrganizations.bProfile && vmWorkspace.objOrganization?.Name == "" && vmWorkspace.bPageLoading && !vmWorkspace.bBusy,
                "Organization profile switches to an empty shell before detail and related-contact checks");
        }
        finally { semWork.Release(); }
        Complete(cmdOrganization.tskExecution);
        Check(vmWorkspace.objOrganization?.Id == lngOrganizationId && vmWorkspace.objOrganization.Name == "FICTIONAL navigation organization", "Organization detail publishes after authorized hydration");
        Execute(vmOrganizations.vmProfile!.cmdBack); ReadyRows(vmWorkspace);
        Check(!vmOrganizations.bProfile, "Organization Back uses the same asynchronous lookup routing");
    }

    private static T Field<T>(object objOwner, string strName) => (T)objOwner.GetType().GetField(strName, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(objOwner)!;
    private static void Password(MainWindowViewModel vmWorkspace, bool bSetup)
    { foreach (var chrValue in "FICTIONAL asynchronous navigation!") { vmWorkspace.secPassword.AppendChar(chrValue); if (bSetup) vmWorkspace.secConfirmation.AppendChar(chrValue); } }
    private static void Execute(System.Windows.Input.ICommand cmdCommand) { cmdCommand.Execute(null); if (cmdCommand is AsyncRelayCommand cmdAsync) Complete(cmdAsync.tskExecution); }
    private static void Complete(Task tskWork) => UiDevelopmentChecks.Complete(tskWork);
    private static void ReadyRows(MainWindowViewModel vmWorkspace)
    {
        if (vmWorkspace.vmShell.vmCurrentPage is ContactsPageViewModel vmContacts) Complete(vmContacts.tskSearch);
        if (vmWorkspace.vmShell.vmCurrentPage is OrganizationsPageViewModel vmOrganizations) Complete(vmOrganizations.tskSearch);
    }
    private static void Sql(Database objDatabase, string strSql)
    { using var dbConnection = (SqliteConnection)typeof(Database).GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(objDatabase, null)!; using var cmdSql = new SqliteCommand(strSql, dbConnection); cmdSql.ExecuteNonQuery(); }
    private static void PumpUntil(Func<bool> fnComplete)
    { var swBudget = Stopwatch.StartNew(); while (!fnComplete()) { Dispatcher.UIThread.RunJobs(); if (swBudget.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Navigation fixture timed out."); Thread.Sleep(1); } }
#endif
    private static long AuditReads(Database objDatabase)
    {
        using var dbConnection = (SqliteConnection)typeof(Database).GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(objDatabase, null)!;
        using var cmdRead = dbConnection.CreateCommand(); cmdRead.CommandText = "SELECT count(*) FROM audit_log WHERE action='AccessVerified'";
        return (long)cmdRead.ExecuteScalar()!;
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
}
