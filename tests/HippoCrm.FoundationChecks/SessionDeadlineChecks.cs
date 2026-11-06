using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class SessionDeadlineChecks
{
    internal static void Run()
    {
        var objClock = new ManualSessionClock();
        using var objSession = new Session(1, "fixture", objClock);
        var iExpiredEvents = 0;
        objSession.Expired += (objSender, objArguments) => iExpiredEvents++;
        objClock.Advance(Session.InactivityLimit - TimeSpan.FromTicks(1));
        Check(!objSession.IsExpired, "One tick before the deadline is valid");
        objClock.lngTicks++;
        Check(iExpiredEvents == 0, "Boundary input can arrive before the queued deadline timer callback");
        Check(objSession.IsExpired && !objSession.TryRecordActivity(), "Exact 15-minute boundary expires before activity can refresh it");
        objClock.Advance(TimeSpan.Zero);
        Check(iExpiredEvents == 1, "Deadline timer signals expiry without waiting for UI polling");
        var bDispatched = false;
        Reject<UnauthorizedAccessException>(() => objSession.UseTokenHash(arrHash => bDispatched = true), "Expired credentials reject authenticated dispatch");
        Check(!bDispatched, "Expired session never calls the authenticated callback");
        Check(objSession.UseTokenHashForRevocation(arrHash => arrHash.Length == 32), "Expired credential can only be used to revoke itself");
        objClock.lngTicks = 0;
        Check(!objSession.TryRecordActivity(), "Expiry remains latched when the clock is moved back");

        var objFreshClock = new ManualSessionClock();
        using var objFreshSession = new Session(2, "fixture", objFreshClock);
        objFreshClock.dtWall = objFreshClock.dtWall.AddYears(30);
        Check(!objFreshSession.IsExpired, "Forward wall-clock adjustment does not change monotonic deadline");
        objFreshClock.dtWall = objFreshClock.dtWall.AddYears(-60);
        objFreshClock.Advance(TimeSpan.FromMinutes(14));
        Check(objFreshSession.TryRecordActivity(), "Pre-expiry input refreshes from its accepted timestamp");
        objFreshClock.Advance(TimeSpan.FromMinutes(14));
        Check(!objFreshSession.IsExpired, "Accepted activity extends the monotonic deadline");
        objFreshClock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromTicks(1));
        Check(!objFreshSession.TryRecordActivity(), "Input immediately after expiry locks despite a backward wall-clock change");

        var objBrokenClock = new ManualSessionClock { lngTicks = 10 };
        using var objBrokenSession = new Session(3, "fixture", objBrokenClock);
        objBrokenClock.lngTicks = 9;
        Check(objBrokenSession.IsExpired, "A regressing monotonic source fails closed");
        using var objInvalidated = new Session(4, "fixture", new ManualSessionClock());
        objInvalidated.Invalidate();
        Check(!objInvalidated.TryRecordActivity(), "Explicit locking cannot be reversed by queued input");
        objInvalidated.Dispose();
        Check(objInvalidated.IsExpired && !objInvalidated.TryRecordActivity(), "Disposed sessions cannot accept input");
        Console.WriteLine("15 monotonic session deadline checks passed.");
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); Console.WriteLine("PASS " + strMessage); }
    private static void Reject<T>(Action actOperation, string strMessage) where T : Exception
    { try { actOperation(); } catch (T) { Check(true, strMessage); return; } throw new InvalidOperationException("FAIL " + strMessage); }
}
