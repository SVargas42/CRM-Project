using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
namespace HippoCrm.Core;

public sealed class AuthService
{
    internal const int iIterations = PasswordBuffer.iIterations;
    private readonly Func<SqliteConnection> fnOpenConnection;
    public AuthService(Database objDatabase) : this(objDatabase.Open) { }
    public AuthService(Func<SqliteConnection> fnOpenConnection) =>
        this.fnOpenConnection = fnOpenConnection ?? throw new ArgumentNullException(nameof(fnOpenConnection));

    private SqliteConnection OpenConnection()
    {
        var dbConnection = fnOpenConnection();
        if (dbConnection.State == ConnectionState.Open) return dbConnection;
        dbConnection.Dispose();
        throw new InvalidOperationException("An open encrypted workspace connection is required.");
    }
    public bool NeedsSetup
    {
        get
        {
            using var dbConnection = OpenConnection();
            using var cmdCount = new SqliteCommand("SELECT count(*) FROM users", dbConnection);
            return Convert.ToInt64(cmdCount.ExecuteScalar(), CultureInfo.InvariantCulture) == 0;
        }
    }
    public Session CreateAdministrator(string strUsername, SecureString secPassword)
    {
        using var dbConnection = OpenConnection();
        using var txAuthentication = dbConnection.BeginTransaction(deferred: false);
        using var cmdCount = new SqliteCommand("SELECT count(*) FROM users", dbConnection, txAuthentication);
        if (Convert.ToInt64(cmdCount.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
            Reject(dbConnection, txAuthentication, null, AuditAction.SetupRejected, "Administrator setup is already complete.");
        if (!ValidNewCredentials(strUsername, secPassword))
            Reject(dbConnection, txAuthentication, null, AuditAction.SetupRejected, "Use a username with 3–64 characters and a password with 12–256 characters.");
        var lngUserId = InsertUser(dbConnection, txAuthentication, strUsername.Trim(), secPassword, UserRole.Administrator);
        AuthenticationAudit.Append(dbConnection, txAuthentication, lngUserId, AuditEntity.Users, lngUserId, AuditAction.AdministratorCreated);
        return IssueSession(dbConnection, txAuthentication, lngUserId, strUsername.Trim());
    }

    // Existing callers execute this compatibility API on their database worker.
    public Session SignIn(string strUsername, SecureString secPassword) =>
        SignInAsync(strUsername, secPassword).GetAwaiter().GetResult();
    public async Task<Session> SignInAsync(string strUsername, SecureString secPassword, CancellationToken ctCancellation = default)
    {
        var swVerification = Stopwatch.StartNew();
        SecureString? secSnapshot = null;
        try
        {
            ctCancellation.ThrowIfCancellationRequested();
            if (secPassword is not null && secPassword.Length <= PasswordBuffer.iMaximumPasswordLength)
            { secSnapshot = secPassword.Copy(); secSnapshot.MakeReadOnly(); }
            return await Task.Run(() => SignInCore(strUsername, secSnapshot), ctCancellation).ConfigureAwait(false);
        }
        finally
        {
            try { secSnapshot?.Dispose(); }
            finally { await EnforceMinimumDelayAsync(swVerification).ConfigureAwait(false); }
        }
    }
    private Session SignInCore(string strUsername, SecureString? secPassword)
    {
        var arrSalt = SensitiveMemory.AllocateBytes(64);
        var arrExpected = SensitiveMemory.AllocateBytes(32);
        var arrActual = SensitiveMemory.AllocateBytes(32);
        try
        {
            using var dbConnection = OpenConnection();
            using var txAuthentication = dbConnection.BeginTransaction(deferred: false);
            if (secPassword is null || string.IsNullOrWhiteSpace(strUsername) || strUsername.Trim().Length > 64)
                Reject(dbConnection, txAuthentication, null, AuditAction.SignInFailed, "Invalid username or password.");
            long? lngUserId = null;
            var strStoredUsername = "";
            var iAttempts = 0; var iSaltLength = 32; var bEnabled = false; var bValidMetadata = false;
            DateTimeOffset? dtLockedUntil = null;
            using (var cmdUser = new SqliteCommand("SELECT id,username,salt,password_hash,iterations,failed_attempts,locked_until,is_enabled FROM users WHERE username=$username", dbConnection, txAuthentication))
            {
                cmdUser.Parameters.AddWithValue("$username", strUsername.Trim());
                using var rdrUser = cmdUser.ExecuteReader();
                if (rdrUser.Read())
                {
                    lngUserId = rdrUser.GetInt64(0); strStoredUsername = rdrUser.GetString(1);
                    iAttempts = rdrUser.GetInt32(5); bEnabled = rdrUser.GetInt64(7) == 1;
                    bValidMetadata = ReadCredential(rdrUser, 2, 3, 4, arrSalt, arrExpected, out iSaltLength) && iAttempts is >= 0 and <= 5;
                    if (!rdrUser.IsDBNull(6))
                    {
                        if (DateTimeOffset.TryParse(rdrUser.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dtParsedLock))
                            dtLockedUntil = dtParsedLock;
                        else bValidMetadata = false;
                    }
                }
            }
            if (!bValidMetadata)
            {
                SensitiveMemory.Clear(arrSalt); SensitiveMemory.Clear(arrExpected);
                iSaltLength = 32; RandomNumberGenerator.Fill(arrSalt.AsSpan(0, iSaltLength));
            }
            PasswordBuffer.Derive(secPassword, arrSalt.AsSpan(0, iSaltLength), arrActual);
            var bMatches = CryptographicOperations.FixedTimeEquals(arrActual, arrExpected);
            if (lngUserId is null || !bValidMetadata || !bEnabled)
                Reject(dbConnection, txAuthentication, lngUserId, AuditAction.SignInFailed, "Invalid username or password.");
            var dtNow = DateTimeOffset.UtcNow;
            if (dtLockedUntil > dtNow)
                Reject(dbConnection, txAuthentication, lngUserId, AuditAction.SignInLocked, "Invalid username or password.");
            if (!bMatches)
            {
                iAttempts = dtLockedUntil.HasValue ? 1 : Math.Min(5, iAttempts + 1);
                using var cmdFailure = new SqliteCommand("UPDATE users SET failed_attempts=$attempts,locked_until=$locked WHERE id=$id", dbConnection, txAuthentication);
                cmdFailure.Parameters.AddWithValue("$attempts", iAttempts);
                cmdFailure.Parameters.AddWithValue("$locked", iAttempts >= 5 ? dtNow.AddMinutes(15).ToString("O", CultureInfo.InvariantCulture) : (object)DBNull.Value);
                cmdFailure.Parameters.AddWithValue("$id", lngUserId.Value); cmdFailure.ExecuteNonQuery();
                Reject(dbConnection, txAuthentication, lngUserId, AuditAction.SignInFailed, "Invalid username or password.");
            }
            using (var cmdReset = new SqliteCommand("UPDATE users SET failed_attempts=0,locked_until=NULL WHERE id=$id", dbConnection, txAuthentication))
            { cmdReset.Parameters.AddWithValue("$id", lngUserId.Value); cmdReset.ExecuteNonQuery(); }
            return IssueSession(dbConnection, txAuthentication, lngUserId.Value, strStoredUsername);
        }
        finally { SensitiveMemory.Clear(arrActual); SensitiveMemory.Clear(arrExpected); SensitiveMemory.Clear(arrSalt); }
    }

    public void SignOut(Session objSession)
    {
        ArgumentNullException.ThrowIfNull(objSession);
        try
        {
            using var dbConnection = OpenConnection();
            using var txOperation = dbConnection.BeginTransaction(deferred: false);
            var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
            objSession.UseTokenHash(arrHash => { RetentionStatements.RevokeSession(dbConnection, txOperation, arrHash); return true; });
            AuthenticationAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Sessions, 0, AuditAction.SignedOut);
            txOperation.Commit();
        }
        finally { objSession.Dispose(); }
    }

    public void ChangePassword(Session objSession, SecureString secCurrent, SecureString secNew) =>
        ChangePasswordAsync(objSession, secCurrent, secNew).GetAwaiter().GetResult();
    public async Task ChangePasswordAsync(Session objSession, SecureString secCurrent, SecureString secNew)
    {
        var swVerification = Stopwatch.StartNew();
        try
        {
            ArgumentNullException.ThrowIfNull(objSession);
            ArgumentNullException.ThrowIfNull(secCurrent); ArgumentNullException.ThrowIfNull(secNew);
            using var secCurrentSnapshot = secCurrent.Copy(); using var secNewSnapshot = secNew.Copy();
            secCurrentSnapshot.MakeReadOnly(); secNewSnapshot.MakeReadOnly();
            await Task.Run(() => ChangePasswordCore(objSession, secCurrentSnapshot, secNewSnapshot)).ConfigureAwait(false);
        }
        finally { await EnforceMinimumDelayAsync(swVerification).ConfigureAwait(false); }
    }
    private void ChangePasswordCore(Session objSession, SecureString secCurrent, SecureString secNew)
    {
        var arrSalt = SensitiveMemory.AllocateBytes(64);
        var arrExpected = SensitiveMemory.AllocateBytes(32); var arrActual = SensitiveMemory.AllocateBytes(32);
        try
        {
            ValidateNewPassword(secNew);
            using var dbConnection = OpenConnection(); using var txOperation = dbConnection.BeginTransaction(deferred: false);
            var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
            var iSaltLength = 0; var bValidMetadata = false;
            using (var cmdUser = new SqliteCommand("SELECT salt,password_hash,iterations FROM users WHERE id=$id AND is_enabled=1", dbConnection, txOperation))
            {
                cmdUser.Parameters.AddWithValue("$id", objIdentity.lngUserId); using var rdrUser = cmdUser.ExecuteReader();
                if (rdrUser.Read()) bValidMetadata = ReadCredential(rdrUser, 0, 1, 2, arrSalt, arrExpected, out iSaltLength);
            }
            if (!bValidMetadata) throw new CryptographicException("The account could not be verified.");
            PasswordBuffer.Derive(secCurrent, arrSalt.AsSpan(0, iSaltLength), arrActual);
            if (!CryptographicOperations.FixedTimeEquals(arrActual, arrExpected))
                Reject(dbConnection, txOperation, objIdentity.lngUserId, AuditAction.PasswordChangeRejected, "Current password is incorrect.");
            ReplacePassword(dbConnection, txOperation, objIdentity.lngUserId, secNew);
            AuthenticationAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, objIdentity.lngUserId, AuditAction.PasswordChanged);
            txOperation.Commit();
        }
        finally { SensitiveMemory.Clear(arrActual); SensitiveMemory.Clear(arrExpected); SensitiveMemory.Clear(arrSalt); }
    }

