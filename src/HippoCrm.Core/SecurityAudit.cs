using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
namespace HippoCrm.Core;

internal enum AuditEntity { Authentication, Users, Contacts, Organizations, Interactions, Employment, Contracts, Sessions, Schema, Images }
internal enum AuditAction
{
    AdministratorCreated, SetupRejected, UserCreated, SignInSucceeded, SignInFailed, SignInLocked,
    SignedOut, RoleChanged, AccessGranted, AccessRevoked, Created, Updated, Ended, AccessVerified, PermissionDenied, SchemaUpgraded,
    PasswordChanged, PasswordReset, PasswordChangeRejected, AccountEnabled, AccountDisabled,
    CuiAuthorized, CuiRevoked, LifecycleChanged, Archived, Restored
}

internal static class SecurityAudit
{
    private sealed record AuditKeySource(IDatabaseKeyStore objStore, string strIdentity, ITrustedAuditCheckpointLease objLease);
    private static readonly ConditionalWeakTable<SqliteTransaction, AuditCheckpoint> objTransactions = new();
    private static readonly ConditionalWeakTable<SqliteConnection, AuditKeySource> objSources = new();
    internal static void RegisterConnection(SqliteConnection dbConnection, IDatabaseKeyStore objStore, string strIdentity, ITrustedAuditCheckpointLease objLease) =>
        objSources.Add(dbConnection, new AuditKeySource(objStore, strIdentity, objLease));

    private static AuditHead? ReadHead(SqliteConnection dbConnection, SqliteTransaction txOperation)
    {
        using var cmdExists = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='audit_chain_state'", dbConnection, txOperation);
        if (Convert.ToInt64(cmdExists.ExecuteScalar()) == 0) return null;
        return WithKey(dbConnection, arrKey =>
        {
            var arrSignature = VerifyPrefix(dbConnection, txOperation, arrKey);
            try
            {
                using var cmdLast = new SqliteCommand("SELECT MAX(legacy_last_id,COALESCE((SELECT MAX(id) FROM audit_log),0)) FROM audit_chain_state WHERE id=1", dbConnection, txOperation);
                var lngLastId = Convert.ToInt64(cmdLast.ExecuteScalar());
                return new AuditHead(lngLastId, Convert.ToHexString(arrSignature));
            }
            finally { SensitiveMemory.Clear(arrSignature); }
        });
    }

    private static AuditCheckpoint VerifyFreshness(SqliteConnection dbConnection, SqliteTransaction txOperation)
    {
        if (objTransactions.TryGetValue(txOperation, out var objVerified)) return objVerified;
        if (!objSources.TryGetValue(dbConnection, out var objSource)) throw new CryptographicException("Trusted workspace protection is unavailable.");
        var objState = TrustedAuditProtocol.Verify(objSource.objLease, ReadHead(dbConnection, txOperation));
        objTransactions.Add(txOperation, objState);
        return objState;
    }

    internal static void Commit(SqliteConnection dbConnection, SqliteTransaction txOperation)
    {
        var objBefore = VerifyFreshness(dbConnection, txOperation);
        var objAfter = ReadHead(dbConnection, txOperation) ?? throw new CryptographicException("The audit anchor is missing.");
        var objSource = objSources.GetValue(dbConnection, dbMissing => throw new CryptographicException("Trusted workspace protection is unavailable."));
        TrustedAuditProtocol.Commit(objSource.objLease, objBefore, objAfter, txOperation.Commit);
    }

    private static T WithKey<T>(SqliteConnection dbConnection, Func<byte[], T> fnUse)
    {
        if (!objSources.TryGetValue(dbConnection, out var objSource))
            throw new CryptographicException("The protected audit key is unavailable.");
        byte[]? arrMaster = null;
        var arrAuditKey = SensitiveMemory.AllocateBytes(32);
        try
        {
            arrMaster = objSource.objStore.ReadKey(objSource.strIdentity);
            if (arrMaster.Length != 32) throw new CryptographicException("The protected audit key is invalid.");
            HMACSHA256.HashData(arrMaster, "HippoCrm/AuditChain/v1"u8, arrAuditKey);
            return fnUse(arrAuditKey);
        }
        finally { SensitiveMemory.Clear(arrMaster); SensitiveMemory.Clear(arrAuditKey); }
    }

