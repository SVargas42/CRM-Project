using System.Security.Cryptography;
using HippoCrm.Core;

namespace HippoCrm.TestSupport;

// Fictional test databases only. Production uses the OS credential store.
public sealed class EphemeralKeyStore : IDatabaseKeyStore, IDisposable
{
    private readonly byte[] arrKey = RandomNumberGenerator.GetBytes(32);
    public byte[]? arrLastRead { get; private set; }
    public void EnsureKey(string strDatabasePath, bool bAllowCreate) { }
    public byte[] ReadKey(string strDatabasePath)
    {
        arrLastRead = (byte[])arrKey.Clone();
        return arrLastRead;
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(arrKey);
}
