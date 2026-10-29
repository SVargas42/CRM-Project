using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed class AuthService(Database objDatabase)
{
    internal const int iIterations = 600_000;
    public bool NeedsSetup
    {
        get
        {
            using var dbConnection = objDatabase.Open();
            using var cmdCount = Database.Command(dbConnection, "SELECT count(*) FROM users");
            return (long)cmdCount.ExecuteScalar()! == 0;
        }
    }

    public Session CreateAdministrator(string strUsername, SecureString secPassword)
    {
        using var dbConnection = objDatabase.Open();
        using var txAuthentication = dbConnection.BeginTransaction(false);
        using var cmdCount = Database.Command(dbConnection, "SELECT count(*) FROM users");
        cmdCount.Transaction = txAuthentication;
        if ((long)cmdCount.ExecuteScalar()! != 0)
            Reject(dbConnection, txAuthentication, null, AuditAction.SetupRejected, "Administrator setup is already complete.");
        if (!ValidNewCredentials(strUsername, secPassword))
            Reject(dbConnection, txAuthentication, null, AuditAction.SetupRejected, "Use a username with 3–64 characters and a password with 12–256 characters.");
        var lngUserId = InsertUser(dbConnection, txAuthentication, strUsername.Trim(), secPassword, UserRole.Administrator);
        SecurityAudit.Append(dbConnection, txAuthentication, lngUserId, AuditEntity.Users, lngUserId, AuditAction.AdministratorCreated);
        return IssueSession(dbConnection, txAuthentication, lngUserId, strUsername.Trim());
    }

    public Session SignIn(string strUsername, SecureString secPassword)
    {
        using var dbConnection = objDatabase.Open();
        // Immediate transactions serialize password-failure increments across app instances.
        using var txAuthentication = dbConnection.BeginTransaction(false);
        if (secPassword is null || secPassword.Length > 256 || string.IsNullOrWhiteSpace(strUsername) || strUsername.Trim().Length > 64)
            Reject(dbConnection, txAuthentication, null, AuditAction.SignInFailed, "Invalid username or password.");
        byte[]? arrActual = null;
        byte[] arrSalt = new byte[32];
        byte[] arrExpected = new byte[32];
        long? lngUserId = null;
        var strName = "";
        var iAttempts = 0;
        DateTimeOffset? dtLockedUntil = null;
        var bEnabled = false;
        var bValidMetadata = false;
        try
        {
            using (var cmdUser = Database.Command(dbConnection,
                "SELECT id,username,salt,password_hash,iterations,failed_attempts,locked_until,is_enabled FROM users WHERE username=$name",
                ("$name", strUsername.Trim())))
            {
                cmdUser.Transaction = txAuthentication;
                using var rdrUser = cmdUser.ExecuteReader();
                if (rdrUser.Read())
                {
                    lngUserId = rdrUser.GetInt64(0); strName = rdrUser.GetString(1);
                    arrSalt = (byte[])rdrUser[2]; arrExpected = (byte[])rdrUser[3];
                    iAttempts = rdrUser.GetInt32(5);
                    var bValidLock = rdrUser.IsDBNull(6) || DateTimeOffset.TryParse(rdrUser.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dtParsedLock);
                    if (!rdrUser.IsDBNull(6) && bValidLock)
                        dtLockedUntil = DateTimeOffset.Parse(rdrUser.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    bEnabled = rdrUser.GetInt64(7) == 1;
                    bValidMetadata = arrSalt.Length == 32 && arrExpected.Length == 32 && rdrUser.GetInt32(4) == iIterations && iAttempts is >= 0 and <= 5 && bValidLock;
                }
            }
            // Unknown/disabled/locked accounts still incur the same configured password derivation.
            arrActual = PasswordBuffer.Derive(secPassword, bValidMetadata ? arrSalt : new byte[32], iIterations);
            var bMatches = CryptographicOperations.FixedTimeEquals(arrActual, arrExpected);
            if (lngUserId is null || !bValidMetadata || !bEnabled)
                Reject(dbConnection, txAuthentication, lngUserId, AuditAction.SignInFailed, "Invalid username or password.");
            if (dtLockedUntil > DateTimeOffset.UtcNow)
                Reject(dbConnection, txAuthentication, lngUserId, AuditAction.SignInLocked, "Invalid username or password.");
            if (!bMatches)
            {
                iAttempts = dtLockedUntil.HasValue ? 1 : Math.Min(5, iAttempts + 1);
                using var cmdFailure = Database.Command(dbConnection,
                    "UPDATE users SET failed_attempts=$attempts,locked_until=$locked WHERE id=$id",
                    ("$attempts", iAttempts), ("$locked", iAttempts >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15).ToString("O", CultureInfo.InvariantCulture) : null),
                    ("$id", lngUserId.Value));
                cmdFailure.Transaction = txAuthentication; cmdFailure.ExecuteNonQuery();
                Reject(dbConnection, txAuthentication, lngUserId, AuditAction.SignInFailed, "Invalid username or password.");
            }
            using var cmdReset = Database.Command(dbConnection, "UPDATE users SET failed_attempts=0,locked_until=NULL WHERE id=$id", ("$id", lngUserId.Value));
            cmdReset.Transaction = txAuthentication; cmdReset.ExecuteNonQuery();
            return IssueSession(dbConnection, txAuthentication, lngUserId.Value, strName);
        }
        finally
        {
            if (arrActual is not null) CryptographicOperations.ZeroMemory(arrActual);
            CryptographicOperations.ZeroMemory(arrExpected); CryptographicOperations.ZeroMemory(arrSalt);
        }
    }

    public void SignOut(Session objSession)
    {
        try
        {
            using var dbConnection = objDatabase.Open();
            using var txAuthentication = dbConnection.BeginTransaction(false);
            var objIdentity = AccessControl.Identity(dbConnection, txAuthentication, objSession);
            var arrTokenHash = objSession.TokenHash();
            try
            {
                using var cmdRevoke = Database.Command(dbConnection, "DELETE FROM sessions WHERE token_hash=$hash", ("$hash", arrTokenHash));
                cmdRevoke.Transaction = txAuthentication; cmdRevoke.ExecuteNonQuery();
                SecurityAudit.Append(dbConnection, txAuthentication, objIdentity.lngUserId, AuditEntity.Sessions, 0, AuditAction.SignedOut);
                txAuthentication.Commit();
            }
            finally { CryptographicOperations.ZeroMemory(arrTokenHash); }
        }
        finally { objSession.Dispose(); }
    }

    internal static bool ValidNewCredentials(string strUsername, SecureString secPassword) =>
        !string.IsNullOrWhiteSpace(strUsername) && strUsername.Trim().Length is >= 3 and <= 64 &&
        secPassword is not null && secPassword.Length is >= 12 and <= 256;

    internal static long InsertUser(SqliteConnection dbConnection, SqliteTransaction txOperation,
        string strUsername, SecureString secPassword, UserRole enRole)
    {
        if (!ValidNewCredentials(strUsername, secPassword) || !Enum.IsDefined(enRole))
            throw new ArgumentException("Use a valid role, a username with 3–64 characters and a password with 12–256 characters.");
        var arrSalt = RandomNumberGenerator.GetBytes(32);
        byte[]? arrHash = null;
        try
        {
            arrHash = PasswordBuffer.Derive(secPassword, arrSalt, iIterations);
            using var cmdInsert = Database.Command(dbConnection,
                "INSERT INTO users(username,salt,password_hash,iterations,created_at,role) VALUES($name,$salt,$hash,$iterations,$now,$role) RETURNING id",
                ("$name", strUsername.Trim()), ("$salt", arrSalt), ("$hash", arrHash), ("$iterations", iIterations),
                ("$now", Database.Now), ("$role", enRole.ToString()));
            cmdInsert.Transaction = txOperation;
            return (long)cmdInsert.ExecuteScalar()!;
        }
        finally
        {
            if (arrHash is not null) CryptographicOperations.ZeroMemory(arrHash);
            CryptographicOperations.ZeroMemory(arrSalt);
        }
    }

    private static Session IssueSession(SqliteConnection dbConnection, SqliteTransaction txAuthentication, long lngUserId, string strUsername)
    {
        var objSession = new Session(lngUserId, strUsername);
        var arrTokenHash = objSession.TokenHash();
        try
        {
            using var cmdSession = Database.Command(dbConnection, "INSERT INTO sessions(token_hash,user_id,created_at) VALUES($hash,$id,$now)",
                ("$hash", arrTokenHash), ("$id", lngUserId), ("$now", Database.Now));
            cmdSession.Transaction = txAuthentication; cmdSession.ExecuteNonQuery();
            SecurityAudit.Append(dbConnection, txAuthentication, lngUserId, AuditEntity.Authentication, 0, AuditAction.SignInSucceeded);
            txAuthentication.Commit();
            return objSession;
        }
        catch { objSession.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(arrTokenHash); }
    }

    [DoesNotReturn]
    private static void Reject(SqliteConnection dbConnection, SqliteTransaction txAuthentication, long? lngUserId, AuditAction enAction, string strMessage)
    {
        SecurityAudit.Append(dbConnection, txAuthentication, lngUserId, AuditEntity.Authentication, 0, enAction);
        txAuthentication.Commit();
        throw new ArgumentException(strMessage);
    }
}
