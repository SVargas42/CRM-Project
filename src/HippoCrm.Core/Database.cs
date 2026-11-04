using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed class Database
{


    private readonly IDatabaseKeyStore objKeyStore;
    public string Path { get; }

    public Database(string strPath, IDatabaseKeyStore? objKeyStore = null)
    {
        // Even non-UI callers must pass before directories/credentials are touched.
        SqlCipherConnectionFactory.VerifyApprovedRuntime();
        Path = SqlCipherConnectionFactory.ValidateLocalPath(strPath);
        // UNC paths could initiate SMB traffic before SQLite opens.
        if (Path.StartsWith(@"\\", StringComparison.Ordinal) ||
            (OperatingSystem.IsWindows() && new DriveInfo(System.IO.Path.GetPathRoot(Path)!).DriveType == DriveType.Network))
            throw new InvalidOperationException("Choose a local data directory.");
        this.objKeyStore = objKeyStore ?? (OperatingSystem.IsMacOS()
            ? new MacKeychainKeyStore()
            : new WindowsCredentialKeyStore());
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var strIdentity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.ToUpperInvariant())));
        using var mtxInitialization = new Mutex(false, @"Local\HippoCrm.Database." + strIdentity);
        var bAcquired = false;
        try
        {
            try { bAcquired = mtxInitialization.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { bAcquired = true; }
            if (!bAcquired) throw new InvalidOperationException("The workspace is busy. Try again shortly.");
            RejectPlaintextDatabase();
            this.objKeyStore.EnsureKey(Path, !File.Exists(Path));
            using var dbConnection = OpenConnection(!File.Exists(Path));
            using var txSchema = dbConnection.BeginTransaction();
            SchemaManager.Initialize(dbConnection, txSchema);
            txSchema.Commit();
        }
        finally { if (bAcquired) mtxInitialization.ReleaseMutex(); }
    }

    internal SqliteConnection Open() => OpenConnection(false);

    public bool ReadDarkTheme()
    {
        using var dbConnection = Open();
        using var cmdTheme = Command(dbConnection, "SELECT value FROM workspace_settings WHERE name='theme'");
        return string.Equals(cmdTheme.ExecuteScalar() as string, "Dark", StringComparison.Ordinal);
    }

    public void SaveDarkTheme(bool bDark)
    {
        using var dbConnection = Open();
        using var txTheme = dbConnection.BeginTransaction();
        using var cmdTheme = Command(dbConnection,
            "INSERT INTO workspace_settings(name,value) VALUES('theme',$theme) ON CONFLICT(name) DO UPDATE SET value=excluded.value",
            ("$theme", bDark ? "Dark" : "Light"));
        cmdTheme.Transaction = txTheme; cmdTheme.ExecuteNonQuery(); txTheme.Commit();
    }

    private SqliteConnection OpenConnection(bool bCreate) => SqlCipherConnectionFactory.Open(Path, objKeyStore, bCreate);

    private void RejectPlaintextDatabase()
    {
        if (!File.Exists(Path)) return;
        using var stmDatabase = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> spnHeader = stackalloc byte[16];
        if (stmDatabase.Read(spnHeader) == 16 && spnHeader.SequenceEqual("SQLite format 3\0"u8))
            throw new InvalidOperationException("This workspace requires a separately approved encryption migration. Its files have not been changed.");
    }

    internal static SqliteCommand Command(SqliteConnection dbConnection, string strSql,
        params (string strName, object? objValue)[] arrParameters)
    {
        var cmdStatement = dbConnection.CreateCommand();
        cmdStatement.CommandText = strSql;
        foreach (var (strName, objValue) in arrParameters)
            cmdStatement.Parameters.AddWithValue(strName, objValue ?? DBNull.Value);
        return cmdStatement;
    }

    internal static string Now => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
