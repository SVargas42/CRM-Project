using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

internal enum AuditEntity { Authentication, Users, Contacts, Organizations, Interactions, Employment, Contracts, Sessions, Schema }
internal enum AuditAction
{
    AdministratorCreated, SetupRejected, UserCreated, SignInSucceeded, SignInFailed, SignInLocked,
    SignedOut, RoleChanged, AccessGranted, AccessRevoked, Created, Updated, Ended, AccessVerified, PermissionDenied, SchemaUpgraded,
    PasswordChanged, PasswordReset, PasswordChangeRejected, AccountEnabled, AccountDisabled
}

internal static class SecurityAudit
{
    // Typed event names and numeric identifiers prevent PII/passwords/keys from entering event payloads.
    internal static void Append(SqliteConnection dbConnection, SqliteTransaction txOperation, long? lngActorId,
        AuditEntity enEntity, long lngEntityId, AuditAction enAction, long? lngSubjectId = null)
    {
        using var cmdAudit = Database.Command(dbConnection,
            "INSERT INTO audit_log(user_id,subject_user_id,entity,entity_id,action,created_at) VALUES($actor,$subject,$entity,$id,$action,$now)",
            ("$actor", lngActorId), ("$subject", lngSubjectId), ("$entity", enEntity.ToString()),
            ("$id", lngEntityId), ("$action", enAction.ToString()), ("$now", Database.Now));
        cmdAudit.Transaction = txOperation;
        cmdAudit.ExecuteNonQuery();
    }
}
