using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace HippoCrm.Core;

// Local account-scoped key files avoid macOS Keychain authorization dialogs.
// Keys are random and files are restricted to the current macOS user; the app
// password remains the sign-in gate but does not encrypt these key files.
[SupportedOSPlatform("macos")]
public sealed class MacFileKeyStore : IDatabaseKeyStore
{
    private static readonly ConcurrentDictionary<string, object> objPathLocks = new(StringComparer.Ordinal);
    private static readonly UnixFileMode enDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly UnixFileMode enFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public void EnsureKey(string strDatabasePath, bool bAllowCreate)
    {
        var strKeyPath = GetKeyPath(strDatabasePath);
        lock (objPathLocks.GetOrAdd(strKeyPath, _ => new object()))
        {
            if (File.Exists(strKeyPath))
            {
                ApplyPrivatePermissions(strKeyPath, false);
                var arrExisting = ReadKeyFile(strKeyPath);
                SensitiveMemory.Clear(arrExisting);
                return;
            }
            if (!bAllowCreate)
                throw new CryptographicException("The workspace key is unavailable. Contact your administrator.");

            var strDirectory = Path.GetDirectoryName(strKeyPath)!;
            Directory.CreateDirectory(strDirectory);
            ApplyPrivatePermissions(strDirectory, true);
            var arrKey = SensitiveMemory.AllocateBytes(32);
            try
            {
                RandomNumberGenerator.Fill(arrKey);
                try
                {
                    using var stmKey = new FileStream(strKeyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        4096, FileOptions.WriteThrough);
                    File.SetUnixFileMode(strKeyPath, enFileMode);
                    stmKey.Write(arrKey);
                    stmKey.Flush(flushToDisk: true);
                }
                catch (IOException) when (File.Exists(strKeyPath))
                {
                    // Another process provisioned the same workspace key first.
                    var arrWinner = ReadKeyFile(strKeyPath);
                    SensitiveMemory.Clear(arrWinner);
                }
            }
            finally { SensitiveMemory.Clear(arrKey); }
        }
    }

    public byte[] ReadKey(string strDatabasePath)
    {
        var strKeyPath = GetKeyPath(strDatabasePath);
        lock (objPathLocks.GetOrAdd(strKeyPath, _ => new object()))
        {
            if (!File.Exists(strKeyPath))
                throw new CryptographicException("The workspace key is unavailable. Contact your administrator.");
            ApplyPrivatePermissions(strKeyPath, false);
            return ReadKeyFile(strKeyPath);
        }
    }

    internal void DeleteKeyForTesting(string strDatabasePath)
    {
        var strKeyPath = GetKeyPath(strDatabasePath);
        lock (objPathLocks.GetOrAdd(strKeyPath, _ => new object()))
            if (File.Exists(strKeyPath)) File.Delete(strKeyPath);
    }

    private static string GetKeyPath(string strDatabasePath)
    {
        var strAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(strAppData) || !Path.IsPathFullyQualified(strAppData))
            throw new InvalidOperationException("The local application data directory is unavailable.");
        var strIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(strDatabasePath))));
        return Path.Combine(strAppData, "BigEagleGroup", "HippoCrm", "LocalKeys", strIdentity + ".key");
    }

    private static byte[] ReadKeyFile(string strKeyPath)
    {
        using var stmKey = new FileStream(strKeyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stmKey.Length != 32) throw new CryptographicException("The workspace key file is invalid.");
        var arrKey = SensitiveMemory.AllocateBytes(32);
        try
        {
            stmKey.ReadExactly(arrKey);
            return arrKey;
        }
        catch
        {
            SensitiveMemory.Clear(arrKey);
            throw;
        }
    }

    private static void ApplyPrivatePermissions(string strPath, bool bDirectory)
    {
        File.SetUnixFileMode(strPath, bDirectory ? enDirectoryMode : enFileMode);
    }
}
