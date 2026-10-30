using System.Security;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed class UserAdministrationService(Database objDatabase, Session objSession)
{
    public sealed record UserAccount(long lngId,string strUsername,UserRole enRole,bool bEnabled);
    public IReadOnlyList<UserAccount> Users()
    {
        using var dbConnection=objDatabase.Open(); using var txOperation=dbConnection.BeginTransaction(false);
        var objIdentity=AccessControl.Identity(dbConnection,txOperation,objSession); AccessControl.Administrator(dbConnection,txOperation,objIdentity);
        using var cmdUsers=Database.Command(dbConnection,"SELECT id,username,role,is_enabled FROM users ORDER BY username COLLATE NOCASE"); cmdUsers.Transaction=txOperation;
        using var rdrUsers=cmdUsers.ExecuteReader(); var lstUsers=new List<UserAccount>();
        while(rdrUsers.Read()) lstUsers.Add(new(rdrUsers.GetInt64(0),rdrUsers.GetString(1),Enum.Parse<UserRole>(rdrUsers.GetString(2)),rdrUsers.GetInt64(3)!=0)); rdrUsers.Close();
        SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,AuditEntity.Users,0,AuditAction.AccessVerified); txOperation.Commit(); return lstUsers;
    }
    public void ResetPassword(long lngUserId,SecureString secNew)
    {
        using var dbConnection=objDatabase.Open(); using var txOperation=dbConnection.BeginTransaction(false);
        var objIdentity=AccessControl.Identity(dbConnection,txOperation,objSession); AccessControl.Administrator(dbConnection,txOperation,objIdentity);
        if(lngUserId==objIdentity.lngUserId) throw new ArgumentException("Use Change password for your own account.");
        AuthService.ReplacePassword(dbConnection,txOperation,lngUserId,secNew);
        SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,AuditEntity.Users,lngUserId,AuditAction.PasswordReset,lngUserId); txOperation.Commit();
    }
    public void SetEnabled(long lngUserId,bool bEnabled)
    {
        using var dbConnection=objDatabase.Open(); using var txOperation=dbConnection.BeginTransaction(false);
        var objIdentity=AccessControl.Identity(dbConnection,txOperation,objSession); AccessControl.Administrator(dbConnection,txOperation,objIdentity);
        if(lngUserId==objIdentity.lngUserId && !bEnabled) throw new ArgumentException("You cannot disable your own account.");
        using var cmdUser=Database.Command(dbConnection,"UPDATE users SET is_enabled=$enabled WHERE id=$id RETURNING id",("$enabled",bEnabled),("$id",lngUserId)); cmdUser.Transaction=txOperation;
        if(cmdUser.ExecuteScalar() is null) throw new ArgumentException("Select an existing user.");
        if(!bEnabled) { using var cmdRevoke=Database.Command(dbConnection,"DELETE FROM sessions WHERE user_id=$id",("$id",lngUserId)); cmdRevoke.Transaction=txOperation; cmdRevoke.ExecuteNonQuery(); }
        SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,AuditEntity.Users,lngUserId,bEnabled ? AuditAction.AccountEnabled : AuditAction.AccountDisabled,lngUserId); txOperation.Commit();
    }
    public long CreateUser(string strUsername, SecureString secPassword, UserRole enRole)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        var lngUserId = AuthService.InsertUser(dbConnection, txOperation, strUsername, secPassword, enRole);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, lngUserId, AuditAction.UserCreated, lngUserId);
        txOperation.Commit();
        return lngUserId;
    }

    public void SetRole(long lngUserId, UserRole enRole)
    {
        if (!Enum.IsDefined(enRole)) throw new ArgumentException("Select a valid role.");
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        using var cmdLastAdmin = Database.Command(dbConnection,
            "SELECT count(*) FROM users WHERE id=$id AND role='Administrator' AND is_enabled=1 AND (SELECT count(*) FROM users WHERE role='Administrator' AND is_enabled=1)=1",
            ("$id", lngUserId));
        cmdLastAdmin.Transaction = txOperation;
        if (enRole != UserRole.Administrator && (long)cmdLastAdmin.ExecuteScalar()! != 0)
            throw new ArgumentException("Keep at least one enabled administrator.");
        using var cmdRole = Database.Command(dbConnection, "UPDATE users SET role=$role WHERE id=$id RETURNING id", ("$role", enRole.ToString()), ("$id", lngUserId));
        cmdRole.Transaction = txOperation;
        if (cmdRole.ExecuteScalar() is null) throw new ArgumentException("Select an existing user.");
        // Re-authentication is required after a role change, including demotion.
        using var cmdRevoke = Database.Command(dbConnection, "DELETE FROM sessions WHERE user_id=$id", ("$id", lngUserId));
        cmdRevoke.Transaction = txOperation; cmdRevoke.ExecuteNonQuery();
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, lngUserId, AuditAction.RoleChanged, lngUserId);
        txOperation.Commit();
    }

    public void SetRecordAccess(long lngUserId, RecordKind enKind, long lngRecordId, bool bAllowRead, bool bAllowWrite)
    {
        if (!Enum.IsDefined(enKind) || (!bAllowRead && bAllowWrite)) throw new ArgumentException("Select valid record permissions.");
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        using var cmdUser = Database.Command(dbConnection, "SELECT count(*) FROM users WHERE id=$id", ("$id", lngUserId));
        cmdUser.Transaction = txOperation;
        if ((long)cmdUser.ExecuteScalar()! != 1 || !AccessControl.CanAccess(dbConnection, txOperation, objIdentity, enKind, lngRecordId, false))
            throw new ArgumentException("Select an existing user and record.");
        var strTable = enKind == RecordKind.Contact ? "contact_access" : "organization_access";
        var strColumn = enKind == RecordKind.Contact ? "contact_id" : "organization_id";
        var strSql = bAllowRead
            ? $"INSERT INTO {strTable}(user_id,{strColumn},can_write) VALUES($user,$record,$write) ON CONFLICT(user_id,{strColumn}) DO UPDATE SET can_write=$write"
            : $"DELETE FROM {strTable} WHERE user_id=$user AND {strColumn}=$record";
        using var cmdGrant = Database.Command(dbConnection, strSql, ("$user", lngUserId), ("$record", lngRecordId), ("$write", bAllowWrite));
        cmdGrant.Transaction = txOperation; cmdGrant.ExecuteNonQuery();
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId,
            enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations, lngRecordId,
            bAllowRead ? AuditAction.AccessGranted : AuditAction.AccessRevoked, lngUserId);
        txOperation.Commit();
    }
}
