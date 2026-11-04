using System.Security.Cryptography;

namespace HippoCrm.Core;

// Digests are integrity metadata, not keys. Immutable values avoid adapter-owned buffer aliasing.
public sealed record AuditHead(long lngLastId, string strSignature);
public sealed record AuditCheckpoint(long lngVersion, AuditHead? objCommitted, AuditHead? objPrepared);

public interface ITrustedAuditCheckpointStore
{
    // Must authenticate the installation/workspace binding and exclude other processes until Dispose.
    // Only local hardware/authenticated local IPC is permitted; no network calls or remote services.
    // Contention must return a bounded local 'busy' failure; it must not wait indefinitely.
    // Unknown workspaces MUST fail, unless explicitly enrolled offline by an authorized provisioner.
    ITrustedAuditCheckpointLease Acquire(string strWorkspaceIdentity);
}

public interface ITrustedAuditCheckpointLease : IDisposable
{
    // Dispose releases the exclusive lease idempotently, without deleting any durable state.
    AuditCheckpoint Read();
    // Durable atomic compare-and-swap, with rollback-resistant storage outside the DB backup domain.
    // Update must reject stale versions and return only once the new state survives power loss.
    void CompareExchange(long lngExpectedVersion, AuditCheckpoint objReplacement);
}

public sealed class AuditRecoveryRequiredException : CryptographicException
{
    public AuditRecoveryRequiredException() : base("Workspace recovery is required. Contact your local administrator.") { }
}

internal static partial class AuditCheckpointProvisioning
{
    // A client-supplied, authenticated build adds the partial implementation. No runtime override or file fallback.
    private static readonly ITrustedAuditCheckpointStore objStore = Create();
    static partial void Configure(ref ITrustedAuditCheckpointStore? objConfiguredStore);
    private static ITrustedAuditCheckpointStore Create()
    {
        ITrustedAuditCheckpointStore? objConfiguredStore = null;
        Configure(ref objConfiguredStore);
        return objConfiguredStore ?? new UnprovisionedStore();
    }
    internal static ITrustedAuditCheckpointLease Acquire(string strWorkspaceIdentity) => objStore.Acquire(strWorkspaceIdentity);
    private sealed class UnprovisionedStore : ITrustedAuditCheckpointStore
    {
        public ITrustedAuditCheckpointLease Acquire(string strWorkspaceIdentity) =>
            throw new CryptographicException("Trusted workspace protection is not provisioned. Contact your local administrator.");
    }
}

internal static class TrustedAuditProtocol
{
    private static void ValidateHead(AuditHead? objHead)
    {
        if (objHead is not null && (objHead.lngLastId < 0 || objHead.strSignature is null ||
            objHead.strSignature.Length != 64 || objHead.strSignature.Any(chrValue => !char.IsAsciiHexDigitUpper(chrValue))))
            throw new CryptographicException("The trusted workspace state is invalid.");
    }

    internal static AuditCheckpoint Verify(ITrustedAuditCheckpointLease objLease, AuditHead? objActual)
    {
        var objState = objLease.Read();
        if (objState is null || objState.lngVersion < 0) throw new CryptographicException("The trusted workspace state is invalid.");
        ValidateHead(objActual); ValidateHead(objState.objCommitted); ValidateHead(objState.objPrepared);
        if (objState.objPrepared is { } objProposed && objState.objCommitted is { } objPrevious
            && objProposed.lngLastId <= objPrevious.lngLastId)
            throw new CryptographicException("The trusted workspace state is invalid.");
        if (objState.objPrepared is not null)
        {
            // A matching proposed head proves SQLite committed. An old head is ambiguous:
            // crash before commit and rollback after commit are indistinguishable. Never erase that evidence.
            if (objActual != objState.objPrepared) throw new AuditRecoveryRequiredException();
            var objRecovered = new AuditCheckpoint(checked(objState.lngVersion + 1), objActual, null);
            objLease.CompareExchange(objState.lngVersion, objRecovered);
            return objRecovered;
        }
        if (objState.objCommitted != objActual) throw new AuditRecoveryRequiredException();
        return objState;
    }

    internal static void Commit(ITrustedAuditCheckpointLease objLease, AuditCheckpoint objBefore, AuditHead objAfter, Action actCommit)
    {
        ValidateHead(objAfter);
        // Validate even a read-only commit against the latest durable checkpoint.
        var objCurrent = Verify(objLease, objBefore.objCommitted);
        if (objCurrent != objBefore) throw new AuditRecoveryRequiredException();
        if (objAfter == objBefore.objCommitted) { actCommit(); return; }
        if (objBefore.objCommitted is { } objPrevious && objAfter.lngLastId <= objPrevious.lngLastId)
            throw new CryptographicException("An advancing audit entry is required.");
        var objPrepared = new AuditCheckpoint(checked(objBefore.lngVersion + 1), objBefore.objCommitted, objAfter);
        objLease.CompareExchange(objBefore.lngVersion, objPrepared);
        // Any exception leaves durable pending evidence. Never infer 'not committed' from an exception.
        actCommit();
        objLease.CompareExchange(objPrepared.lngVersion, new AuditCheckpoint(checked(objPrepared.lngVersion + 1), objAfter, null));
    }
}
