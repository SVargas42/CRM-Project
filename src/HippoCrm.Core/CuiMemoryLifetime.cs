namespace HippoCrm.Core;

// Authorization freshness is independent of Session's 15-minute inactivity limit.
// Reaching this interval requests verification; it never terminates active editing.
public static partial class CuiMemoryPolicy
{
    public static TimeSpan CacheFreshnessLifetime => TimeSpan.FromSeconds(60);
    public static TimeSpan CleanupDeadline => TimeSpan.FromSeconds(2);
    // Includes waiting behind legitimate SQLCipher key derivation and durable writes.
    public static TimeSpan AuthorizationProbeDeadline => TimeSpan.FromSeconds(5);
    public const int iPageSize = 128;
    public const int iMaximumFieldLength = 512;

}

// Marks local authorization caches stale after a committed permission change.
// Other processes are detected by the authenticated database epoch at read/probe boundaries.
public static class CuiAuthorizationEpoch
{
    private static long lngCurrent;
    private static event Action? Changed;
    public static long Current => Interlocked.Read(ref lngCurrent);
    internal static void Advance() { Interlocked.Increment(ref lngCurrent); Changed?.Invoke(); }
    internal static void Subscribe(Action actInvalidate) => Changed += actInvalidate;
    internal static void Unsubscribe(Action actInvalidate) => Changed -= actInvalidate;
}

public sealed class CuiMemoryLease : IDisposable
{
    private readonly Session objSession;
    private readonly TimeProvider objClock;
    private readonly object objGate = new();
    private long lngVerified;
    private long lngLocalEpoch = CuiAuthorizationEpoch.Current;
    private long lngDatabaseEpoch;
    private bool bStale;
    private readonly CancellationTokenSource objCancellation = new();
    private readonly ITimer objTimer;
    public long DatabaseEpoch { get { lock (objGate) return lngDatabaseEpoch; } }
    public CancellationToken Token => objCancellation.Token;
    internal CuiMemoryLease(Session objSession, TimeProvider objClock, long lngEpoch)
    {
        this.objSession = objSession; this.objClock = objClock; lngDatabaseEpoch = lngEpoch;
        lngVerified = objClock.GetTimestamp();
        objTimer = objClock.CreateTimer(objState => OnFreshnessTimer(), null, CuiMemoryPolicy.CacheFreshnessLifetime, Timeout.InfiniteTimeSpan);
        CuiAuthorizationEpoch.Subscribe(MarkStale);
    }
    public bool IsCurrent(Session? objCandidate)
    {
        try
        {
            long lngLastVerified; lock (objGate) lngLastVerified = lngVerified;
            if (!ReferenceEquals(objSession, objCandidate) || objSession.IsExpired || objClock.GetTimestamp() < lngLastVerified)
                Invalidate();
        }
        catch (Exception) { Invalidate(); }
        return !objCancellation.IsCancellationRequested;
    }
    public bool NeedsRefresh
    {
        get
        {
            if (!IsCurrent(objSession)) return true;
            try
            {
                lock (objGate)
                {
                    if (lngLocalEpoch != CuiAuthorizationEpoch.Current || objClock.GetElapsedTime(lngVerified, objClock.GetTimestamp()) >= CuiMemoryPolicy.CacheFreshnessLifetime)
                        bStale = true;
                    return bStale;
                }
            }
            catch (Exception) { Invalidate(); return true; }
        }
    }
    private void OnFreshnessTimer()
    {
        try
        {
            lock (objGate)
            {
                var tsElapsed = objClock.GetElapsedTime(lngVerified, objClock.GetTimestamp());
                if (tsElapsed >= CuiMemoryPolicy.CacheFreshnessLifetime) bStale = true;
                else objTimer.Change(CuiMemoryPolicy.CacheFreshnessLifetime - tsElapsed, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception) { Invalidate(); }
    }
    public void MarkStale() { lock (objGate) bStale = true; }
    // Only call after a successful live authorization check, using the local epoch
    // captured before that check. Concurrent changes require another verification.
    public bool RenewVerified(long lngEpoch, long lngObservedLocalEpoch)
    {
        if (!IsCurrent(objSession)) return false;
        lock (objGate)
        {
            if (lngObservedLocalEpoch != CuiAuthorizationEpoch.Current) { bStale = true; return false; }
            lngVerified = objClock.GetTimestamp(); lngDatabaseEpoch = lngEpoch;
            lngLocalEpoch = lngObservedLocalEpoch; bStale = false;
            objTimer.Change(CuiMemoryPolicy.CacheFreshnessLifetime, Timeout.InfiniteTimeSpan);
            return true;
        }
    }
    public void Invalidate()
    {
        try { objCancellation.Cancel(); }
        catch (AggregateException) { /* A failing observer cannot undo committed permission invalidation. */ }
    }
    // Keep the canceled token source readable by in-flight workers; it owns no OS wait handle.
    public void Dispose() { CuiAuthorizationEpoch.Unsubscribe(MarkStale); Invalidate(); objTimer.Dispose(); }
}
