using System.Globalization;
using System.Security.Cryptography;
using HippoCrm.Core;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

// TEST ONLY: no filesystem workspace or production startup override is possible.
// This checks EF/policy behavior, not FIPS validation or database-file encryption.
public sealed class Database : IDisposable
{
    private readonly string strConnection;
    private readonly SqliteConnection dbKeeper;
    private readonly MemoryKeys objKeys = new();
    public Database(string strPath, IDatabaseKeyStore objStore) => throw new InvalidOperationException("Disk workspaces are prohibited in the policy test adapter.");
    public Database(bool bVersionSix = false)
    {
        SQLitePCL.Batteries_V2.Init();
        strConnection = new SqliteConnectionStringBuilder { DataSource = "HippoPolicy" + Guid.NewGuid().ToString("N"), Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared, Pooling = false }.ToString();
        dbKeeper = Open();
        using var txSchema = dbKeeper.BeginTransaction(false);
        if (bVersionSix)
        {
            using var stmSchema = typeof(Database).Assembly.GetManifestResourceStream("HippoCrm.Core.VersionSixSchema.sql")!;
            using var rdrSchema = new StreamReader(stmSchema);
            using var cmdSchema = new SqliteCommand(rdrSchema.ReadToEnd(), dbKeeper, txSchema); cmdSchema.ExecuteNonQuery();
            RetentionSchema.Initialize(dbKeeper, txSchema); SecurityAudit.Initialize(dbKeeper, txSchema, false);
        }
        else SchemaManager.Initialize(dbKeeper, txSchema);
        txSchema.Commit();
    }
    internal SqliteConnection Open()
    {
        var dbConnection = new SqliteConnection(strConnection); dbConnection.Open();
        try
        {
            SecurityAudit.RegisterConnection(dbConnection, objKeys, strConnection);
            using var cmdSettings = new SqliteCommand("PRAGMA foreign_keys=ON; PRAGMA temp_store=MEMORY;", dbConnection); cmdSettings.ExecuteNonQuery();
            return dbConnection;
        }
        catch { dbConnection.Dispose(); throw; }
    }
    internal void Upgrade()
    {
        using var dbConnection = Open(); using var txSchema = dbConnection.BeginTransaction(false);
        SchemaManager.Initialize(dbConnection, txSchema); txSchema.Commit();
    }
    internal static SqliteCommand Command(SqliteConnection dbConnection, string strSql, params (string strName, object? objValue)[] arrParameters)
    {
        var cmdStatement = new SqliteCommand(strSql, dbConnection);
        foreach (var (strName, objValue) in arrParameters) cmdStatement.Parameters.AddWithValue(strName, objValue ?? DBNull.Value);
        return cmdStatement;
    }
    internal static string Now => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    public void Dispose() { try { dbKeeper.Dispose(); } finally { objKeys.Dispose(); } }
    private sealed class MemoryKeys : IDatabaseKeyStore, IDisposable
    {
        private readonly byte[] arrKey = SensitiveMemory.AllocateBytes(32);
        internal MemoryKeys() => RandomNumberGenerator.Fill(arrKey);
        public void EnsureKey(string strIdentity, bool bCreate) { }
        public byte[] ReadKey(string strIdentity)
        { var arrCopy = SensitiveMemory.AllocateBytes(32); arrKey.CopyTo(arrCopy, 0); return arrCopy; }
        public void Dispose() => SensitiveMemory.Clear(arrKey);
    }
}
