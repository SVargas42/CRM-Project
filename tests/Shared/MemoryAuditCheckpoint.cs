using HippoCrm.Core;

namespace HippoCrm.TestSupport;

// TEST ONLY: process memory is deliberately NOT a deployable rollback-resistant trust anchor.
internal sealed class MemoryAuditCheckpoint : ITrustedAuditCheckpointLease
{
    internal AuditCheckpoint objState = new(0, null, null);
    internal int iWrites;
    internal int iFailWrite = 0;
    internal bool bDenyRead = false;
    public AuditCheckpoint Read() => bDenyRead ? throw new IOException("Fixture checkpoint unavailable.") : objState;
    public void CompareExchange(long lngExpectedVersion, AuditCheckpoint objReplacement)
    {
        if (++iWrites == iFailWrite) throw new IOException("Fixture checkpoint write interrupted.");
        if (objState.lngVersion != lngExpectedVersion || objReplacement.lngVersion != checked(lngExpectedVersion + 1))
            throw new InvalidOperationException("Stale checkpoint version.");
        objState = objReplacement;
    }
    public void Dispose() { }
}
