using System.Security.Cryptography;
namespace HippoCrm.Core;

public enum UserRole { Administrator, Editor, Viewer }
public enum RecordKind { Contact, Organization }

public sealed class Session : IDisposable
{
    private readonly object objGate = new();
    private readonly byte[] arrToken = SensitiveMemory.AllocateBytes(32);
    private bool bDisposed;
    public long UserId { get; }
    public string Username { get; }

    internal Session(long lngUserId, string strUsername)
    {
        UserId = lngUserId; Username = strUsername;
        var bInitialized = false;
        try { RandomNumberGenerator.Fill(arrToken); bInitialized = true; }
        finally { if (!bInitialized) SensitiveMemory.Clear(arrToken); }
    }

    internal T UseTokenHash<T>(Func<byte[], T> fnUseHash)
    {
        ArgumentNullException.ThrowIfNull(fnUseHash);
        lock (objGate)
        {
            ObjectDisposedException.ThrowIf(bDisposed, this);
            var arrHash = SensitiveMemory.AllocateBytes(32);
            try { SHA256.HashData(arrToken.AsSpan(), arrHash.AsSpan()); return fnUseHash(arrHash); }
            finally { SensitiveMemory.Clear(arrHash); }
        }
    }

    public void Dispose()
    {
        lock (objGate)
        {
            if (bDisposed) return;
            try { bDisposed = true; }
            finally { SensitiveMemory.Clear(arrToken); }
        }
    }
}
