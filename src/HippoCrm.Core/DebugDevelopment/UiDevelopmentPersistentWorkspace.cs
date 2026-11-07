#if DEBUG
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed partial class Database
{
    public static Database OpenUiDevelopmentWorkspace(string? strDirectory = null, IDatabaseKeyStore? objKeys = null)
    {
        if (strDirectory is null)
        {
            var strAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(strAppData) || !System.IO.Path.IsPathFullyQualified(strAppData))
                throw new InvalidOperationException("The local Debug application data directory is unavailable.");
            strDirectory = System.IO.Path.Combine(strAppData, "BigEagleGroup", "HippoCrm",
                OperatingSystem.IsMacOS() ? "UiDevelopment-PasswordOnly" : "UiDevelopment");
        }
        strDirectory = System.IO.Path.GetFullPath(strDirectory);
        var strLeaf = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(strDirectory));
        if (!strLeaf.Equals("UiDevelopment", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && !strLeaf.Equals("UiDevelopment-PasswordOnly", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Use a dedicated UiDevelopment directory for the Debug demo.");
        objKeys ??= OperatingSystem.IsWindows() ? new WindowsCredentialKeyStore()
            : OperatingSystem.IsMacOS() ? new MacFileKeyStore()
            : throw new PlatformNotSupportedException("A platform-specific Debug key store is required.");
        return new Database(new UiDevelopmentPersistent(strDirectory, objKeys));
    }

    private sealed class UiDevelopmentPersistent : IUiDevelopmentWorkspace
    {
        public IDatabaseKeyStore objKeys { get; }
        public string strPath { get; }
        private readonly string strAuditIdentity;
        private readonly UiDevelopmentFileCheckpoint objCheckpoint;
        private bool bAllowCreate;
        private bool bDisposed;

        internal UiDevelopmentPersistent(string strDirectory, IDatabaseKeyStore objStore)
        {
            strPath = SqlCipherConnectionFactory.ValidateLocalPath(System.IO.Path.Combine(strDirectory, "ui-development.db"));
            objKeys = objStore;
            strAuditIdentity = strPath + ".audit-master";
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(strPath)!);
            objCheckpoint = new UiDevelopmentFileCheckpoint(strPath, objStore, strAuditIdentity);
            try
            {
                bAllowCreate = objCheckpoint.bNewWorkspace;
                SQLitePCL.Batteries_V2.Init();
            }
            catch { objCheckpoint.Dispose(); throw; }
        }
        public void KeepAlive() { }
        public SqliteConnection Open()
        {
            ObjectDisposedException.ThrowIf(bDisposed, this);
            var strChecked = SqlCipherConnectionFactory.ValidateLocalPath(strPath);
            var objBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = strChecked, Mode = bAllowCreate ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private, Pooling = false
            };
            var dbConnection = new SqliteConnection(objBuilder.ToString());
            try
            {
                dbConnection.Open(); bAllowCreate = false;
                ApplyKey(dbConnection);
                using (var cmdCipher = new SqliteCommand("PRAGMA cipher_version", dbConnection))
                    if (cmdCipher.ExecuteScalar() is not string strVersion || !strVersion.StartsWith("4.", StringComparison.Ordinal))
                        throw new CryptographicException("SQLCipher is required for the Debug demo workspace.");
                using (var cmdSettings = new SqliteCommand("PRAGMA cipher_memory_security=ON; PRAGMA temp_store=MEMORY; PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON; PRAGMA synchronous=FULL; PRAGMA fullfsync=ON;", dbConnection))
                    cmdSettings.ExecuteNonQuery();
                SecurityAudit.RegisterConnection(dbConnection, objKeys, strAuditIdentity, objCheckpoint);
                using var txCheck = dbConnection.BeginTransaction(false);
                using var cmdAnchor = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='audit_chain_state'", dbConnection, txCheck);
                if (Convert.ToInt64(cmdAnchor.ExecuteScalar()) != 0)
                {
                    SecurityAudit.Verify(dbConnection, txCheck);
                    SecurityAudit.Commit(dbConnection, txCheck);
                }
                else
                {
                    TrustedAuditProtocol.Verify(objCheckpoint, null);
                    using var cmdTables = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'", dbConnection, txCheck);
                    if (Convert.ToInt64(cmdTables.ExecuteScalar()) != 0) throw new AuditRecoveryRequiredException();
                    txCheck.Commit(); // Read-only bootstrap check; schema/audit enrollment follows atomically.
                }
                return dbConnection;
            }
            catch { dbConnection.Dispose(); throw; }
        }
        private void ApplyKey(SqliteConnection dbConnection)
        {
            byte[]? arrKey = null;
            var gchKey = default(GCHandle);
            try
            {
                arrKey = objKeys.ReadKey(strPath);
                if (arrKey.Length != 32) throw new CryptographicException("The Debug encryption key is invalid.");
                gchKey = GCHandle.Alloc(arrKey, GCHandleType.Pinned);
                if (UiSqliteKey(dbConnection.Handle!.DangerousGetHandle(), gchKey.AddrOfPinnedObject(), arrKey.Length) != 0)
                    throw new CryptographicException("The Debug workspace key could not be applied.");
            }
            finally { SensitiveMemory.Clear(arrKey); if (gchKey.IsAllocated) gchKey.Free(); }
        }
        public void Dispose()
        {
            if (bDisposed) return;
            bDisposed = true; objCheckpoint.Dispose();
        }
        [DllImport("e_sqlcipher", EntryPoint = "sqlite3_key", CallingConvention = CallingConvention.Cdecl)]
        private static extern int UiSqliteKey(IntPtr ptrDatabase, IntPtr ptrKey, int iKeyLength);
    }
}
#endif
