using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.TestSupport;

internal static class CuiPresentationChecks
{
    private static int iPassed;
    internal static void Run()
    {
        AppBuilder.Configure<Application>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SearchChecks(); ExpiredGetterChecks(); BlockedProbeChecks(); BlockedCleanupChecks();
#if DEBUG
        ActiveEditingChecks();
#endif
        Check(!new CrmTextBox().IsUndoEnabled, "CUI text editors do not retain undo history");
        var vmFields = new MultiValueFieldViewModel("FICTIONAL first" + Environment.NewLine + "FICTIONAL second");
        var vmOldEntry = vmFields.lstValues[0]; vmFields.strNewValue = "FICTIONAL draft"; vmFields.Dispose();
        Check(vmFields.lstValues.Count == 0 && vmFields.strNewValue == "" && vmOldEntry.strValue == "", "Disposed multi-value editor clears its original, entry and draft references");
        var objBefore = new Contact { FirstName = "FICTIONAL submitted" };
        var objCurrent = new Contact { FirstName = "FICTIONAL newer input" };
        var objCommitted = new Contact { Id = 42, Revision = 1, FirstName = "FICTIONAL submitted", Email = "fixture@example.test" };
        typeof(MainWindowViewModel).GetMethod("MergeCommittedRecord", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(Contact)).Invoke(null, [objCurrent, objBefore, objCommitted]);
        Check(objCurrent.Id == 42 && objCurrent.Revision == 1 && objCurrent.Email == "fixture@example.test" && objCurrent.FirstName == "FICTIONAL newer input",
            "Save acknowledgement retains persisted identity and normalized fields without overwriting newer draft input");
        Console.WriteLine($"{iPassed} CUI presentation/cancellation checks passed.");
    }
    private static void SearchChecks()
    {
        var bAuthorized = true;
        var objCancellation = new CancellationTokenSource();
        var objContact = new Contact { FirstName = "FICTIONAL pending" };
        var objLoadCompletion = new TaskCompletionSource<MasterSearchSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vmSearch = new MasterSearchViewModel(() => objLoadCompletion.Task, () => bAuthorized,
            ctCancellation => Task.FromResult(bAuthorized), () => objCancellation.Token);
        vmSearch.cmdSearch.Execute(null);
        Check(vmSearch.bSearching, "Search waits asynchronously for a page");
        vmSearch.Reset();
        Complete(((AsyncRelayCommand)vmSearch.cmdSearch).tskExecution);
        Check(vmSearch.lstResults.Count == 0 && !vmSearch.bSearching, "Reset cancels the search without waiting for its provider");
        objLoadCompletion.SetResult(new MasterSearchSnapshot([objContact], []));
        PumpUntil(() => objContact.FirstName.Length == 0);
        Check(vmSearch.lstResults.Count == 0 && objContact.FirstName == "", "A late canceled page is discarded and its owned fields cleared");
        var objDenied = new Contact { FirstName = "FICTIONAL denied" };
        var vmDenied = new MasterSearchViewModel(() => Task.FromResult<MasterSearchSnapshot?>(new([objDenied], [])),
            () => true, ctCancellation => Task.FromResult(false), () => CancellationToken.None);
        vmDenied.cmdSearch.Execute(null); Complete(((AsyncRelayCommand)vmDenied.cmdSearch).tskExecution);
        Check(vmDenied.lstResults.Count == 0 && objDenied.FirstName == "", "Publication revalidates current authorization and clears rejected projections");
        var objValidation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bValidating = false;
        var vmRevalidating = new MasterSearchViewModel(() => Task.FromResult<MasterSearchSnapshot?>(new([new Contact { FirstName = "FICTIONAL queued" }], [])),
            () => true, ctCancellation => { bValidating = true; return objValidation.Task; }, () => CancellationToken.None);
        vmRevalidating.cmdSearch.Execute(null); PumpUntil(() => bValidating);
        vmRevalidating.Reset(); objValidation.SetResult(true); Complete(((AsyncRelayCommand)vmRevalidating.cmdSearch).tskExecution);
        Check(vmRevalidating.lstResults.Count == 0, "Reset during asynchronous publication validation cannot resurrect an old page");
        var vmCached = new MasterSearchViewModel(() => Task.FromResult<MasterSearchSnapshot?>(new([new Contact { FirstName = "FICTIONAL cached" }], [])),
            () => bAuthorized, ctCancellation => Task.FromResult(bAuthorized), () => objCancellation.Token);
        vmCached.cmdSearch.Execute(null); Complete(((AsyncRelayCommand)vmCached.cmdSearch).tskExecution);
        Check(vmCached.lstResults.Count == 1, "Authorized search can publish a bounded result");
        vmCached.strSearchText = "FICTIONAL pending search text"; vmCached.InvalidateCache();
        Check(vmCached.strSearchText == "FICTIONAL pending search text", "Background cache invalidation preserves pending search input");
        bAuthorized = false;
        Check(vmCached.lstResults.Count == 0 && vmCached.lstOrganizations.Count == 0, "Every cache getter rejects an invalidated issuing scope");
        objCancellation.Cancel(); vmCached.Reset(); objCancellation.Dispose();
    }
    private static void BlockedCleanupChecks()
    {
        var vmWorkspace = new MainWindowViewModel(); Complete(vmWorkspace.tskInitialization);
        Check(vmWorkspace.strPage == "Error", "Cleanup fixture preserves normal production provisioning denial");
        var objClock = new ManualSessionClock();
        using var objSession = (Session)Activator.CreateInstance(typeof(Session), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [1L, "FICTIONAL blocked", objClock], null)!;
        using var objLease = objSession.CreateCuiMemoryLease(0);
        SetField(vmWorkspace, "objSession", objSession); SetField(vmWorkspace, "objCuiLease", objLease);
        var objOwned = new Contact { FirstName = "FICTIONAL owned" };
        SetField(vmWorkspace, "objCurrentContact", objOwned);
        SetField(vmWorkspace, "lstCachedContacts", new List<Contact> { objOwned });
        var objStarted = new ManualResetEventSlim(); var objRelease = new ManualResetEventSlim();
        var objLate = new Contact { FirstName = "FICTIONAL late native result" };
        var mthWork = typeof(MainWindowViewModel).GetMethod("WorkAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(typeof(Contact));
        var tskWorker = (Task<Contact>)mthWork.Invoke(vmWorkspace, [(Func<Contact>)(() =>
        { objStarted.Set(); if (!objRelease.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return objLate; })])!;
        PumpUntil(() => objStarted.IsSet);
        typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.tskPendingOperation))!.SetValue(vmWorkspace, tskWorker);
        var swCleanup = Stopwatch.StartNew(); vmWorkspace.cmdLock.Execute(null);
        Check(vmWorkspace.lstContacts.Count == 0 && vmWorkspace.objContact is null && objOwned.FirstName == "", "Lock clears owned CUI before a blocked database worker completes");
        Complete(((AsyncRelayCommand)vmWorkspace.cmdLock).tskExecution);
        Check(swCleanup.Elapsed < TimeSpan.FromSeconds(4) && vmWorkspace.strPage == "Error", "Cleanup supervision reaches a closed recovery screen within its deadline");
        Check(!vmWorkspace.RecordActivity() && !vmWorkspace.CanReadCui(), "Cleanup timeout cannot renew a session or reopen the workspace");
        objRelease.Set();
        try { Complete(tskWorker); throw new InvalidOperationException("Late worker was accepted."); }
        catch (OperationCanceledException) { }
        Check(objLate.FirstName == "" && vmWorkspace.lstContacts.Count == 0, "Late worker result is cleared and cannot repopulate a locked view");
        Complete(vmWorkspace.CloseAsync()); objStarted.Dispose(); objRelease.Dispose();
    }
    private static void ExpiredGetterChecks()
    {
        var vmWorkspace = new MainWindowViewModel(); Complete(vmWorkspace.tskInitialization);
        var objClock = new ManualSessionClock();
        using var objSession = (Session)Activator.CreateInstance(typeof(Session), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [1L, "FICTIONAL expired getter", objClock], null)!;
        using var objLease = objSession.CreateCuiMemoryLease(0);
        var objOwned = new Contact { FirstName = "FICTIONAL before expiry" };
        SetField(vmWorkspace, "objSession", objSession); SetField(vmWorkspace, "objCuiLease", objLease);
        SetField(vmWorkspace, "lstCachedContacts", new List<Contact> { objOwned });
        SetField(vmWorkspace, "objCurrentContact", objOwned);
        objClock.lngTicks = CuiMemoryPolicy.CacheFreshnessLifetime.Ticks;
        Check(vmWorkspace.lstContacts.Count == 0 && ReferenceEquals(vmWorkspace.objContact, objOwned) && objOwned.FirstName == "FICTIONAL before expiry"
            && !objSession.IsExpired && objLease.NeedsRefresh,
            "Stale lookup cache is withheld while the active record and session survive freshness expiry");
        objClock.lngTicks = Session.InactivityLimit.Ticks;
        Check(vmWorkspace.lstContacts.Count == 0 && objOwned.FirstName == "" && objSession.IsExpired,
            "The exact 15-minute idle deadline still locks and clears working state");
        Complete(vmWorkspace.CloseAsync());
    }
#if DEBUG
    private static void ActiveEditingChecks()
    {
        using var objDatabase = Database.CreateUiDevelopmentWorkspace();
        var objClock = new ManualSessionClock();
        var objAuthentication = (AuthService)Activator.CreateInstance(typeof(AuthService), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [objDatabase, objClock], null)!;
        using var secPassword = TestPasswords.Secret("FICTIONAL active editing password!");
        using var objSession = objAuthentication.CreateAdministrator("active-editor", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objSession); objUsers.SetCuiAuthorization(objSession.UserId, true);
        var objCrm = new CrmService(objDatabase, objSession);
        var objSaved = new Contact { FirstName = "FICTIONAL stored" }; objCrm.Save(objSaved);
        var vmWorkspace = new MainWindowViewModel(); Complete(vmWorkspace.tskInitialization);
        Check(vmWorkspace.strPage == "Error", "Active-edit fixture retains normal production provisioning denial");
        var objDraft = new Contact { Id = objSaved.Id, Revision = objSaved.Revision, FirstName = "FICTIONAL unsaved edit" };
        using var objLease = objSession.CreateCuiMemoryLease(objCrm.CuiEpoch());
        SetField(vmWorkspace, "objDatabase", objDatabase); SetField(vmWorkspace, "objAuthentication", objAuthentication);
        SetField(vmWorkspace, "objCrm", objCrm); SetField(vmWorkspace, "objSession", objSession); SetField(vmWorkspace, "objCuiLease", objLease);
        SetField(vmWorkspace, "objCurrentContact", objDraft); SetField(vmWorkspace, "strCurrentPage", "Contacts");
        typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.bCanEditContact))!.SetValue(vmWorkspace, true);
        vmWorkspace.vmNewContactEmails.strNewValue = "FICTIONAL unsaved email entry"; vmWorkspace.strNote = "FICTIONAL unsaved note";
        var vmContacts = (ContactsPageViewModel)typeof(ShellViewModel).GetField("vmContacts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vmWorkspace.vmShell)!;
        var vmEditor = new EditContactViewModel(vmWorkspace, objSaved, objContact => { }, () => { });
        vmEditor.vmEmails.strNewValue = "FICTIONAL editor input";
        SetField(vmContacts, "vmCurrentEdit", vmEditor); SetField(vmWorkspace.vmShell, "vmCurrent", vmContacts);
        var bUnexpectedDiscardPrompt = false;
        vmWorkspace.fnConfirmDiscard = () => { bUnexpectedDiscardPrompt = true; return Task.FromResult(true); };
        var bPreserved = true;
        for (var iMinute = 0; iMinute < 30; iMinute++)
        {
            objClock.Advance(TimeSpan.FromSeconds(61)); bPreserved &= vmWorkspace.RecordActivity();
            var tskRefresh = vmWorkspace.ValidateCuiScopeAsync(objLease.Token); Complete(tskRefresh);
            bPreserved &= tskRefresh.Result && ReferenceEquals(vmWorkspace.objContact, objDraft) && !objSession.IsExpired;
        }
        Check(bPreserved && objDraft.FirstName == "FICTIONAL unsaved edit" && vmWorkspace.strNote == "FICTIONAL unsaved note"
            && vmWorkspace.vmNewContactEmails.strNewValue == "FICTIONAL unsaved email entry",
            "Thirty minutes of active editing and real authorization refresh preserve the session, record draft and field drafts");
        var lngOldEpoch = objLease.DatabaseEpoch;
        var lngOtherUser = objUsers.CreateUser("unrelated-viewer", secPassword, UserRole.Viewer);
        objUsers.SetRecordAccess(lngOtherUser, RecordKind.Contact, objSaved.Id, true, false);
        var tskChanged = vmWorkspace.ValidateCuiScopeAsync(objLease.Token); Complete(tskChanged);
        Check(tskChanged.Result && objLease.DatabaseEpoch > lngOldEpoch && ReferenceEquals(vmWorkspace.objContact, objDraft)
            && objDraft.FirstName == "FICTIONAL unsaved edit", "An unrelated committed permission change silently renews the epoch without discarding the active draft");
        vmWorkspace.vmShell.SelectContact(null); Dispatcher.UIThread.RunJobs();
        Check(!bUnexpectedDiscardPrompt && ReferenceEquals(vmWorkspace.objContact, objDraft) && ReferenceEquals(vmContacts.vmEdit, vmEditor)
            && vmEditor.vmEmails.strNewValue == "FICTIONAL editor input",
            "Cache-driven lookup deselection cannot prompt for discard, close an editor or clear its pending entry");
        var objCommitted = new Contact { FirstName = "FICTIONAL acknowledged save" };
        var mthWork = typeof(MainWindowViewModel).GetMethod("WorkAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(typeof(long));
        var tskAcknowledged = (Task<long>)mthWork.Invoke(vmWorkspace, [(Func<long>)(() =>
        {
            var lngId = objCrm.Save(objCommitted);
            objUsers.SetRecordAccess(lngOtherUser, RecordKind.Contact, objSaved.Id, true, false);
            return lngId;
        })])!;
        Complete(tskAcknowledged);
        Check(tskAcknowledged.Result == objCommitted.Id && objCommitted.Id > 0 && !objSession.IsExpired,
            "A committed save acknowledgement survives a concurrent permission epoch change without terminating editing");
        var tskAfterSave = vmWorkspace.ValidateCuiScopeAsync(objLease.Token); Complete(tskAfterSave);
        objLease.MarkStale();
        var semDatabase = (SemaphoreSlim)typeof(MainWindowViewModel).GetField("semDatabaseWork", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vmWorkspace)!;
        semDatabase.Wait();
        try
        {
            Complete(vmWorkspace.ProbeCuiAuthorizationAsync());
            Check(!objSession.IsExpired && ReferenceEquals(vmWorkspace.objContact, objDraft) && objDraft.FirstName == "FICTIONAL unsaved edit",
                "A real queued authorization probe timeout preserves active editing and its draft");
        }
        finally { semDatabase.Release(); }
        PumpUntil(() => !objLease.NeedsRefresh);
        objUsers.SetCuiAuthorization(objSession.UserId, false);
        var tskRevoked = vmWorkspace.ValidateCuiScopeAsync(objLease.Token); Complete(tskRevoked);
        Check(!tskRevoked.Result && vmWorkspace.objContact is null && objSession.IsExpired,
            "Confirmed CUI revocation still fails closed and cannot renew an active session");
        Complete(vmWorkspace.CloseAsync());
    }
#endif
    private static void BlockedProbeChecks()
    {
        var vmWorkspace = new MainWindowViewModel(); Complete(vmWorkspace.tskInitialization);
        using var objSession = (Session)Activator.CreateInstance(typeof(Session), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [1L, "FICTIONAL probe", new ManualSessionClock()], null)!;
        using var objLease = objSession.CreateCuiMemoryLease(0);
        SetField(vmWorkspace, "objSession", objSession); SetField(vmWorkspace, "objCuiLease", objLease);
        var semDatabase = (SemaphoreSlim)typeof(MainWindowViewModel).GetField("semDatabaseWork", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vmWorkspace)!;
        semDatabase.Wait();
        try
        {
            Complete(vmWorkspace.ProbeCuiAuthorizationAsync());
            Check(vmWorkspace.CanReadCui() && !objSession.IsExpired, "Blocked background probe does not terminate an active session");
        }
        finally { semDatabase.Release(); }
        Complete(vmWorkspace.CloseAsync());
    }
    private static void SetField(object objOwner, string strName, object objValue) =>
        objOwner.GetType().GetField(strName, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(objOwner, objValue);
    private static void Complete(Task tskWork)
    {
        PumpUntil(() => tskWork.IsCompleted); tskWork.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    private static void PumpUntil(Func<bool> fnComplete)
    {
        var swWait = Stopwatch.StartNew();
        while (!fnComplete() && swWait.Elapsed < TimeSpan.FromSeconds(10)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        if (!fnComplete()) throw new TimeoutException("CUI presentation check timed out.");
        Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
}
