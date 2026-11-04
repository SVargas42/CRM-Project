using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
namespace HippoCrm.Core;

internal static class SqlCipherConnectionFactory
{
    private static readonly Lazy<bool> objProviderInitialization = new(() => { SQLitePCL.Batteries_V2.Init(); return true; });
    internal static SqliteConnection Open(string strDatabasePath, IDatabaseKeyStore objKeyStore, bool bCreate = false)
    {
        ArgumentNullException.ThrowIfNull(objKeyStore);
        var strPath = ValidateLocalPath(strDatabasePath);
        _ = objProviderInitialization.Value;
        objKeyStore.EnsureKey(strPath, bCreate);
        var csbConnection = new SqliteConnectionStringBuilder
        {
            DataSource = strPath, Mode = bCreate ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private, Pooling = false
        };
        var dbConnection = new SqliteConnection(csbConnection.ToString());
        try
        {
            dbConnection.Open();
            using (var cmdVersion = new SqliteCommand("PRAGMA cipher_version;", dbConnection))
            {
                if (cmdVersion.ExecuteScalar() is not string strVersion || !strVersion.StartsWith("4.", StringComparison.Ordinal))
                    throw new CryptographicException("The required encryption engine is unavailable.");
            }
            ApplyKey(dbConnection, objKeyStore, strPath);
            using (var cmdSettings = new SqliteCommand("PRAGMA cipher_memory_security=ON; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON;", dbConnection))
                cmdSettings.ExecuteNonQuery();
            using (var cmdValidate = new SqliteCommand("SELECT count(*) FROM sqlite_master", dbConnection)) cmdValidate.ExecuteScalar();
            // This is a distinct OS-protected credential, not a key file.
            var strAuditIdentity = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(strPath)!, System.IO.Path.GetFileName(strPath) + ".audit-master");
            var bHasAnchor = false;
            using (var cmdExists = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='audit_chain_state'", dbConnection))
            {
                if (Convert.ToInt64(cmdExists.ExecuteScalar()) != 0)
                {
                    using var cmdAnchor = new SqliteCommand("SELECT count(*) FROM audit_chain_state WHERE id=1", dbConnection);
                    bHasAnchor = Convert.ToInt64(cmdAnchor.ExecuteScalar()) == 1;
                }
            }
            objKeyStore.EnsureKey(strAuditIdentity, !bHasAnchor);
            SecurityAudit.RegisterConnection(dbConnection, objKeyStore, strAuditIdentity);
            if (bHasAnchor)
            {
                using var txAudit = dbConnection.BeginTransaction(deferred: false);
                SecurityAudit.Verify(dbConnection, txAudit); txAudit.Commit();
            }
            return dbConnection;
        }
        catch { dbConnection.Dispose(); throw; }
    }
    private static void ApplyKey(SqliteConnection dbConnection, IDatabaseKeyStore objKeyStore, string strPath)
    {
        byte[]? arrKey = null; var gchKey = default(GCHandle);
        try
        {
            arrKey = objKeyStore.ReadKey(strPath);
            if (arrKey.Length != 32) throw new CryptographicException("The workspace key is unavailable.");
            gchKey = GCHandle.Alloc(arrKey, GCHandleType.Pinned);
            if (SqliteKey(dbConnection.Handle!.DangerousGetHandle(), gchKey.AddrOfPinnedObject(), arrKey.Length) != 0)
                throw new CryptographicException("The encrypted workspace cannot be opened.");
        }
        finally { SensitiveMemory.Clear(arrKey); if (gchKey.IsAllocated) gchKey.Free(); }
    }
    internal static string ValidateLocalPath(string strDatabasePath)
    {
        if (!System.IO.Path.IsPathFullyQualified(strDatabasePath)) throw new ArgumentException("Choose an absolute local workspace path.");
        var strPath = System.IO.Path.GetFullPath(strDatabasePath);
        if (strPath.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidOperationException("Network workspaces are prohibited.");
        var enComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var objDrive = DriveInfo.GetDrives().Where(objCandidate =>
        {
            var strRoot = System.IO.Path.TrimEndingDirectorySeparator(objCandidate.Name);
            var strPrefix = System.IO.Path.EndsInDirectorySeparator(strRoot) ? strRoot : strRoot + System.IO.Path.DirectorySeparatorChar;
            return strPath.Equals(strRoot, enComparison) || strPath.StartsWith(strPrefix, enComparison);
        }).OrderByDescending(objCandidate => objCandidate.Name.Length).FirstOrDefault();
        if (objDrive is null || objDrive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram))
            throw new InvalidOperationException("The workspace must be on a verified local volume.");
        var lstAncestors = new List<DirectoryInfo>();
        for (var objDirectory = new DirectoryInfo(System.IO.Path.GetDirectoryName(strPath)!); objDirectory is not null; objDirectory = objDirectory.Parent)
            lstAncestors.Add(objDirectory);
        // Inspect root first: probing a child before rejecting a parent junction
        // could follow that junction onto a remote filesystem.
        foreach (var objDirectory in lstAncestors.AsEnumerable().Reverse())
            if (objDirectory.LinkTarget is not null) throw new InvalidOperationException("Workspace aliases are prohibited.");
        if (new FileInfo(strPath).LinkTarget is not null) throw new InvalidOperationException("Workspace aliases are prohibited.");
        return strPath;
    }
    [DllImport("e_sqlcipher", EntryPoint = "sqlite3_key", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SqliteKey(IntPtr ptrDatabase, IntPtr ptrKey, int iKeyLength);
}
