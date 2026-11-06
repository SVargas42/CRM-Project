using System.Security.Cryptography;
using System.Windows.Input;
using HippoCrm.Core;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? objPageCancellation;
    private int iPageRevision;
    private bool bCurrentPageLoading;
    private string strCurrentPageLoadError = "";
    private readonly List<Task> lstPageHydrations = [];
    private ICommand? cmdCurrentReloadPage;
    private RecordKind? enPageRecordKind;
    private long lngPageRecordId;
    private Action<CuiProtectedRecord>? actPageRecordShell;
    public Task tskPageHydration { get; private set; } = Task.CompletedTask;
    public bool bPageLoading => bCurrentPageLoading;
    public bool bPageLoadFailed => strCurrentPageLoadError.Length != 0;
    public string strPageLoadError => strCurrentPageLoadError;
    public ICommand cmdReloadPage => cmdCurrentReloadPage ??= new AsyncRelayCommand(() => NavigateAsync(strPage, enPageRecordKind, lngPageRecordId, actPageRecordShell), () => !bBusy && !bLocking && !bClosed);

    internal Task NavigateRecordAsync(RecordKind enKind, long lngId, Action<CuiProtectedRecord> actEmptyShell) =>
        NavigateAsync(enKind == RecordKind.Contact ? "Contacts" : "Organizations", enKind, lngId, actEmptyShell);

    internal ICommand NavigationCommand(string strDestination, Func<bool>? fnAllowed = null)
    {
        var cmdNavigation = new AsyncRelayCommand(() => NavigateAsync(strDestination),
            () => !bBusy && !bLocking && !bClosed && (fnAllowed?.Invoke() ?? true));
        lstAsyncCommands.Add(new WeakReference<AsyncRelayCommand>(cmdNavigation));
        return cmdNavigation;
    }

    private void SetPageLoadState(bool bLoading, string strError = "")
    {
        bCurrentPageLoading = bLoading; strCurrentPageLoadError = strError;
        Notify(nameof(bPageLoading)); Notify(nameof(bPageLoadFailed)); Notify(nameof(strPageLoadError));
        foreach (var wrCommand in lstAsyncCommands)
            if (wrCommand.TryGetTarget(out var cmdAsync)) cmdAsync.Refresh();
    }

    private void CancelPageHydration()
    {
        iPageRevision++;
        try { objPageCancellation?.Cancel(); }
        catch (AggregateException) { /* An observer cannot prevent route invalidation or locking. */ }
        finally { objPageCancellation?.Dispose(); objPageCancellation = null; }
        enPageRecordKind = null; lngPageRecordId = 0; actPageRecordShell = null;
    }

    private async Task NavigateAsync(string strDestination, RecordKind? enRecordKind = null, long lngRecordId = 0, Action<CuiProtectedRecord>? actEmptyShell = null)
    {
        if (strDestination is not ("Overview" or "Contacts" or "Organizations" or "AuditLogs")) return;
        if (!CheckSession() || objSession is null || objCuiLease is null || bBusy || bClosed || bLocking) return;
        if (strDestination == "AuditLogs" && !bCanCreate) return;
        if (!await ConfirmDiscardAsync() || !CheckSession() || bBusy || bClosed || bLocking) return;

        CancelPageHydration();
        enPageRecordKind = enRecordKind; lngPageRecordId = lngRecordId; actPageRecordShell = actEmptyShell;
        objPageCancellation = CancellationTokenSource.CreateLinkedTokenSource(CuiToken);
        var ctRequest = objPageCancellation.Token;
        var iRevision = iPageRevision;
        var objIssuingSession = objSession; var objIssuingLease = objCuiLease; var objIssuingCrm = objCrm;
        if (objIssuingSession is null || objIssuingLease is null || objIssuingCrm is null) return;
        SetPageLoadState(true); strMessage = "";
        ClearNavigationPresentation();
        iLookupPageOffset = 0; Notify(nameof(strLookupPageScope));
        strSearch = ""; strSkill = ""; bArchived = false; iAvailability = 0;
        iContactStatus = 0;

        // Only local state precedes routing. No database, audit or authorization
        // request is awaited before the destination's empty shell is selected.
        var bSamePage = strPage == strDestination;
        strPage = strDestination;
        if (bSamePage) Notify(nameof(strPage));
        if (enRecordKind == RecordKind.Contact)
        {
            objCurrentContact = new Contact { Id = lngRecordId };
            objContactBaseline = CopyRecord(objCurrentContact); actEmptyShell?.Invoke(objCurrentContact);
        }
        else if (enRecordKind == RecordKind.Organization)
        {
            objCurrentOrganization = new Organization { Id = lngRecordId };
            objOrganizationBaseline = CopyRecord(objCurrentOrganization); actEmptyShell?.Invoke(objCurrentOrganization);
        }
        NotifyNavigationData();
        tskPageHydration = HydratePageAsync(strDestination, iRevision, objIssuingSession, objIssuingLease, objIssuingCrm, ctRequest, enRecordKind, lngRecordId);
        lstPageHydrations.RemoveAll(tskWork => tskWork.IsCompleted);
        lstPageHydrations.Add(tskPageHydration);
        // Preserve existing awaitable command/cleanup behavior without a global busy gate.
        tskPendingOperation = tskPageHydration;
        await tskPageHydration;
    }

    private bool IsCurrentPage(string strDestination, int iRevision, Session objIssuingSession,
        CuiMemoryLease objIssuingLease, CrmService objIssuingCrm, CancellationToken ctRequest) =>
        !ctRequest.IsCancellationRequested && iRevision == iPageRevision && strPage == strDestination
        && !bClosed && !bLocking && ReferenceEquals(objSession, objIssuingSession)
        && ReferenceEquals(objCuiLease, objIssuingLease) && ReferenceEquals(objCrm, objIssuingCrm)
        && objIssuingLease.IsCurrent(objIssuingSession);

    private async Task HydratePageAsync(string strDestination, int iRevision, Session objIssuingSession,
        CuiMemoryLease objIssuingLease, CrmService objIssuingCrm, CancellationToken ctRequest, RecordKind? enRecordKind, long lngRecordId)
    {
        using var objData = new PageHydrationData();
        bool Current() => IsCurrentPage(strDestination, iRevision, objIssuingSession, objIssuingLease, objIssuingCrm, ctRequest);
        try
        {
            await Task.Yield();
            ctRequest.ThrowIfCancellationRequested();
            if (!Current()) return;
            if (objIssuingLease.NeedsRefresh && !await ValidateCuiScopeAsync(ctRequest))
            {
                if (Current()) SetPageLoadState(false, "This page could not be verified. Reload it to try again.");
                return;
            }
            var lngEpoch = objIssuingLease.DatabaseEpoch; var lngLocalEpoch = CuiAuthorizationEpoch.Current;
            if (enRecordKind == RecordKind.Contact)
            {
                objData.objContact = await WorkWithCancellationAsync(() => objIssuingCrm.ContactRecord(lngRecordId), ctRequest);
                objData.objContactEditor = await WorkWithCancellationAsync(() => objIssuingCrm.ContactEditor(lngRecordId), ctRequest);
            }
            else if (enRecordKind == RecordKind.Organization)
            {
                objData.objOrganization = await WorkWithCancellationAsync(() => objIssuingCrm.OrganizationRecord(lngRecordId), ctRequest);
                objData.bCanWriteOrganization = await WorkWithCancellationAsync(() => objIssuingCrm.CanWriteOrganization(lngRecordId), ctRequest);
                objData.objActiveRelated = await WorkWithCancellationAsync(() => objIssuingCrm.LookupPage(ctCancellation: ctRequest, lngOrganizationId: lngRecordId), ctRequest);
                objData.objArchivedRelated = await WorkWithCancellationAsync(() => objIssuingCrm.LookupPage(ctCancellation: ctRequest, lngOrganizationId: lngRecordId, bArchived: true), ctRequest);
            }
            else if (strDestination == "Overview")
                objData.objDashboard = await WorkWithCancellationAsync(() => objIssuingCrm.Dashboard(), ctRequest);
            else if (strDestination == "Contacts")
            {
                objData.lstChoices = await WorkWithCancellationAsync(() => objIssuingCrm.AllOrganizations(bNamesOnly: true), ctRequest);
                objData.lstContacts = await WorkWithCancellationAsync(() => objIssuingCrm.Contacts(iLimit: CuiMemoryPolicy.iPageSize), ctRequest);
            }
            else if (strDestination == "Organizations")
                objData.lstOrganizations = await WorkWithCancellationAsync(() => objIssuingCrm.Organizations(iLimit: CuiMemoryPolicy.iPageSize), ctRequest);

            // A final live check cannot retroactively authorize an older snapshot.
            // Require the very same epoch at load and publication, including local changes.
            if (!Current()) return;
            var bVerified = await ValidateCuiScopeAsync(ctRequest);
            if (!Current()) return;
            if (!bVerified || objIssuingLease.NeedsRefresh || lngEpoch != objIssuingLease.DatabaseEpoch || lngLocalEpoch != CuiAuthorizationEpoch.Current)
            { SetPageLoadState(false, "Access changed while this page was loading. Reload it to try again."); return; }

            lstContactOrganizations = OrganizationOptions(objData.objContactEditor?.Organizations ?? objData.lstChoices);
            lstCachedContacts = objData.lstContacts; objData.lstContacts = [];
            lstCachedOrganizations = objData.lstOrganizations; objData.lstOrganizations = [];
            objCachedDashboard = objData.objDashboard; objData.objDashboard = null;
            if (objData.objContact is not null && objData.objContactEditor is not null)
            {
                CuiMemoryPolicy.Release(objCurrentContact); CuiMemoryPolicy.Release(objContactBaseline);
                objCurrentContact = objData.objContact; objData.objContact = null;
                objContactBaseline = CopyRecord(objCurrentContact);
                bCanEditContact = objData.objContactEditor.CanEdit;
                ApplyContactEditorData(objData.objContactEditor); objData.objContactEditor = null;
                RefreshOrganizationSelections();
            }
            else if (objData.objOrganization is not null)
            {
                CuiMemoryPolicy.Release(objCurrentOrganization); CuiMemoryPolicy.Release(objOrganizationBaseline);
                objCurrentOrganization = objData.objOrganization; objData.objOrganization = null;
                objOrganizationBaseline = CopyRecord(objCurrentOrganization); bCanEditOrganization = objData.bCanWriteOrganization;
                lstCachedOrganizationContacts = (objData.objActiveRelated?.lstContacts ?? []).Concat(objData.objArchivedRelated?.lstContacts ?? []).ToArray();
                CuiMemoryPolicy.Release(objData.objActiveRelated?.lstOrganizations); CuiMemoryPolicy.Release(objData.objArchivedRelated?.lstOrganizations);
                objData.objActiveRelated = null; objData.objArchivedRelated = null;
            }
            bReadCachesInvalid = false;
            SetPageLoadState(false);
            NotifyNavigationData();
        }
        catch (OperationCanceledException)
        {
            if (Current()) SetPageLoadState(false, "Access changed while this page was loading. Reload it to try again.");
        }
        catch (Exception objException) when (objException is UnauthorizedAccessException or CryptographicException)
        {
            // Integrity/authorization denial is a workspace lock, never a stale fallback.
            if (ReferenceEquals(objSession, objIssuingSession) && !bClosed && !bLocking)
            { objIssuingLease.Invalidate(); _ = LockAsync(); }
        }
        catch (Exception objException) when (objException is IOException or InvalidOperationException or SqliteException)
        {
            if (Current()) SetPageLoadState(false, "This page could not be loaded. Check local access and reload it.");
        }
        catch (Exception)
        {
            if (Current()) SetPageLoadState(false, "This page could not be loaded. Reload it to try again.");
        }
        finally
        {
            if (Current() && bPageLoading) SetPageLoadState(false, "This page could not be verified. Reload it to try again.");
        }
    }

    private void ClearNavigationPresentation()
    {
        // No mutation can be active when routing starts. Release owned working
        // copies and cancel row/search owners before a different page can render.
        vmShell.InvalidateCuiReadCaches();
        vmShell.ResetNavigationSearch();
        CuiMemoryPolicy.Release(objCurrentContact); CuiMemoryPolicy.Release(objCurrentOrganization);
        CuiMemoryPolicy.Release(objContactBaseline); CuiMemoryPolicy.Release(objOrganizationBaseline);
        CuiMemoryPolicy.Release(lstCachedContacts); CuiMemoryPolicy.Release(lstCachedOrganizations);
        CuiMemoryPolicy.Release(lstCachedOrganizationContacts); CuiMemoryPolicy.Release(lstCachedEmployers);
        CuiMemoryPolicy.Release(objTokenChoiceCatalog);
        objCurrentContact = null; objCurrentOrganization = null; objContactBaseline = null; objOrganizationBaseline = null;
        lstCachedContacts = []; lstCachedOrganizations = []; lstCachedOrganizationContacts = []; lstCachedEmployers = [];
        objCachedDashboard = null; objTokenChoiceCatalog = null; lstContactOrganizations = []; lstOrganizationSelections = [];
        lstCachedInteractions = []; lstCachedEmployment = []; lstCachedContracts = [];
        bCanEditContact = false; bCanEditOrganization = false;
        bOrganizationLookup = false; strOrganizationSearch = ""; objOrganizationToAdd = null;
        ResetNewContactDetails(); ResetNewOrganizationDetails(); ClearHistoryInputs();
    }

    private void NotifyNavigationData()
    {
        Notify(nameof(lstContacts)); Notify(nameof(lstOrganizations)); Notify(nameof(lstContactOrganizations));
        Notify(nameof(lstOrganizationContacts)); Notify(nameof(lstEmployers)); Notify(nameof(objDashboard));
        Notify(nameof(objContact)); Notify(nameof(objOrganization)); Notify(nameof(lstInteractions));
        Notify(nameof(lstEmployment)); Notify(nameof(lstContracts)); Notify(nameof(bCanEditContact)); Notify(nameof(bCanEditOrganization));
        Notify(nameof(bSavedContact)); Notify(nameof(bSavedOrganization)); Notify(nameof(bCanEditContactHistory));
        Notify(nameof(strSearch)); Notify(nameof(strSkill)); Notify(nameof(bArchived)); Notify(nameof(iAvailability));
        Notify(nameof(iContactStatus)); NotifyTokenLists();
    }

    private sealed class PageHydrationData : IDisposable
    {
        internal IReadOnlyList<Contact> lstContacts = [];
        internal IReadOnlyList<Organization> lstOrganizations = [];
        internal IReadOnlyList<Organization> lstChoices = [];
        internal Dashboard? objDashboard;
        internal Contact? objContact;
        internal Organization? objOrganization;
        internal ContactEditorData? objContactEditor;
        internal CuiLookupPage? objActiveRelated;
        internal CuiLookupPage? objArchivedRelated;
        internal bool bCanWriteOrganization;
        public void Dispose()
        {
            CuiMemoryPolicy.Release(lstContacts); CuiMemoryPolicy.Release(lstOrganizations); CuiMemoryPolicy.Release(lstChoices);
            CuiMemoryPolicy.Release(objContact); CuiMemoryPolicy.Release(objOrganization); CuiMemoryPolicy.Release(objContactEditor);
            CuiMemoryPolicy.Release(objActiveRelated); CuiMemoryPolicy.Release(objArchivedRelated);
            lstContacts = []; lstOrganizations = []; lstChoices = []; objDashboard = null;
            objContact = null; objOrganization = null; objContactEditor = null; objActiveRelated = null; objArchivedRelated = null;
        }
    }
}