    internal static void Initialize(SqliteConnection dbConnection, SqliteTransaction txOperation, bool bRequireExistingAnchor)
    {
        VerifyFreshness(dbConnection, txOperation);
        using var cmdTable = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='audit_chain_state'", dbConnection, txOperation);
        var bHasTable = Convert.ToInt64(cmdTable.ExecuteScalar()) == 1;
        if (bRequireExistingAnchor && !bHasTable)
            throw new CryptographicException("The audit integrity anchor is missing.");
        using var cmdColumn = new SqliteCommand("SELECT count(*) FROM pragma_table_info('audit_log') WHERE name='signature'", dbConnection, txOperation);
        if (Convert.ToInt64(cmdColumn.ExecuteScalar()) == 0)
        {
            if (bRequireExistingAnchor) throw new CryptographicException("The audit signature column is missing.");
            using var cmdAlter = new SqliteCommand("ALTER TABLE audit_log ADD COLUMN signature BLOB CHECK(signature IS NULL OR length(signature)=32)", dbConnection, txOperation);
            cmdAlter.ExecuteNonQuery();
        }
        using (var cmdCreate = new SqliteCommand("""
            CREATE TABLE IF NOT EXISTS audit_chain_state(
                id INTEGER PRIMARY KEY CHECK(id=1),
                legacy_last_id INTEGER NOT NULL CHECK(legacy_last_id>=0),
                legacy_digest BLOB NOT NULL CHECK(length(legacy_digest)=32),
                initialized_at TEXT NOT NULL);
            """, dbConnection, txOperation)) cmdCreate.ExecuteNonQuery();
        using var cmdCount = new SqliteCommand("SELECT count(*) FROM audit_chain_state", dbConnection, txOperation);
        var lngAnchors = Convert.ToInt64(cmdCount.ExecuteScalar());
        if (lngAnchors == 0)
        {
            if (bRequireExistingAnchor || bHasTable) throw new CryptographicException("The audit integrity anchor is missing.");
            using var cmdSigned = new SqliteCommand("SELECT count(*) FROM audit_log WHERE signature IS NOT NULL", dbConnection, txOperation);
            if (Convert.ToInt64(cmdSigned.ExecuteScalar()) != 0)
                throw new CryptographicException("Existing signed audit records require their original anchor.");
            using var cmdLast = new SqliteCommand("SELECT COALESCE(MAX(id),0) FROM audit_log", dbConnection, txOperation);
            var lngLastId = Convert.ToInt64(cmdLast.ExecuteScalar());
            var strInitialized = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            WithKey(dbConnection, arrKey =>
            {
                var arrDigest = LegacyDigest(dbConnection, txOperation, arrKey, lngLastId, strInitialized);
                try
                {
                    using var cmdAnchor = new SqliteCommand("INSERT INTO audit_chain_state(id,legacy_last_id,legacy_digest,initialized_at) VALUES(1,$last,$digest,$now)", dbConnection, txOperation);
                    cmdAnchor.Parameters.AddWithValue("$last", lngLastId); cmdAnchor.Parameters.AddWithValue("$digest", arrDigest);
                    cmdAnchor.Parameters.AddWithValue("$now", strInitialized); cmdAnchor.ExecuteNonQuery();
                }
                finally { SensitiveMemory.Clear(arrDigest); }
                return true;
            });
        }
        else if (lngAnchors != 1) throw new CryptographicException("The audit integrity anchor is invalid.");
        using (var cmdGuards = new SqliteCommand("""
            CREATE TRIGGER IF NOT EXISTS protect_audit_anchor BEFORE DELETE ON audit_chain_state BEGIN SELECT RAISE(ABORT,'Audit anchor is immutable'); END;
            CREATE TRIGGER IF NOT EXISTS immutable_audit_anchor BEFORE UPDATE ON audit_chain_state BEGIN SELECT RAISE(ABORT,'Audit anchor is immutable'); END;
            CREATE TRIGGER IF NOT EXISTS signed_audit_insert BEFORE INSERT ON audit_log
            WHEN NEW.signature IS NULL OR length(NEW.signature)<>32
            BEGIN SELECT RAISE(ABORT,'A signed audit entry is required'); END;
            """, dbConnection, txOperation)) cmdGuards.ExecuteNonQuery();
        Verify(dbConnection, txOperation);
    }

    internal static void Verify(SqliteConnection dbConnection, SqliteTransaction txOperation)
    {
        VerifyFreshness(dbConnection, txOperation);
        WithKey(dbConnection, arrKey =>
        {
            var arrPrevious = VerifyPrefix(dbConnection, txOperation, arrKey);
            try { return true; }
            finally { SensitiveMemory.Clear(arrPrevious); }
        });
    }

