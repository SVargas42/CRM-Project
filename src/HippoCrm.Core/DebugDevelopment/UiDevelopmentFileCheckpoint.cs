#if DEBUG
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HippoCrm.Core;

// DEBUG MOCK ONLY. Authentication detects edits, not rollback of both local files.
// This is never registered as a production rollback-resistant checkpoint provider.
internal sealed class UiDevelopmentFileCheckpoint : ITrustedAuditCheckpointLease
{
    private sealed record Body(int iFormatVersion, string strWorkspace, AuditCheckpoint objCheckpoint);
    private sealed record Envelope(Body objBody, string strMac);
    private readonly object objGate = new();
    private readonly IDatabaseKeyStore objKeys;
    private readonly string strAuditIdentity;
    private readonly string strWorkspace;
    private readonly string strCheckpointPath;
    private readonly FileStream stmLease;
    private bool bDisposed;
    internal bool bNewWorkspace { get; }

    internal UiDevelopmentFileCheckpoint(string strDatabase, IDatabaseKeyStore objStore, string strAuditKeyIdentity)
    {
        objKeys = objStore; strAuditIdentity = strAuditKeyIdentity;
        var strDirectory = Path.GetDirectoryName(strDatabase)!;
        strCheckpointPath = SqlCipherConnectionFactory.ValidateLocalPath(Path.Combine(strDirectory, "checkpoint.json"));
        var strLeasePath = SqlCipherConnectionFactory.ValidateLocalPath(Path.Combine(strDirectory, "workspace.lease"));
        // The file persists, ownership does not. The OS releases the handle on crash/exit.
        stmLease = new FileStream(strLeasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var strCanonical = OperatingSystem.IsWindows() ? strDatabase.ToUpperInvariant() : strDatabase;
            strWorkspace = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(strCanonical)));
            var bDatabase = File.Exists(strDatabase); var bCheckpoint = File.Exists(strCheckpointPath);
            if (bDatabase != bCheckpoint) throw new AuditRecoveryRequiredException();
            bNewWorkspace = !bDatabase;
            objStore.EnsureKey(strDatabase, bNewWorkspace);
            objStore.EnsureKey(strAuditIdentity, bNewWorkspace);
            if (bNewWorkspace) Write(new AuditCheckpoint(0, null, null));
            else Read();
        }
        catch { stmLease.Dispose(); throw; }
    }
    public AuditCheckpoint Read()
    {
        lock (objGate)
        {
            ObjectDisposedException.ThrowIf(bDisposed, this);
            SqlCipherConnectionFactory.ValidateLocalPath(strCheckpointPath);
            using var stmInput = new FileStream(strCheckpointPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stmInput.Length is <= 0 or > 16384) throw new CryptographicException("The Debug checkpoint is invalid.");
            try
            {
                var objEnvelope = JsonSerializer.Deserialize<Envelope>(stmInput) ?? throw new CryptographicException("The Debug checkpoint is missing.");
                if (objEnvelope.objBody is null || objEnvelope.objBody.iFormatVersion != 1 || objEnvelope.objBody.strWorkspace != strWorkspace || objEnvelope.objBody.objCheckpoint is null)
                    throw new CryptographicException("The Debug checkpoint belongs to another workspace or format.");
                var arrActual = Authenticate(objEnvelope.objBody);
                byte[]? arrStored = null;
                try
                {
                    arrStored = Convert.FromHexString(objEnvelope.strMac);
                    if (!CryptographicOperations.FixedTimeEquals(arrActual, arrStored)) throw new CryptographicException("The Debug checkpoint failed authentication.");
                    return objEnvelope.objBody.objCheckpoint;
                }
                finally { SensitiveMemory.Clear(arrActual); SensitiveMemory.Clear(arrStored); }
            }
            catch (Exception objException) when (objException is JsonException or FormatException or ArgumentException)
            { throw new CryptographicException("The Debug checkpoint is invalid.", objException); }
        }
    }
    public void CompareExchange(long lngExpectedVersion, AuditCheckpoint objReplacement)
    {
        lock (objGate)
        {
            var objCurrent = Read();
            if (objCurrent.lngVersion != lngExpectedVersion || objReplacement.lngVersion != checked(lngExpectedVersion + 1))
                throw new InvalidOperationException("The Debug checkpoint version changed.");
            Write(objReplacement);
        }
    }
    private byte[] Authenticate(Body objBody)
    {
        byte[]? arrMaster = null;
        var arrKey = SensitiveMemory.AllocateBytes(32);
        var arrPayload = JsonSerializer.SerializeToUtf8Bytes(objBody);
        var arrMac = SensitiveMemory.AllocateBytes(32);
        var bTransferred = false;
        try
        {
            arrMaster = objKeys.ReadKey(strAuditIdentity);
            if (arrMaster.Length != 32) throw new CryptographicException("The Debug checkpoint key is invalid.");
            HMACSHA256.HashData(arrMaster, "HippoCrm/DebugUiCheckpoint/v1"u8, arrKey);
            HMACSHA256.HashData(arrKey, arrPayload, arrMac);
            bTransferred = true; return arrMac;
        }
        finally
        {
            SensitiveMemory.Clear(arrMaster); SensitiveMemory.Clear(arrKey); SensitiveMemory.Clear(arrPayload);
            if (!bTransferred) SensitiveMemory.Clear(arrMac);
        }
    }
    private void Write(AuditCheckpoint objState)
    {
        SqlCipherConnectionFactory.ValidateLocalPath(strCheckpointPath);
        var objBody = new Body(1, strWorkspace, objState);
        var arrMac = Authenticate(objBody);
        var strTemporary = strCheckpointPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var arrJson = JsonSerializer.SerializeToUtf8Bytes(new Envelope(objBody, Convert.ToHexString(arrMac)));
            try
            {
                using (var stmOutput = new FileStream(strTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stmOutput.Write(arrJson); stmOutput.Flush(flushToDisk: true); }
                File.Move(strTemporary, strCheckpointPath, overwrite: true);
            }
            finally { SensitiveMemory.Clear(arrJson); }
        }
        finally
        {
            SensitiveMemory.Clear(arrMac);
            if (File.Exists(strTemporary)) File.Delete(strTemporary); // Public checkpoint metadata only; never key/data files.
        }
    }
    public void Dispose()
    {
        lock (objGate) { if (bDisposed) return; bDisposed = true; stmLease.Dispose(); }
    }
}
#endif
