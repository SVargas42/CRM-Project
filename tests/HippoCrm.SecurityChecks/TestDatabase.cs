using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

// TEST ONLY. Real production services execute against the OS's SQLite in memory.
// This substitutes the unavailable ADO provider, NOT authorization/authentication logic.
// It cannot validate SQLCipher encryption, native provider ABI, or production connection setup.
public sealed class Database : IDisposable
{
    private readonly FixtureAuditKeyStore objKeys = new();
    private readonly string strUri = "file:HippoCrmSecurity" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
    private readonly SqliteConnection dbKeeper;
    public Database(string strUnusedPath) => throw new InvalidOperationException("Security adapter tests must inject an in-memory database.");
    public Database(bool bLegacy = false, bool bVersionSix = false)
    {
        dbKeeper = Open();
        using var txSchema = dbKeeper.BeginTransaction(false);
        if (bLegacy)
        {
            using var stmSchema = typeof(Database).Assembly.GetManifestResourceStream("HippoCrm.Core.LegacySchema.sql")!;
            using var rdrSchema = new StreamReader(stmSchema);
            using var cmdSchema = Command(dbKeeper, rdrSchema.ReadToEnd()); cmdSchema.Transaction = txSchema; cmdSchema.ExecuteNonQuery();
        }
        else if (bVersionSix)
        {
            using var stmSchema = typeof(Database).Assembly.GetManifestResourceStream("HippoCrm.Core.VersionSixSchema.sql")!;
            using var rdrSchema = new StreamReader(stmSchema);
            using var cmdSchema = Command(dbKeeper, rdrSchema.ReadToEnd()); cmdSchema.Transaction = txSchema; cmdSchema.ExecuteNonQuery();
            RetentionSchema.Initialize(dbKeeper, txSchema);
            SecurityAudit.Initialize(dbKeeper, txSchema, false);
        }
        else SchemaManager.Initialize(dbKeeper, txSchema);
        txSchema.Commit();
    }
    internal SqliteConnection Open()
    {
        var dbConnection = new SqliteConnection(strUri); dbConnection.Open();
        SecurityAudit.RegisterConnection(dbConnection, objKeys, strUri);
        using var cmdForeignKeys = Command(dbConnection, "PRAGMA foreign_keys=ON"); cmdForeignKeys.ExecuteNonQuery();
        return dbConnection;
    }
    public bool ReadDarkTheme()
    {
        using var dbConnection = Open();
        using var cmdTheme = Command(dbConnection, "SELECT value FROM workspace_settings WHERE name='theme'");
        return string.Equals(cmdTheme.ExecuteScalar() as string, "Dark", StringComparison.Ordinal);
    }
    public void SaveDarkTheme(bool bDark)
    {
        using var dbConnection = Open(); using var txTheme = dbConnection.BeginTransaction(false);
        using var cmdTheme = Command(dbConnection,
            "INSERT INTO workspace_settings(name,value) VALUES('theme',$theme) ON CONFLICT(name) DO UPDATE SET value=excluded.value",
            ("$theme", bDark ? "Dark" : "Light"));
        cmdTheme.Transaction = txTheme; cmdTheme.ExecuteNonQuery(); txTheme.Commit();
    }
    internal void Upgrade()
    {
        using var dbConnection = Open(); using var txSchema = dbConnection.BeginTransaction(false);
        SchemaManager.Initialize(dbConnection, txSchema); txSchema.Commit();
    }
    internal static SqliteCommand Command(SqliteConnection dbConnection, string strSql, params (string strName, object? objValue)[] arrParameters)
    {
        var cmdStatement = dbConnection.CreateCommand(); cmdStatement.CommandText = strSql;
        foreach (var (strName, objValue) in arrParameters) cmdStatement.Parameters.AddWithValue(strName, objValue ?? DBNull.Value);
        return cmdStatement;
    }
    internal static string Now => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    public void Dispose()
    {
        try { dbKeeper.Dispose(); }
        finally { objKeys.Dispose(); }
    }
    // Fictional, in-memory test keys only. Production continues to use OS-protected
    // credentials through the real SQLCipher connection factory.
    private sealed class FixtureAuditKeyStore : IDatabaseKeyStore, IDisposable
    {
        private readonly byte[] arrMasterKey = CreateKey();
        private static byte[] CreateKey()
        {
            var arrKey = SensitiveMemory.AllocateBytes(32);
            System.Security.Cryptography.RandomNumberGenerator.Fill(arrKey); return arrKey;
        }
        public void EnsureKey(string strDatabasePath, bool bAllowCreate) { }
        public byte[] ReadKey(string strDatabasePath)
        {
            var arrKey = SensitiveMemory.AllocateBytes(32); arrMasterKey.CopyTo(arrKey, 0); return arrKey;
        }
        public void Dispose() => SensitiveMemory.Clear(arrMasterKey);
    }
}
