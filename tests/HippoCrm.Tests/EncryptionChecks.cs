using System.Security.Cryptography;
using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class EncryptionChecks
{
    public static void Run(Database objDatabase, string strPath, EphemeralKeyStore objKeyStore)
    {
        Assert(objKeyStore.arrLastRead is not null && objKeyStore.arrLastRead.All(bytValue => bytValue == 0), "Connection clears the managed key buffer");
        using (var dbConnection = objDatabase.Open())
        {
            using var cmdInsert = dbConnection.CreateCommand();
            cmdInsert.CommandText = "CREATE TABLE encryption_probe(body TEXT); INSERT INTO encryption_probe VALUES('FICTIONAL-ENCRYPTION-PROBE-7C9713');";
            cmdInsert.ExecuteNonQuery();
        }
        var arrFile = File.ReadAllBytes(strPath);
        Assert(!arrFile.AsSpan().StartsWith("SQLite format 3\0"u8), "Database has no plaintext SQLite header");
        Assert(arrFile.AsSpan().IndexOf("FICTIONAL-ENCRYPTION-PROBE-7C9713"u8) < 0, "Probe value is absent from database bytes");
        var arrOriginalHash = SHA256.HashData(arrFile);
        using (var dbUnkeyed = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = strPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            dbUnkeyed.Open();
            using var cmdRead = dbUnkeyed.CreateCommand(); cmdRead.CommandText = "SELECT count(*) FROM sqlite_master";
            Reject<SqliteException>(() => cmdRead.ExecuteScalar(), "Unkeyed schema access fails");
        }
        using var objWrongKeyStore = new EphemeralKeyStore();
        Reject<SqliteException>(() => new Database(strPath, objWrongKeyStore), "Wrong key cannot open the database");
        Assert(objWrongKeyStore.arrLastRead is not null && objWrongKeyStore.arrLastRead.All(bytValue => bytValue == 0), "Failed opens clear the key buffer");
        Assert(arrOriginalHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(strPath))), "Rejected opens do not alter database bytes");
        Reject<CryptographicException>(() => new Database(strPath, new MissingKeyStore()), "Missing keys fail closed");
        if (OperatingSystem.IsWindows())
        {
            using var stmLocked = new FileStream(strPath, FileMode.Open, FileAccess.Read, FileShare.None);
            Reject<IOException>(() => new Database(strPath, objKeyStore), "Exclusively locked files fail safely");
        }
        var strPlaintextPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(strPath)!, "plaintext-probe.db");
        File.WriteAllBytes(strPlaintextPath, "SQLite format 3\0FICTIONAL-PLAINTEXT-PROBE"u8.ToArray());
        var arrPlaintextHash = SHA256.HashData(File.ReadAllBytes(strPlaintextPath));
        Reject<InvalidOperationException>(() => new Database(strPlaintextPath, objKeyStore), "Plaintext databases require an explicit migration");
        Assert(arrPlaintextHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(strPlaintextPath))), "Plaintext rejection preserves original bytes");
        using var dbReopened = new Database(strPath, objKeyStore).Open();
        using var cmdReopened = dbReopened.CreateCommand(); cmdReopened.CommandText = "SELECT body FROM encryption_probe";
        Assert((string?)cmdReopened.ExecuteScalar() == "FICTIONAL-ENCRYPTION-PROBE-7C9713", "The correct key reopens existing encrypted records");
    }

    private static void Assert(bool bCondition, string strDescription)
    {
        if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
        Console.WriteLine("PASS " + strDescription);
    }
    private static void Reject<T>(Action actOperation, string strDescription) where T : Exception
    {
        try { actOperation(); }
        catch (T) { Console.WriteLine("PASS " + strDescription); return; }
        throw new InvalidOperationException("FAIL " + strDescription);
    }
    private sealed class MissingKeyStore : IDatabaseKeyStore
    {
        public void EnsureKey(string strDatabasePath, bool bAllowCreate) => throw new CryptographicException("Test key unavailable.");
        public byte[] ReadKey(string strDatabasePath) => throw new CryptographicException("Test key unavailable.");
    }
}

