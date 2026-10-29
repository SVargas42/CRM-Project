using System.Security.Cryptography;

namespace HippoCrm.Core;

public enum UserRole { Administrator, Editor, Viewer }
public enum RecordKind { Contact, Organization }

// Only authentication can issue a session. Identity fields alone are never authorization evidence.
public sealed class Session : IDisposable
{
    private readonly byte[] arrToken = GC.AllocateUninitializedArray<byte>(32, pinned: true);
    private bool bDisposed;
    public long UserId { get; }
    public string Username { get; }

    internal Session(long lngUserId, string strUsername)
    {
        UserId = lngUserId; Username = strUsername;
        RandomNumberGenerator.Fill(arrToken);
    }

    internal byte[] TokenHash()
    {
        ObjectDisposedException.ThrowIf(bDisposed, this);
        return SHA256.HashData(arrToken);
    }

    public void Dispose()
    {
        if (bDisposed) return;
        CryptographicOperations.ZeroMemory(arrToken);
        bDisposed = true;
    }
}
