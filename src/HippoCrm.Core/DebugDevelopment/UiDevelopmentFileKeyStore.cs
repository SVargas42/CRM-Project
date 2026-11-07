#if DEBUG
using System.Security.Cryptography;

namespace HippoCrm.Core;

// Approved fictional-data exception. Selected only by the macOS Debug factory;
// tests may instantiate on Windows, where Unix permission assertions are skipped.
internal sealed class UiDevelopmentFileKeyStore : IDatabaseKeyStore
{
    private readonly string strDirectory;
    private readonly string strDatabase;
    private readonly StringComparison enComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal UiDevelopmentFileKeyStore(string strDirectory)
    {
        this.strDirectory = Path.GetFullPath(strDirectory);
        if (!Path.GetFileName(Path.TrimEndingDirectorySeparator(this.strDirectory)).Equals("UiDevelopment", enComparison))
            throw new ArgumentException("Use a dedicated UiDevelopment directory for the Debug demo.");
        strDatabase = Path.Combine(this.strDirectory, "ui-development.db");
    }
    private string KeyPath(string strIdentity)
    {
        var strFullIdentity = Path.GetFullPath(strIdentity);
        var strName = strFullIdentity.Equals(strDatabase, enComparison) ? "workspace.key"
            : strFullIdentity.Equals(strDatabase + ".audit-master", enComparison) ? "audit-master.key" : null;
        if (strName is null) throw new AuditRecoveryRequiredException();
        return SqlCipherConnectionFactory.ValidateLocalPath(Path.Combine(strDirectory, strName));
    }
    public void EnsureKey(string strIdentity, bool bAllowCreate)
    {
        var strKey = KeyPath(strIdentity);
        if (File.Exists(strKey)) { SensitiveMemory.Clear(ReadKey(strIdentity)); return; }
        // Never invent replacement credentials for an existing or half-restored workspace.
        if (!bAllowCreate || File.Exists(strDatabase) || File.Exists(Path.Combine(strDirectory, "checkpoint.json")))
            throw new AuditRecoveryRequiredException();
        Directory.CreateDirectory(strDirectory);
        var arrKey = SensitiveMemory.AllocateBytes(32);
        var strTemporary = SqlCipherConnectionFactory.ValidateLocalPath(Path.Combine(strDirectory, ".key-" + Guid.NewGuid().ToString("N") + ".tmp"));
        try
        {
            RandomNumberGenerator.Fill(arrKey);
            var objOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough };
            // UnixCreateMode sets the restrictive mode at creation, never after publication.
            if (!OperatingSystem.IsWindows()) objOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stmKey = new FileStream(strTemporary, objOptions)) { stmKey.Write(arrKey); stmKey.Flush(flushToDisk: true); }
            try { File.Move(strTemporary, strKey, overwrite: false); }
            catch (IOException) when (File.Exists(strKey)) { SensitiveMemory.Clear(ReadKey(strIdentity)); }
        }
        finally
        {
            SensitiveMemory.Clear(arrKey);
            if (File.Exists(strTemporary))
            {
                // A failed publication must not leave an adapter-owned secret temporary file.
                using (var stmKey = new FileStream(strTemporary, FileMode.Open, FileAccess.Write, FileShare.None))
                { stmKey.Write(arrKey); stmKey.Flush(flushToDisk: true); }
                File.Delete(strTemporary);
            }
        }
    }
    public byte[] ReadKey(string strIdentity)
    {
        var strKey = KeyPath(strIdentity);
        if (!File.Exists(strKey)) throw new AuditRecoveryRequiredException();
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(strKey) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            throw new AuditRecoveryRequiredException();
        var arrKey = SensitiveMemory.AllocateBytes(32);
        var bTransferred = false;
        try
        {
            using var stmKey = new FileStream(strKey, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stmKey.Length != 32) throw new AuditRecoveryRequiredException();
            stmKey.ReadExactly(arrKey); bTransferred = true; return arrKey;
        }
        finally { if (!bTransferred) SensitiveMemory.Clear(arrKey); }
    }
}
#endif
