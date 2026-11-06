using System.Security;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed class UserAdministrationService(Database objDatabase, Session objSession)
{
    public sealed record UserAccount(long lngId,string strUsername,UserRole enRole,bool bEnabled,bool bCuiAuthorized);
    public IReadOnlyList<UserAccount> Users()
    {
        using var dbConnection=objDatabase.Open(); using var txOperation=dbConnection.BeginTransaction(false);
        var objIdentity=AccessControl.Identity(dbConnection,txOperation,objSession); AccessControl.Administrator(dbConnection,txOperation,objIdentity);
        using var cmdUsers=Database.Command(dbConnection,"SELECT id,username,role,is_enabled,cui_authorized FROM users ORDER BY username COLLATE NOCASE"); cmdUsers.Transaction=txOperation;
        using var rdrUsers=cmdUsers.ExecuteReader(); var lstUsers=new List<UserAccount>();
        while(rdrUsers.Read()) lstUsers.Add(new(rdrUsers.GetInt64(0),rdrUsers.GetString(1),Enum.Parse<UserRole>(rdrUsers.GetString(2)),rdrUsers.GetInt64(3)!=0,rdrUsers.GetInt64(4)==1)); rdrUsers.Close();
        SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,AuditEntity.Users,0,AuditAction.AccessVerified); SecurityAudit.Commit(dbConnection, txOperation); return lstUsers;
    }
    public void ResetPassword(long lngUserId,SecureString secNew)
    {
        using var dbConnection=objDatabase.Open(); using var txOperation=dbConnection.BeginTransaction(false);
        var objIdentity=AccessControl.Identity(dbConnection,txOperation,objSession); AccessControl.Administrator(dbConnection,txOperation,objIdentity);
        if(lngUserId==objIdentity.lngUserId) throw new ArgumentException("Use Change password for your own account.");
        AuthService.ReplacePassword(dbConnection,txOperation,lngUserId,secNew);
        SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,AuditEntity.Users,lngUserId,AuditAction.PasswordReset,lngUserId); SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
    }

    // Administrator authority manages explicit CUI approvals, not implicit record
    // access. Even administrators remain denied CUI until this audited action.
    public void SetCuiAuthorization(long lngUserId, bool bAuthorized)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        using var cmdCui = new SqliteCommand("UPDATE users SET cui_authorized=$authorized WHERE id=$id RETURNING id", dbConnection, txOperation);
        cmdCui.Parameters.AddWithValue("$authorized", bAuthorized);
        cmdCui.Parameters.AddWithValue("$id", lngUserId);
        if (cmdCui.ExecuteScalar() is null) throw new ArgumentException("Select an existing user.");
        using (var cmdPolicy = new SqliteCommand(bAuthorized
            ? "INSERT INTO cui_policy_access(user_id,cui_category,distribution_statement,approval_reference,status) VALUES($id,'CUI','','Basic CUI approval','Active') ON CONFLICT(user_id,cui_category,distribution_statement) DO UPDATE SET status='Active',approval_reference=excluded.approval_reference"
            : "UPDATE cui_policy_access SET status='Inactive' WHERE user_id=$id AND status='Active'", dbConnection, txOperation))
        { cmdPolicy.Parameters.AddWithValue("$id", lngUserId); cmdPolicy.ExecuteNonQuery(); }
        if (!bAuthorized) RetentionStatements.RevokeUserSessions(dbConnection, txOperation, lngUserId);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, lngUserId,
            bAuthorized ? AuditAction.CuiAuthorized : AuditAction.CuiRevoked, lngUserId);
        SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
    }

    // Approval is an offline controlling-authority decision, not inferred from role,
    // citizenship, employer, a different distribution letter, or Basic CUI approval.
    public void SetCuiPolicyAccess(long lngUserId, string strCategory, string strDistributionStatement,
        bool bAuthorized, string strApprovalReference)
    {
        CuiPolicy.ValidatePair(strCategory, strDistributionStatement);
        if (string.IsNullOrWhiteSpace(strApprovalReference) || strApprovalReference.Length > 256 || strApprovalReference.Any(char.IsControl))
            throw new ArgumentException("Enter the local authority approval reference.");
        using var dbConnection = objDatabase.Open(); using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        using (var cmdUser = new SqliteCommand("SELECT cui_recipient_kind FROM users WHERE id=$id AND cui_authorized=1 AND is_enabled=1", dbConnection, txOperation))
        {
            cmdUser.Parameters.AddWithValue("$id", lngUserId);
            var strRecipient = cmdUser.ExecuteScalar() as string;
            if (strRecipient is null) throw new ArgumentException("Select an enabled user approved to handle CUI.");
            if (bAuthorized && strCategory == CuiPolicy.strCtiMarking &&
                (!Enum.TryParse<CuiRecipientKind>(strRecipient, out var enRecipient) || !CuiPolicy.AllowedDistributions(enRecipient).Contains(strDistributionStatement, StringComparison.Ordinal)))
                throw new ArgumentException("This recipient is not eligible for the selected distribution statement.");
        }
        using var cmdPolicy = new SqliteCommand("INSERT INTO cui_policy_access(user_id,cui_category,distribution_statement,approval_reference,status) VALUES($id,$category,$distribution,$reference,$status) ON CONFLICT(user_id,cui_category,distribution_statement) DO UPDATE SET approval_reference=excluded.approval_reference,status=excluded.status", dbConnection, txOperation);
        cmdPolicy.Parameters.AddWithValue("$id", lngUserId); cmdPolicy.Parameters.AddWithValue("$category", strCategory);
        cmdPolicy.Parameters.AddWithValue("$distribution", strDistributionStatement); cmdPolicy.Parameters.AddWithValue("$reference", strApprovalReference);
        cmdPolicy.Parameters.AddWithValue("$status", bAuthorized ? "Active" : "Inactive"); cmdPolicy.ExecuteNonQuery();
        if (!bAuthorized) RetentionStatements.RevokeUserSessions(dbConnection, txOperation, lngUserId);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, lngUserId,
            bAuthorized ? AuditAction.CuiPolicyGranted : AuditAction.CuiPolicyRevoked, lngUserId);
        SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
    }

    public void SetCuiRecipientKind(long lngUserId, CuiRecipientKind enRecipientKind, string strEvidenceReference)
    {
        if (!Enum.IsDefined(enRecipientKind) || string.IsNullOrWhiteSpace(strEvidenceReference)
            || strEvidenceReference.Length > 256 || strEvidenceReference.Any(char.IsControl))
            throw new ArgumentException("Enter a verified recipient type and its local authority reference.");
        using var dbConnection = objDatabase.Open(); using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        using var cmdRecipient = new SqliteCommand("UPDATE users SET cui_recipient_kind=$kind,cui_recipient_evidence=$reference WHERE id=$id RETURNING id", dbConnection, txOperation);
        cmdRecipient.Parameters.AddWithValue("$kind", enRecipientKind.ToString()); cmdRecipient.Parameters.AddWithValue("$reference", strEvidenceReference);
        cmdRecipient.Parameters.AddWithValue("$id", lngUserId);
        if (cmdRecipient.ExecuteScalar() is null) throw new ArgumentException("Select an existing user.");
        using var cmdRevoke = new SqliteCommand("UPDATE cui_policy_access SET status='Inactive' WHERE user_id=$id AND cui_category='CUI//SP-CTI' AND status='Active'", dbConnection, txOperation);
        cmdRevoke.Parameters.AddWithValue("$id", lngUserId); cmdRevoke.ExecuteNonQuery();
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, lngUserId, AuditAction.CuiRecipientChanged, lngUserId);
        SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
    }

    public void SetRecordCuiDesignation(RecordKind enKind, long lngRecordId, int iExpectedRevision, CuiDesignation objDesignation)
    {
        if (!Enum.IsDefined(enKind)) throw new ArgumentException("Select a valid record type.");
        CuiPolicy.Validate(objDesignation);
        using var dbConnection = objDatabase.Open(); using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        AccessControl.Require(dbConnection, txOperation, objIdentity, enKind, lngRecordId, true);
        AccessControl.RequirePolicy(dbConnection, txOperation, objIdentity, objDesignation);
        var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
        // SQLite propagates the designation to every retained image in this same transaction.
        using var cmdPolicy = new SqliteCommand($"UPDATE {strTable} SET cui_category=$category,distribution_statement=$distribution,controlling_office=$office,determination_date=$date,distribution_notice=$notice,revision=revision+1,updated_at=$now WHERE id=$id AND revision=$revision RETURNING id", dbConnection, txOperation);
        CuiPolicy.Bind(cmdPolicy, objDesignation); cmdPolicy.Parameters.AddWithValue("$id", lngRecordId);
        cmdPolicy.Parameters.AddWithValue("$revision", iExpectedRevision); cmdPolicy.Parameters.AddWithValue("$now", Database.Now);
        if (cmdPolicy.ExecuteScalar() is null) throw new ArgumentException("This record changed. Reload it before changing its designation.");
        CuiPolicy.GrantDesignatorAccess(dbConnection, txOperation, objIdentity, enKind, lngRecordId, objDesignation);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId,
            enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations, lngRecordId, AuditAction.CuiDesignationChanged);
        SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
    }
    public void SetEnabled(long lngUserId,bool bEnabled)
    {
        using var dbConnection=objDatabase.Open(); using var txOperation=dbConnection.BeginTransaction(false);
        var objIdentity=AccessControl.Identity(dbConnection,txOperation,objSession); AccessControl.Administrator(dbConnection,txOperation,objIdentity);
        if(lngUserId==objIdentity.lngUserId && !bEnabled) throw new ArgumentException("You cannot disable your own account.");
        using var cmdUser=Database.Command(dbConnection,"UPDATE users SET is_enabled=$enabled WHERE id=$id RETURNING id",("$enabled",bEnabled),("$id",lngUserId)); cmdUser.Transaction=txOperation;
        if(cmdUser.ExecuteScalar() is null) throw new ArgumentException("Select an existing user.");
        if(!bEnabled) RetentionStatements.RevokeUserSessions(dbConnection,txOperation,lngUserId);
        SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,AuditEntity.Users,lngUserId,bEnabled ? AuditAction.AccountEnabled : AuditAction.AccountDisabled,lngUserId); SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
    }
    public long CreateUser(string strUsername, SecureString secPassword, UserRole enRole)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Administrator(dbConnection, txOperation, objIdentity);
        var lngUserId = AuthService.InsertUser(dbConnection, txOperation, strUsername, secPassword, enRole);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, lngUserId, AuditAction.UserCreated, lngUserId);
        SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
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
        RetentionStatements.RevokeUserSessions(dbConnection, txOperation, lngUserId);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, lngUserId, AuditAction.RoleChanged, lngUserId);
        SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
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
        RetentionStatements.SetRecordAccess(dbConnection, txOperation, enKind, lngUserId, lngRecordId, bAllowRead, bAllowWrite);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId,
            enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations, lngRecordId,
            bAllowRead ? AuditAction.AccessGranted : AuditAction.AccessRevoked, lngUserId);
        SecurityAudit.Commit(dbConnection, txOperation); CuiAuthorizationEpoch.Advance();
    }
}
