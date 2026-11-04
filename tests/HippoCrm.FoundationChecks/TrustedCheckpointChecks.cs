using System.Security.Cryptography;
using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class TrustedCheckpointChecks
{
    private static int iPassed;
    internal static void Run()
    {
        var objOld = new AuditHead(10, new string('A', 64));
        var objNew = new AuditHead(11, new string('B', 64));
        var objLease = new MemoryAuditCheckpoint { objState = new(2, objOld, null) };
        Check(TrustedAuditProtocol.Verify(objLease, objOld).objCommitted == objOld, "Current authenticated audit head is accepted");
        Reject<AuditRecoveryRequiredException>(() => TrustedAuditProtocol.Verify(objLease, objNew), "Uncheckpointed history is rejected");
        var bCommitted = false;
        TrustedAuditProtocol.Commit(objLease, objLease.Read(), objNew, () =>
        {
            Check(objLease.Read().objPrepared == objNew, "Durable prepare precedes the database commit");
            bCommitted = true;
        });
        Check(bCommitted && objLease.Read() == new AuditCheckpoint(4, objNew, null), "Successful commit confirms a new monotonic checkpoint version");
        Reject<AuditRecoveryRequiredException>(() => TrustedAuditProtocol.Verify(objLease, objOld), "A valid signed suffix truncation is rejected");
        Reject<AuditRecoveryRequiredException>(() => TrustedAuditProtocol.Verify(objLease, null), "Whole database replacement with empty storage is rejected");
        Reject<InvalidOperationException>(() => objLease.CompareExchange(2, new(3, objOld, null)), "Stale checkpoint writers cannot overwrite progress");

        var objPrepareFailure = new MemoryAuditCheckpoint { objState = new(2, objOld, null), iFailWrite = 1 };
        bCommitted = false;
        Reject<IOException>(() => TrustedAuditProtocol.Commit(objPrepareFailure, objPrepareFailure.Read(), objNew, () => bCommitted = true), "Prepare failure rejects the operation");
        Check(!bCommitted && objPrepareFailure.Read().objCommitted == objOld, "Prepare failure never invokes the database commit");

        var objDbFailure = new MemoryAuditCheckpoint { objState = new(2, objOld, null) };
        Reject<IOException>(() => TrustedAuditProtocol.Commit(objDbFailure, objDbFailure.Read(), objNew, () => throw new IOException("Fixture crash")), "Database commit interruption preserves prepared evidence");
        Reject<AuditRecoveryRequiredException>(() => TrustedAuditProtocol.Verify(objDbFailure, objOld), "Old database beside a prepare requires explicit recovery, never silent abort inference");
        Check(objDbFailure.Read().objPrepared == objNew, "Ambiguous recovery does not discard the proposed head");

        var objConfirmFailure = new MemoryAuditCheckpoint { objState = new(2, objOld, null), iFailWrite = 2 };
        Reject<IOException>(() => TrustedAuditProtocol.Commit(objConfirmFailure, objConfirmFailure.Read(), objNew, () => { }), "Post-commit checkpoint interruption is reported");
        Check(TrustedAuditProtocol.Verify(objConfirmFailure, objNew) == new AuditCheckpoint(4, objNew, null), "Matching committed database automatically completes interrupted confirmation");
        Check(TrustedAuditProtocol.Verify(objConfirmFailure, objNew).objPrepared is null, "Crash recovery is idempotent");
        Reject<AuditRecoveryRequiredException>(() => TrustedAuditProtocol.Verify(objConfirmFailure, objOld), "Rollback after recovered confirmation remains rejected");

        var objUnavailable = new MemoryAuditCheckpoint { bDenyRead = true };
        Reject<IOException>(() => TrustedAuditProtocol.Verify(objUnavailable, null), "Unavailable protected state fails closed");
        var objMalformed = new MemoryAuditCheckpoint { objState = new(0, new AuditHead(1, "invalid"), null) };
        Reject<CryptographicException>(() => TrustedAuditProtocol.Verify(objMalformed, objOld), "Malformed checkpoint digests fail closed");
        Reject<CryptographicException>(() => AuditCheckpointProvisioning.Acquire("fixture-workspace"), "Production has no default checkpoint adapter or file fallback");
        Console.WriteLine($"{iPassed} trusted checkpoint protocol checks passed.");
    }
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
    private static void Reject<T>(Action actOperation, string strMessage) where T : Exception
    { try { actOperation(); } catch (T) { Check(true, strMessage); return; } throw new InvalidOperationException("FAIL " + strMessage); }
}
