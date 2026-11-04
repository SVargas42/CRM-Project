using System.Security.Cryptography;
using System.Runtime.InteropServices;
namespace HippoCrm.Core;

public enum UserRole { Administrator, Editor, Viewer }
public enum RecordKind { Contact, Organization }

public sealed class Session : IDisposable
{
    private readonly object objGate = new();
    private readonly byte[] arrToken = SensitiveMemory.AllocateBytes(32);
    private bool bDisposed;
    private bool bExpired;
    private readonly TimeProvider objClock;
    private readonly ITimer objIdleTimer;
    private bool bExpiryNotified;
    private long lngLastActivity;
    public event EventHandler? Expired;
    public static TimeSpan InactivityLimit => TimeSpan.FromMinutes(15);
    public long UserId { get; }
    public string Username { get; }

    internal Session(long lngUserId, string strUsername, TimeProvider? objClock = null)
    {
        this.objClock = objClock ?? SessionTimeProvider.objInstance;
        lngLastActivity = this.objClock.GetTimestamp();
        UserId = lngUserId; Username = strUsername;
        var bInitialized = false;
        try
        {
            RandomNumberGenerator.Fill(arrToken);
            objIdleTimer = this.objClock.CreateTimer(OnIdleTimer, null, InactivityLimit, Timeout.InfiniteTimeSpan);
            bInitialized = true;
        }
        finally { if (!bInitialized) SensitiveMemory.Clear(arrToken); }
    }

    internal T UseTokenHash<T>(Func<byte[], T> fnUseHash)
        => UseTokenHashCore(fnUseHash, false);

    // This capability is only for revoking the existing credential, never business access.
    internal T UseTokenHashForRevocation<T>(Func<byte[], T> fnUseHash)
        => UseTokenHashCore(fnUseHash, true);

    private T UseTokenHashCore<T>(Func<byte[], T> fnUseHash, bool bRevocation)
    {
        ArgumentNullException.ThrowIfNull(fnUseHash);
        lock (objGate)
        {
            ObjectDisposedException.ThrowIf(bDisposed, this);
            if (!bRevocation && ExpiredCore(ReadTimestamp()))
                throw new UnauthorizedAccessException("The session expired. Sign in again.");
            var arrHash = SensitiveMemory.AllocateBytes(32);
            try { SHA256.HashData(arrToken.AsSpan(), arrHash.AsSpan()); return fnUseHash(arrHash); }
            finally { SensitiveMemory.Clear(arrHash); }
        }
    }

    public bool IsExpired
    {
        get { lock (objGate) return bDisposed || ExpiredCore(ReadTimestamp()); }
    }

    public bool TryRecordActivity()
    {
        lock (objGate)
        {
            var lngNow = ReadTimestamp();
            if (bDisposed || ExpiredCore(lngNow)) return false;
            lngLastActivity = lngNow;
            objIdleTimer.Change(InactivityLimit, Timeout.InfiniteTimeSpan);
            return true;
        }
    }

    public void Invalidate() { lock (objGate) bExpired = true; }

    private void OnIdleTimer(object? objState)
    {
        lock (objGate)
        {
            if (bDisposed || bExpiryNotified) return;
            var lngNow = ReadTimestamp();
            if (!ExpiredCore(lngNow))
            {
                objIdleTimer.Change(InactivityLimit - objClock.GetElapsedTime(lngLastActivity, lngNow), Timeout.InfiniteTimeSpan);
                return;
            }
            bExpiryNotified = true;
        }
        // Never call presentation code under the credential lock or on the UI dispatcher implicitly.
        Expired?.Invoke(this, EventArgs.Empty);
    }

    private bool ExpiredCore(long lngNow)
    {
        // Equality expires. A broken/regressing monotonic clock must never extend a session.
        try
        {
            if (objClock.TimestampFrequency <= 0 || lngNow < lngLastActivity || objClock.GetElapsedTime(lngLastActivity, lngNow) >= InactivityLimit)
                bExpired = true;
        }
        catch (Exception) { bExpired = true; } // A failed clock is a lock decision, never accepted input.
        return bExpired;
    }

    private long ReadTimestamp()
    {
        try { return objClock.GetTimestamp(); }
        catch (Exception) { bExpired = true; return lngLastActivity; }
    }

    public void Dispose()
    {
        lock (objGate)
        {
            if (bDisposed) return;
            try { bDisposed = true; }
            finally { try { objIdleTimer.Dispose(); } finally { SensitiveMemory.Clear(arrToken); } }
        }
    }
}

// These OS monotonic counters include suspended time. Wall-clock adjustments cannot extend a session.
internal sealed class SessionTimeProvider : TimeProvider
{
    internal static readonly TimeProvider objInstance = new SessionTimeProvider();
    public override long TimestampFrequency => OperatingSystem.IsWindows() ? 1000 : 1_000_000_000;
    public override long GetTimestamp()
    {
        if (OperatingSystem.IsWindows()) return checked((long)GetTickCount64());
        if (OperatingSystem.IsMacOS())
        {
            if (MachTimebaseInfo(out var objInfo) != 0 || objInfo.uiDenominator == 0)
                throw new InvalidOperationException("The session clock is unavailable.");
            return checked((long)((UInt128)MachContinuousTime() * objInfo.uiNumerator / objInfo.uiDenominator));
        }
        throw new PlatformNotSupportedException("A continuous session clock is required.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TimebaseInfo { internal uint uiNumerator; internal uint uiDenominator; }
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern ulong GetTickCount64();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "mach_continuous_time")] private static extern ulong MachContinuousTime();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "mach_timebase_info")] private static extern int MachTimebaseInfo(out TimebaseInfo objInfo);
}
