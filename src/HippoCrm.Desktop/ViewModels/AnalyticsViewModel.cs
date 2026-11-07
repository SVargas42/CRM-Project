using System.Globalization;
using HippoCrm.Core;

namespace HippoCrm.Desktop.ViewModels;

// Owns only the bounded snapshot. No database or authorization work runs in bindings.
public sealed partial class AnalyticsViewModel : ViewModelBase, IDisposable
{
    private readonly Func<CancellationToken, Task<AnalyticsSnapshot?>> fnLoad;
    private readonly Func<bool> fnCanRead;
    private readonly Func<CancellationToken> fnToken;
    private readonly Func<RecordKind, long, Task>? fnOpenProfile;
    private readonly SynchronizationContext? objContext = SynchronizationContext.Current;
    private CancellationTokenSource? objCancellation;
    private AnalyticsSnapshot? objLoaded;
    private int iRevision;
    private long lngLoadedEpoch;
    private bool bCurrentLoading;
    private string strCurrentError = "";
    public AnalyticsSnapshot? objSnapshot => fnCanRead() && lngLoadedEpoch == CuiAuthorizationEpoch.Current ? objLoaded : null;
    public bool bLoading { get => bCurrentLoading; private set { Set(ref bCurrentLoading, value); cmdRefresh.Refresh(); } }
    public string strError { get => strCurrentError; private set { Set(ref strCurrentError, value); Notify(nameof(bFailed)); } }
    public bool bFailed => strError.Length != 0;
    public string strLoadedAt => objSnapshot is { } objData
        ? "Loaded at " + objData.AsOfUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : "Analytics not loaded";
    public AsyncRelayCommand cmdRefresh { get; }
    public Task tskLoad { get; private set; } = Task.CompletedTask;
    public AnalyticsViewModel(Func<CancellationToken, Task<AnalyticsSnapshot?>> fnLoad, Func<bool>? fnCanRead = null, Func<CancellationToken>? fnToken = null,
        Func<RecordKind, long, Task>? fnOpenProfile = null)
    {
        this.fnLoad = fnLoad; this.fnCanRead = fnCanRead ?? (() => false); this.fnToken = fnToken ?? (() => CancellationToken.None);
        this.fnOpenProfile = fnOpenProfile;
        cmdRefresh = new AsyncRelayCommand(RefreshAsync, () => !bLoading);
        InitializeTables();
        CuiAuthorizationEpoch.Subscribe(OnEpochChanged);
    }
    public void EnsureLoaded() { if (!bLoading && objSnapshot is null) tskLoad = LoadAsync(); }
    public Task RefreshAsync() => tskLoad = LoadAsync();
    private async Task LoadAsync()
    {
        Reset();
        var iRequest = iRevision;
        var lngEpoch = CuiAuthorizationEpoch.Current;
        using var objRequest = CancellationTokenSource.CreateLinkedTokenSource(fnToken());
        objCancellation = objRequest; bLoading = true;
        AnalyticsSnapshot? objStaged = null;
        try
        {
            // The workspace loader performs the protected background read and final live checks.
            objStaged = await fnLoad(objRequest.Token);
            objRequest.Token.ThrowIfCancellationRequested();
            if (iRequest != iRevision || lngEpoch != CuiAuthorizationEpoch.Current) return;
            if (objStaged is null || !fnCanRead()) throw new InvalidOperationException();
            objLoaded = objStaged; objStaged = null; lngLoadedEpoch = lngEpoch;
            RebuildTables();
            Notify(nameof(objSnapshot)); Notify(nameof(strLoadedAt));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (iRequest == iRevision) strError = "Analytics could not be loaded. Please reload."; }
        finally
        {
            CuiMemoryPolicy.Release(objStaged);
            if (iRequest == iRevision) { objCancellation = null; bLoading = false; }
        }
    }
    private void OnEpochChanged()
    {
        // Hide immediately through the epoch-gated getter; release on the presentation context.
        try { objCancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* A completed request may race a background epoch notification. */ }
        if (objContext is null || SynchronizationContext.Current == objContext) Reset();
        else objContext.Post(_ => Reset(), null);
    }
    public void Reset()
    {
        iRevision++; objCancellation?.Cancel(); objCancellation = null;
        CuiMemoryPolicy.Release(objLoaded); objLoaded = null;
        ClearTables();
        bLoading = false; strError = ""; Notify(nameof(objSnapshot)); Notify(nameof(strLoadedAt));
    }
    public void Dispose() { CuiAuthorizationEpoch.Unsubscribe(OnEpochChanged); Reset(); }
}
