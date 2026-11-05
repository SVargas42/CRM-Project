using HippoCrm.Core;
using System.Windows.Input;

namespace HippoCrm.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static void MergeCommittedRecord<T>(T objDraft, T objBefore, T objCommitted) where T : class
    {
        foreach (var objProperty in typeof(T).GetProperties().Where(objProperty => objProperty.CanWrite))
        {
            var objCurrent = objProperty.GetValue(objDraft); var objOriginal = objProperty.GetValue(objBefore);
            var bUnchanged = objCurrent is List<long> lstCurrent && objOriginal is List<long> lstOriginal
                ? lstCurrent.SequenceEqual(lstOriginal) : Equals(objCurrent, objOriginal);
            if (objProperty.Name is not ("Id" or "Revision") && !bUnchanged) continue;
            var objSaved = objProperty.GetValue(objCommitted);
            objProperty.SetValue(objDraft, objSaved is List<long> lstSaved ? lstSaved.ToList() : objSaved);
        }
    }
    private CuiMemoryLease? objCuiLease;
    private CancellationTokenSource objPresentationCancellation = new();
    private CancellationTokenRegistration objLeaseRegistration;
    private readonly SynchronizationContext? objPresentationContext = SynchronizationContext.Current;
    private Timer? objAuthorizationTimer;
    private int iAuthorizationProbe;
    private int iMasterPageOffset = 0;
    private int iLookupPageOffset = 0;
    private bool bReadCachesInvalid;
    private ICommand? cmdCurrentNextLookup;
    private ICommand? cmdCurrentPreviousLookup;
    public ICommand cmdNextLookupPage => cmdCurrentNextLookup ??= Command(() => ChangeLookupPageAsync(1));
    public ICommand cmdPreviousLookupPage => cmdCurrentPreviousLookup ??= Command(() => ChangeLookupPageAsync(-1));
    public string strLookupPageScope => $"Page {iLookupPageOffset / CuiMemoryPolicy.iPageSize + 1}; up to 128 records. Text search filters this page.";
    private async Task ChangeLookupPageAsync(int iDirection)
    {
        if (!await ConfirmDiscardAsync()) return;
        iLookupPageOffset = checked(Math.Max(0, iLookupPageOffset + iDirection * CuiMemoryPolicy.iPageSize));
        await RefreshListsAsync(); Notify(nameof(strLookupPageScope));
    }
    public void SetMasterPageOffset(int iOffset) { if (iOffset < 0) throw new ArgumentOutOfRangeException(nameof(iOffset)); iMasterPageOffset = iOffset; }
    public CancellationToken CuiToken => objCuiLease?.Token ?? new CancellationToken(true);
    public long CuiCacheEpoch => objCuiLease?.DatabaseEpoch ?? -1;

    private async Task StartCuiScopeAsync()
    {
        var lngEpoch = await WorkAsync(() => Crm.CuiEpoch());
        if (objSession is null || objSession.IsExpired) throw new UnauthorizedAccessException();
        objCuiLease = objSession.CreateCuiMemoryLease(lngEpoch);
        objLeaseRegistration = objCuiLease.Token.Register(RequestCuiCheck);
        objAuthorizationTimer = new Timer(objState => RequestAuthorizationProbe(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void RequestCuiCheck()
    {
        if (objPresentationContext is not null) objPresentationContext.Post(objState => CheckInactivity(), null);
        else fnRequestSessionCheck?.Invoke();
    }
    private void RequestAuthorizationProbe()
    {
        // Presentation mutations are confined to its context. No synchronous native
        // database call is made from a binding getter or from a timer callback.
        if (objPresentationContext is not null)
            objPresentationContext.Post(async objState => await ProbeCuiAuthorizationAsync(), null);
        else RequestCuiCheck();
    }
    public bool CanReadCui()
    {
        if (bClosed || bLocking || objSession is null || objCuiLease is null) return false;
        if (objCuiLease.IsCurrent(objSession)) return true;
        _ = LockAsync(); return false;
    }
    public bool CanReadCuiCache() => CanReadCui() && objCuiLease is { NeedsRefresh: false };
    public async Task<bool> ValidateCuiScopeAsync(CancellationToken ctCancellation)
    {
        var objLease = objCuiLease;
        if (!CanReadCui() || objLease is null) return false;
        var objCrmToValidate = objCrm;
        if (objCrmToValidate is null) return false;
        var lngContact = objCurrentContact?.Id; var lngOrganization = objCurrentOrganization?.Id;
        var bContactWrite = bCanEditContact; var bOrganizationWrite = bCanEditOrganization;
        var lngLocalEpoch = CuiAuthorizationEpoch.Current;
        var bAcquired = false;
        try
        {
            ctCancellation.ThrowIfCancellationRequested();
            using var objCancellation = CancellationTokenSource.CreateLinkedTokenSource(ctCancellation, objLease.Token);
            await semDatabaseWork.WaitAsync(objCancellation.Token); bAcquired = true;
            var lngEpoch = await Task.Run(() =>
            {
                var lngBefore = objCrmToValidate.CuiEpoch();
                objCrmToValidate.ValidateCuiAccess(lngContact, bContactWrite, lngOrganization, bOrganizationWrite);
                var lngAfter = objCrmToValidate.CuiEpoch();
                if (lngBefore != lngAfter) throw new OperationCanceledException("Authorization changed during verification.");
                return lngAfter;
            }, objCancellation.Token);
            ctCancellation.ThrowIfCancellationRequested();
            if (ReferenceEquals(objLease, objCuiLease) && objLease.IsCurrent(objSession))
            {
                var bChanged = lngEpoch != objLease.DatabaseEpoch;
                var bWasStale = objLease.NeedsRefresh;
                if (!objLease.RenewVerified(lngEpoch, lngLocalEpoch)) return false;
                if (bChanged) InvalidateReadCaches();
                else if (bWasStale) NotifyReadCacheFreshness();
                return true;
            }
        }
        catch (OperationCanceledException) when (objLease.IsCurrent(objSession)) { return false; }
        catch (Exception objException) when (objException is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { objLease.MarkStale(); NotifyReadCacheFreshness(); return false; }
        catch (Exception objException) when (objException is OperationCanceledException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { objLease.Invalidate(); _ = LockAsync(); return false; }
        finally { if (bAcquired) semDatabaseWork.Release(); }
        return false;
    }
    private void InvalidateReadCaches()
    {
        // Drafts/baselines/editors are working state, not disposable lookup caches.
        // Do not release a lookup model that may also be the active draft's owner.
        CuiMemoryPolicy.Release(objTokenChoiceCatalog); objTokenChoiceCatalog = null;
        lstCachedContacts = []; lstCachedOrganizations = []; lstCachedOrganizationContacts = [];
        bReadCachesInvalid = true; vmShell.InvalidateCuiReadCaches();
    }
    private void NotifyReadCacheFreshness()
    {
        Notify(nameof(lstContacts)); Notify(nameof(lstOrganizations)); Notify(nameof(lstOrganizationContacts)); Notify(nameof(objDashboard));
    }
    public async Task ProbeCuiAuthorizationAsync()
    {
        if (Interlocked.Exchange(ref iAuthorizationProbe, 1) != 0) return;
        Task<bool>? tskValidation = null;
        var bDeferred = false;
        var objLease = objCuiLease;
        try
        {
            if (objLease is not null)
            {
                // Wake cheaply; repeated native opens/audit writes are unnecessary
                // while authorization is still fresh. Local epoch changes mark it stale.
                if (!objLease.NeedsRefresh) { await RefreshLookupCachesAsync(); return; }
                tskValidation = ValidateCuiScopeAsync(objLease.Token);
                var bVerified = await tskValidation.WaitAsync(CuiMemoryPolicy.AuthorizationProbeDeadline);
                if (bVerified) await RefreshLookupCachesAsync();
            }
        }
        catch (TimeoutException)
        {
            if (ReferenceEquals(objLease, objCuiLease)) { objLease?.MarkStale(); NotifyReadCacheFreshness(); }
            // Keep a single outstanding probe. A slow protected read must not
            // accumulate workers, terminate the session, or discard active drafts.
            if (tskValidation is not null) { bDeferred = true; _ = CompleteDeferredProbeAsync(tskValidation); }
        }
        finally { if (!bDeferred) Volatile.Write(ref iAuthorizationProbe, 0); }
    }
    private async Task CompleteDeferredProbeAsync(Task tskValidation)
    { try { await ObserveCleanupAsync(tskValidation); } finally { Volatile.Write(ref iAuthorizationProbe, 0); } }
    private async Task RefreshLookupCachesAsync()
    {
        if (!bReadCachesInvalid || bBusy || objCurrentContact is not null || objCurrentOrganization is not null || strPage is not ("Contacts" or "Organizations")) return;
        try { await RefreshListsAsync(); bReadCachesInvalid = false; }
        catch (OperationCanceledException) { }
        catch (Exception objException) when (objException is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { objCuiLease?.MarkStale(); }
        catch (Exception objException) when (objException is UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { objCuiLease?.Invalidate(); _ = LockAsync(); }
    }
    private static async Task ObserveCleanupAsync(Task tskCleanup)
    {
        try { await tskCleanup.ConfigureAwait(false); }
        catch (Exception) { /* Already locked: do not publish late cleanup results. */ }
    }
}
