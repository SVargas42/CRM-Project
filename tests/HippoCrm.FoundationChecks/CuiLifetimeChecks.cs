using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class CuiLifetimeChecks
{
    private static int iPassed;
    internal static void Run()
    {
        var objClock = new ManualSessionClock();
        using var objSession = new Session(1, "FICTIONAL lifetime", objClock);
        using var objLease = objSession.CreateCuiMemoryLease(7);
        objClock.lngTicks = CuiMemoryPolicy.CacheFreshnessLifetime.Ticks - 1;
        Check(objLease.IsCurrent(objSession) && !objLease.NeedsRefresh, "Authorization cache is fresh one tick before its TTL");
        objClock.dtWall = DateTimeOffset.MaxValue;
        Check(!objLease.NeedsRefresh, "Wall-clock jumps do not change monotonic authorization freshness");
        Check(objSession.TryRecordActivity(), "Input renews the independent idle timer");
        objClock.lngTicks++;
        Check(objLease.IsCurrent(objSession) && objLease.NeedsRefresh && !objLease.Token.IsCancellationRequested,
            "Exact cache TTL requests verification without canceling active editing");
        Check(objLease.RenewVerified(7, CuiAuthorizationEpoch.Current) && !objLease.NeedsRefresh, "Live verification renews freshness on the same session lease");
        var bContinuous = true;
        for (var iMinute = 0; iMinute < 30; iMinute++)
        {
            objClock.Advance(TimeSpan.FromSeconds(61));
            bContinuous &= objSession.TryRecordActivity() && objLease.IsCurrent(objSession) && objLease.NeedsRefresh;
            bContinuous &= objLease.RenewVerified(7, CuiAuthorizationEpoch.Current);
        }
        Check(bContinuous && !objSession.IsExpired, "Continuous active use survives repeated cache TTLs for more than 30 minutes");
        using var objOtherSession = new Session(1, "FICTIONAL lifetime", objClock);
        using var objWrongOwner = objSession.CreateCuiMemoryLease(7);
        Check(!objWrongOwner.IsCurrent(objOtherSession), "Same identity in a different issued session cannot reuse a CUI lease");
        using var objPermission = objSession.CreateCuiMemoryLease(7);
        var lngBeforeChange = CuiAuthorizationEpoch.Current;
        CuiAuthorizationEpoch.Advance();
        Check(objPermission.NeedsRefresh && objPermission.IsCurrent(objSession) && !objPermission.Token.IsCancellationRequested,
            "Permission epoch change requests live revalidation without terminating the session");
        Check(!objPermission.RenewVerified(8, lngBeforeChange), "A concurrent permission change rejects an obsolete refresh result");
        Check(objPermission.RenewVerified(8, CuiAuthorizationEpoch.Current), "Verified current epoch can replace a stale epoch");
        using var objTimerLease = objOtherSession.CreateCuiMemoryLease(8);
        objClock.Advance(CuiMemoryPolicy.CacheFreshnessLifetime);
        Check(objTimerLease.NeedsRefresh && !objTimerLease.Token.IsCancellationRequested, "Freshness timer marks caches stale without canceling draft ownership");
        using var objRevoked = objSession.CreateCuiMemoryLease(8); objSession.Invalidate();
        Check(!objRevoked.IsCurrent(objSession), "Invalidated session still denies cache and draft access");
        var objIdleClock = new ManualSessionClock();
        using var objIdleSession = new Session(2, "FICTIONAL idle", objIdleClock);
        using var objIdleLease = objIdleSession.CreateCuiMemoryLease(8);
        for (var iMinute = 0; iMinute < 14; iMinute++)
        { objIdleClock.Advance(TimeSpan.FromMinutes(1)); objIdleLease.RenewVerified(8, CuiAuthorizationEpoch.Current); }
        objIdleClock.Advance(TimeSpan.FromMinutes(1));
        Check(objIdleSession.IsExpired && !objIdleLease.IsCurrent(objIdleSession), "Background freshness checks never extend the exact 15-minute inactivity deadline");
        var objRegressingClock = new ManualSessionClock { lngTicks = 100 };
        using var objRegressingSession = new Session(3, "FICTIONAL regression", objRegressingClock);
        using var objRegressingLease = objRegressingSession.CreateCuiMemoryLease(8); objRegressingClock.lngTicks = 99;
        Check(!objRegressingLease.IsCurrent(objRegressingSession), "A regressing monotonic source still fails closed");
        CredentialTransferChecks();
        Console.WriteLine($"{iPassed} CUI lifetime and credential-transfer checks passed.");
    }
    private static void CredentialTransferChecks()
    {
        byte[]? arrObserved = null; var bReleased = false;
        try
        {
            WindowsCredentialKeyStore.TransferKey(() => new IntPtr(1), arrKey =>
            { arrObserved = arrKey; Array.Fill(arrKey, (byte)0x7d); throw new IOException("FICTIONAL copy failure"); }, () => bReleased = true);
            throw new InvalidOperationException("Failed copy was accepted.");
        }
        catch (IOException) { }
        Check(bReleased && arrObserved is not null && arrObserved.All(bytValue => bytValue == 0), "Partial managed copy is wiped and native cleanup runs on copy failure");
        try
        {
            WindowsCredentialKeyStore.TransferKey(() => new IntPtr(1), arrKey =>
            { arrObserved = arrKey; Array.Fill(arrKey, (byte)0x7d); }, () => throw new IOException("FICTIONAL cleanup failure"));
            throw new InvalidOperationException("Failed cleanup transferred a secret.");
        }
        catch (IOException) { }
        Check(arrObserved is not null && arrObserved.All(bytValue => bytValue == 0), "Managed secret is wiped when native cleanup prevents transfer");
        bReleased = false;
        var arrTransferred = WindowsCredentialKeyStore.TransferKey(() => new IntPtr(1), arrKey => Array.Fill(arrKey, (byte)0x7d), () => bReleased = true);
        try { Check(bReleased && arrTransferred.All(bytValue => bytValue == 0x7d), "Successful transfer follows native cleanup and preserves caller ownership"); }
        finally { SensitiveMemory.Clear(arrTransferred); }
        bReleased = false;
        try { WindowsCredentialKeyStore.TransferKey(() => throw new IOException("FICTIONAL validation failure"), arrKey => throw new InvalidOperationException(), () => bReleased = true); }
        catch (IOException) { }
        Check(bReleased, "Validation failure also releases native ownership");
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
}
