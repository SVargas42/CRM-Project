namespace HippoCrm.TestSupport;

internal sealed class ManualSessionClock : TimeProvider
{
    private readonly List<ManualTimer> lstTimers = [];
    internal long lngTicks;
    internal DateTimeOffset dtWall = DateTimeOffset.UnixEpoch;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => lngTicks;
    public override DateTimeOffset GetUtcNow() => dtWall;
    public override ITimer CreateTimer(TimerCallback fnCallback, object? objState, TimeSpan tsDueTime, TimeSpan tsPeriod)
    {
        var objTimer = new ManualTimer(this, fnCallback, objState);
        lstTimers.Add(objTimer); objTimer.Change(tsDueTime, tsPeriod); return objTimer;
    }
    internal void Advance(TimeSpan tsElapsed)
    {
        lngTicks = checked(lngTicks + tsElapsed.Ticks);
        foreach (var objTimer in lstTimers.ToArray()) objTimer.FireIfDue();
    }
    private sealed class ManualTimer(ManualSessionClock objClock, TimerCallback fnCallback, object? objState) : ITimer
    {
        private long lngDue = long.MaxValue;
        private bool bDisposed;
        public bool Change(TimeSpan tsDueTime, TimeSpan tsPeriod)
        {
            if (bDisposed) return false;
            if (tsPeriod != Timeout.InfiniteTimeSpan) throw new ArgumentException("Only one-shot fixture timers are supported.");
            lngDue = tsDueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(objClock.lngTicks + tsDueTime.Ticks);
            return true;
        }
        internal void FireIfDue()
        {
            if (bDisposed || objClock.lngTicks < lngDue) return;
            lngDue = long.MaxValue; fnCallback(objState);
        }
        public void Dispose() { bDisposed = true; objClock.lstTimers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