    internal static void Append(SqliteConnection dbConnection, SqliteTransaction txOperation, long? lngActorId,
        AuditEntity enEntity, long lngEntityId, AuditAction enAction, long? lngSubjectId = null)
    {
        if (!Enum.IsDefined(enEntity) || !Enum.IsDefined(enAction)) throw new ArgumentException("Select a valid audit event.");
        VerifyFreshness(dbConnection, txOperation);
        WithKey(dbConnection, arrKey =>
        {
            var arrPrevious = VerifyPrefix(dbConnection, txOperation, arrKey);
            byte[]? arrPayload = null;
            var arrSignature = SensitiveMemory.AllocateBytes(32);
            try
            {
                using var cmdLast = new SqliteCommand("SELECT COALESCE(MAX(id),0) FROM audit_log", dbConnection, txOperation);
                var lngId = checked(Convert.ToInt64(cmdLast.ExecuteScalar()) + 1);
                var strNow = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                arrPayload = Encode(lngId, lngActorId, lngSubjectId, enEntity.ToString(), lngEntityId, enAction.ToString(), strNow);
                using var objHash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, arrKey);
                objHash.AppendData(arrPrevious); objHash.AppendData(arrPayload);
                objHash.GetHashAndReset(arrSignature);
                using var cmdAudit = new SqliteCommand("INSERT INTO audit_log(id,user_id,subject_user_id,entity,entity_id,action,created_at,signature) VALUES($id,$actor,$subject,$entity,$record,$action,$now,$signature)", dbConnection, txOperation);
                cmdAudit.Parameters.AddWithValue("$id", lngId); cmdAudit.Parameters.AddWithValue("$actor", (object?)lngActorId ?? DBNull.Value);
                cmdAudit.Parameters.AddWithValue("$subject", (object?)lngSubjectId ?? DBNull.Value); cmdAudit.Parameters.AddWithValue("$entity", enEntity.ToString());
                cmdAudit.Parameters.AddWithValue("$record", lngEntityId); cmdAudit.Parameters.AddWithValue("$action", enAction.ToString());
                cmdAudit.Parameters.AddWithValue("$now", strNow); cmdAudit.Parameters.AddWithValue("$signature", arrSignature); cmdAudit.ExecuteNonQuery();
            }
            finally { SensitiveMemory.Clear(arrPrevious); SensitiveMemory.Clear(arrSignature); SensitiveMemory.Clear(arrPayload); }
            return true;
        });
    }

    private static byte[] VerifyPrefix(SqliteConnection dbConnection, SqliteTransaction txOperation, byte[] arrKey)
    {
        long lngBoundary; string strInitialized;
        var arrExpectedRoot = SensitiveMemory.AllocateBytes(32);
        byte[]? arrPrevious = null; var bTransferred = false;
        try
        {
            using (var cmdAnchor = new SqliteCommand("SELECT legacy_last_id,legacy_digest,initialized_at FROM audit_chain_state WHERE id=1", dbConnection, txOperation))
            {
                using var rdrAnchor = cmdAnchor.ExecuteReader();
                if (!rdrAnchor.Read()) throw new CryptographicException("The audit integrity anchor is missing.");
                lngBoundary = rdrAnchor.GetInt64(0); strInitialized = rdrAnchor.GetString(2);
                ReadSignature(rdrAnchor, 1, arrExpectedRoot);
            }
            arrPrevious = LegacyDigest(dbConnection, txOperation, arrKey, lngBoundary, strInitialized);
            if (!CryptographicOperations.FixedTimeEquals(arrPrevious, arrExpectedRoot))
                throw new CryptographicException("The historical audit snapshot failed verification.");
            using var cmdRecords = new SqliteCommand("SELECT id,user_id,subject_user_id,entity,entity_id,action,created_at,signature FROM audit_log WHERE id>$boundary ORDER BY id", dbConnection, txOperation);
            cmdRecords.Parameters.AddWithValue("$boundary", lngBoundary);
            using var rdrRecords = cmdRecords.ExecuteReader();
            var lngExpectedId = lngBoundary;
            while (rdrRecords.Read())
            {
                if (rdrRecords.GetInt64(0) != checked(++lngExpectedId)) throw new CryptographicException("The audit chain contains a gap.");
                var arrPayload = EncodeRow(rdrRecords);
                var arrStored = SensitiveMemory.AllocateBytes(32); var arrActual = SensitiveMemory.AllocateBytes(32);
                try
                {
                    ReadSignature(rdrRecords, 7, arrStored);
                    using var objHash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, arrKey);
                    objHash.AppendData(arrPrevious); objHash.AppendData(arrPayload); objHash.GetHashAndReset(arrActual);
                    if (!CryptographicOperations.FixedTimeEquals(arrStored, arrActual))
                        throw new CryptographicException("The audit chain failed verification.");
                    arrActual.CopyTo(arrPrevious, 0);
                }
                finally { SensitiveMemory.Clear(arrPayload); SensitiveMemory.Clear(arrStored); SensitiveMemory.Clear(arrActual); }
            }
            bTransferred = true; return arrPrevious;
        }
        finally { SensitiveMemory.Clear(arrExpectedRoot); if (!bTransferred) SensitiveMemory.Clear(arrPrevious); }
    }

    private static byte[] LegacyDigest(SqliteConnection dbConnection, SqliteTransaction txOperation, byte[] arrKey, long lngBoundary, string strInitialized)
    {
        using var objHash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, arrKey);
        var arrHeader = Encode(lngBoundary, null, null, "LegacySnapshot/v1", 0, "Anchor", strInitialized);
        try { objHash.AppendData(arrHeader); }
        finally { SensitiveMemory.Clear(arrHeader); }
        using var cmdRecords = new SqliteCommand("SELECT id,user_id,subject_user_id,entity,entity_id,action,created_at,signature FROM audit_log WHERE id<=$boundary ORDER BY id", dbConnection, txOperation);
        cmdRecords.Parameters.AddWithValue("$boundary", lngBoundary);
        using var rdrRecords = cmdRecords.ExecuteReader();
        var lngLastObserved = 0L;
        while (rdrRecords.Read())
        {
            if (!rdrRecords.IsDBNull(7)) throw new CryptographicException("The audit legacy boundary is invalid.");
            lngLastObserved = rdrRecords.GetInt64(0);
            var arrPayload = EncodeRow(rdrRecords);
            try { objHash.AppendData(arrPayload); }
            finally { SensitiveMemory.Clear(arrPayload); }
        }
        if (lngLastObserved != lngBoundary) throw new CryptographicException("The historical audit snapshot is incomplete.");
        var arrDigest = SensitiveMemory.AllocateBytes(32);
        try { objHash.GetHashAndReset(arrDigest); return arrDigest; }
        catch { SensitiveMemory.Clear(arrDigest); throw; }
    }
    private static void ReadSignature(SqliteDataReader rdrRecord, int iOrdinal, byte[] arrDestination)
    {
        if (rdrRecord.IsDBNull(iOrdinal) || rdrRecord.GetBytes(iOrdinal, 0, null, 0, 0) != 32
            || rdrRecord.GetBytes(iOrdinal, 0, arrDestination, 0, 32) != 32)
            throw new CryptographicException("An audit signature is invalid.");
    }
    private static byte[] EncodeRow(SqliteDataReader rdrRecord) => Encode(rdrRecord.GetInt64(0),
        rdrRecord.IsDBNull(1) ? null : rdrRecord.GetInt64(1), rdrRecord.IsDBNull(2) ? null : rdrRecord.GetInt64(2),
        rdrRecord.GetString(3), rdrRecord.GetInt64(4), rdrRecord.GetString(5), rdrRecord.GetString(6));

    private static byte[] Encode(long lngId, long? lngActor, long? lngSubject, string strEntity, long lngRecord, string strAction, string strTimestamp)
    {
        using var stmPayload = new MemoryStream();
        try
        {
            using (var objWriter = new BinaryWriter(stmPayload, Encoding.UTF8, leaveOpen: true))
            {
                objWriter.Write("HippoCrmAuditEntry/v1"); objWriter.Write(lngId);
                objWriter.Write(lngActor.HasValue); objWriter.Write(lngActor.GetValueOrDefault());
                objWriter.Write(lngSubject.HasValue); objWriter.Write(lngSubject.GetValueOrDefault());
                objWriter.Write(strEntity); objWriter.Write(lngRecord); objWriter.Write(strAction); objWriter.Write(strTimestamp);
            }
            var arrPayload = SensitiveMemory.AllocateBytes(checked((int)stmPayload.Length));
            stmPayload.GetBuffer().AsSpan(0, arrPayload.Length).CopyTo(arrPayload);
            return arrPayload;
        }
        finally { SensitiveMemory.Clear(stmPayload.GetBuffer()); }
    }
}
