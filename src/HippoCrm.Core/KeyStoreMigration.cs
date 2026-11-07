using System.Security.Cryptography;

namespace HippoCrm.Core;

public interface IKeyImportStore : IDatabaseKeyStore
{
    // Caller retains ownership. Import must be add-only and reject conflicting keys.
    void ImportKey(string strWorkspaceIdentity, byte[] arrKey);
}

public static class KeyStoreMigration
{
    // Copies one existing key without changing the database or removing recovery material.
    // Call separately for database and audit identities while the workspace is closed.
    public static void CopyVerifiedKey(IDatabaseKeyStore objSource, IKeyImportStore objDestination,
        string strWorkspaceIdentity)
    {
        ArgumentNullException.ThrowIfNull(objSource);
        ArgumentNullException.ThrowIfNull(objDestination);
        ArgumentException.ThrowIfNullOrWhiteSpace(strWorkspaceIdentity);
        byte[]? arrSource = null;
        byte[]? arrConfirmed = null;
        try
        {
            arrSource = objSource.ReadKey(strWorkspaceIdentity);
            if (arrSource.Length != 32) throw new CryptographicException("The workspace key is invalid.");
            objDestination.ImportKey(strWorkspaceIdentity, arrSource);
            arrConfirmed = objDestination.ReadKey(strWorkspaceIdentity);
            if (arrConfirmed.Length != 32 || !CryptographicOperations.FixedTimeEquals(arrSource, arrConfirmed))
                throw new CryptographicException("The protected workspace key could not be verified.");
        }
        finally
        {
            SensitiveMemory.Clear(arrConfirmed);
            SensitiveMemory.Clear(arrSource);
        }
    }
}