    internal static void ReplacePassword(SqliteConnection dbConnection, SqliteTransaction txOperation, long lngUserId, SecureString secPassword)
    {
        ValidateNewPassword(secPassword);
        var arrSalt = SensitiveMemory.AllocateBytes(32); var arrHash = SensitiveMemory.AllocateBytes(32);
        try
        {
            RandomNumberGenerator.Fill(arrSalt); PasswordBuffer.Derive(secPassword, arrSalt, arrHash);
            using var cmdPassword = new SqliteCommand("UPDATE users SET salt=$salt,password_hash=$hash,iterations=$iterations,failed_attempts=0,locked_until=NULL WHERE id=$id RETURNING id", dbConnection, txOperation);
            cmdPassword.Parameters.AddWithValue("$salt", arrSalt); cmdPassword.Parameters.AddWithValue("$hash", arrHash);
            cmdPassword.Parameters.AddWithValue("$iterations", iIterations); cmdPassword.Parameters.AddWithValue("$id", lngUserId);
            if (cmdPassword.ExecuteScalar() is null) throw new ArgumentException("Select an existing user.");
            RetentionStatements.RevokeUserSessions(dbConnection, txOperation, lngUserId);
        }
        finally { SensitiveMemory.Clear(arrHash); SensitiveMemory.Clear(arrSalt); }
    }
    internal static long InsertUser(SqliteConnection dbConnection, SqliteTransaction txOperation, string strUsername, SecureString secPassword, UserRole enRole)
    {
        if (!ValidNewCredentials(strUsername, secPassword) || !Enum.IsDefined(enRole)) throw new ArgumentException("Enter valid account details.");
        var arrSalt = SensitiveMemory.AllocateBytes(32); var arrHash = SensitiveMemory.AllocateBytes(32);
        try
        {
            RandomNumberGenerator.Fill(arrSalt); PasswordBuffer.Derive(secPassword, arrSalt, arrHash);
            using var cmdInsert = new SqliteCommand("INSERT INTO users(username,salt,password_hash,iterations,created_at,role,is_enabled) VALUES($name,$salt,$hash,$iterations,$now,$role,1) RETURNING id", dbConnection, txOperation);
            cmdInsert.Parameters.AddWithValue("$name", strUsername.Trim()); cmdInsert.Parameters.AddWithValue("$salt", arrSalt);
            cmdInsert.Parameters.AddWithValue("$hash", arrHash); cmdInsert.Parameters.AddWithValue("$iterations", iIterations);
            cmdInsert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)); cmdInsert.Parameters.AddWithValue("$role", enRole.ToString());
            return Convert.ToInt64(cmdInsert.ExecuteScalar() ?? throw new InvalidOperationException("The account could not be created."), CultureInfo.InvariantCulture);
        }
        finally { SensitiveMemory.Clear(arrHash); SensitiveMemory.Clear(arrSalt); }
    }
    internal static bool ValidNewCredentials(string strUsername, SecureString secPassword) =>
        !string.IsNullOrWhiteSpace(strUsername) && strUsername.Trim().Length is >= 3 and <= 64
        && !strUsername.Any(char.IsControl) && secPassword is not null && secPassword.Length is >= 12 and <= 256;
    private static void ValidateNewPassword(SecureString secPassword)
    {
        ArgumentNullException.ThrowIfNull(secPassword);
        if (secPassword.Length is < 12 or > 256) throw new ArgumentException("Use a password with 12–256 characters.");
    }
    private static bool ReadCredential(SqliteDataReader rdrUser, int iSaltOrdinal, int iHashOrdinal, int iIterationsOrdinal,
        byte[] arrSalt, byte[] arrExpected, out int iSaltLength)
    {
        iSaltLength = 0;
        if (rdrUser.IsDBNull(iSaltOrdinal) || rdrUser.IsDBNull(iHashOrdinal) || rdrUser.IsDBNull(iIterationsOrdinal)
            || rdrUser.GetInt32(iIterationsOrdinal) != iIterations) return false;
        var lngSaltLength = rdrUser.GetBytes(iSaltOrdinal, 0, null, 0, 0);
        var lngHashLength = rdrUser.GetBytes(iHashOrdinal, 0, null, 0, 0);
        if (lngSaltLength is < 16 or > 64 || lngHashLength != 32) return false;
        iSaltLength = checked((int)lngSaltLength);
        return rdrUser.GetBytes(iSaltOrdinal, 0, arrSalt, 0, iSaltLength) == iSaltLength
            && rdrUser.GetBytes(iHashOrdinal, 0, arrExpected, 0, arrExpected.Length) == arrExpected.Length;
    }
    private static Session IssueSession(SqliteConnection dbConnection, SqliteTransaction txOperation, long lngUserId, string strUsername)
    {
        var objSession = new Session(lngUserId, strUsername); var bCommitted = false;
        try
        {
            objSession.UseTokenHash(arrHash =>
            {
                using var cmdSession = new SqliteCommand("INSERT INTO sessions(token_hash,user_id,created_at,status) VALUES($hash,$id,$now,'Active')", dbConnection, txOperation);
                cmdSession.Parameters.AddWithValue("$hash", arrHash); cmdSession.Parameters.AddWithValue("$id", lngUserId);
                cmdSession.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                cmdSession.ExecuteNonQuery(); return true;
            });
            AuthenticationAudit.Append(dbConnection, txOperation, lngUserId, AuditEntity.Authentication, 0, AuditAction.SignInSucceeded);
            txOperation.Commit(); bCommitted = true; return objSession;
        }
        finally { if (!bCommitted) objSession.Dispose(); }
    }
    private static async Task EnforceMinimumDelayAsync(Stopwatch swVerification)
    {
        // Delay timers can round down. Recheck the monotonic clock so even a
        // pre-cancelled request cannot finish a fraction before the minimum.
        while (swVerification.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            var iRemainingMilliseconds = Math.Max(1, (int)Math.Ceiling(500 - swVerification.Elapsed.TotalMilliseconds));
            await Task.Delay(iRemainingMilliseconds, CancellationToken.None).ConfigureAwait(false);
        }
    }
    [DoesNotReturn]
    private static void Reject(SqliteConnection dbConnection, SqliteTransaction txOperation, long? lngUserId, AuditAction enAction, string strMessage)
    {
        AuthenticationAudit.Append(dbConnection, txOperation, lngUserId, AuditEntity.Authentication, 0, enAction);
        txOperation.Commit(); throw new ArgumentException(strMessage);
    }
}
