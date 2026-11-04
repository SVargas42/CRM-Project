#if DEBUG
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

// Debug fixture composition. The memory factory remains available for isolated tests;
// the persistent companion uses a dedicated encrypted demo directory and OS keys.
// Production constructors and SqlCipherConnectionFactory retain their strict gates.
public sealed partial class Database : IDisposable
{
    private readonly IUiDevelopmentWorkspace? objUiDevelopment;

    public static Database CreateUiDevelopmentWorkspace() => new(new UiDevelopmentMemory());

    private Database(IUiDevelopmentWorkspace objMemory)
    {
        objUiDevelopment = objMemory;
        objKeyStore = objMemory.objKeys;
        Path = objMemory.strPath;
        try
        {
            objMemory.KeepAlive();
            using var dbInitialization = objMemory.Open();
            using var txInitialization = dbInitialization.BeginTransaction(false);
            SchemaManager.Initialize(dbInitialization, txInitialization);
            SecurityAudit.Commit(dbInitialization, txInitialization);
        }
        catch { objMemory.Dispose(); throw; }
    }

    public void Dispose() => objUiDevelopment?.Dispose();

    private interface IUiDevelopmentWorkspace : IDisposable
    {
        IDatabaseKeyStore objKeys { get; }
        string strPath { get; }
        void KeepAlive();
        SqliteConnection Open();
    }

    private sealed class UiDevelopmentMemory : IUiDevelopmentWorkspace
    {
        internal readonly UiDevelopmentKeys objKeys = new();
        private readonly UiDevelopmentCheckpoint objCheckpoint = new();
        private readonly string strConnection;
        private SqliteConnection? dbKeeper;
        private bool bDisposed;
        IDatabaseKeyStore IUiDevelopmentWorkspace.objKeys => objKeys;
        public string strPath => ":memory:";

        internal UiDevelopmentMemory()
        {
            try
            {
                SQLitePCL.Batteries_V2.Init();
                strConnection = new SqliteConnectionStringBuilder
                {
                    DataSource = "HippoUiFixture" + Guid.NewGuid().ToString("N"),
                    Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared, Pooling = false
                }.ToString();
            }
            catch { objKeys.Dispose(); throw; }
        }
        public void KeepAlive() => dbKeeper = Open();
        public SqliteConnection Open()
        {
            ObjectDisposedException.ThrowIf(bDisposed, this);
            var dbConnection = new SqliteConnection(strConnection);
            try
            {
                dbConnection.Open();
                using var cmdSettings = new SqliteCommand("PRAGMA temp_store=MEMORY; PRAGMA journal_mode=MEMORY; PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON;", dbConnection);
                cmdSettings.ExecuteNonQuery();
                SecurityAudit.RegisterConnection(dbConnection, objKeys, strConnection, objCheckpoint);
                return dbConnection;
            }
            catch { dbConnection.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (bDisposed) return;
            bDisposed = true;
            try { dbKeeper?.Dispose(); }
            finally { objKeys.Dispose(); }
        }
    }

    private sealed class UiDevelopmentKeys : IDatabaseKeyStore, IDisposable
    {
        private readonly byte[] arrKey = SensitiveMemory.AllocateBytes(32);
        private bool bDisposed;
        internal UiDevelopmentKeys()
        {
            try { RandomNumberGenerator.Fill(arrKey); }
            catch { SensitiveMemory.Clear(arrKey); throw; }
        }
        public void EnsureKey(string strIdentity, bool bAllowCreate) => ObjectDisposedException.ThrowIf(bDisposed, this);
        public byte[] ReadKey(string strIdentity)
        {
            ObjectDisposedException.ThrowIf(bDisposed, this);
            var arrCopy = SensitiveMemory.AllocateBytes(32);
            arrKey.CopyTo(arrCopy, 0); return arrCopy;
        }
        public void Dispose() { bDisposed = true; SensitiveMemory.Clear(arrKey); }
    }

    // A process-local fixture, deliberately not a deployable trusted checkpoint store.
    private sealed class UiDevelopmentCheckpoint : ITrustedAuditCheckpointLease
    {
        private readonly object objGate = new();
        private AuditCheckpoint objState = new(0, null, null);
        public AuditCheckpoint Read() { lock (objGate) return objState; }
        public void CompareExchange(long lngExpectedVersion, AuditCheckpoint objReplacement)
        {
            lock (objGate)
            {
                if (objState.lngVersion != lngExpectedVersion || objReplacement.lngVersion != checked(lngExpectedVersion + 1))
                    throw new InvalidOperationException("The UI fixture checkpoint changed.");
                objState = objReplacement;
            }
        }
        public void Dispose() { }
    }
}
#endif
