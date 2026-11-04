using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

internal sealed record AuthorizedIdentity(long lngUserId, UserRole enRole);

internal static class AccessControl
{
    internal static AuthorizedIdentity Identity(SqliteConnection dbConnection, SqliteTransaction txOperation, Session objSession)
    {
        AuthorizedIdentity? objIdentity = null;
        try
        {
            objIdentity = objSession.UseTokenHash<AuthorizedIdentity?>(arrTokenHash =>
            {
                using var cmdIdentity = new SqliteCommand("SELECT u.id,u.role FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=$hash AND s.status='Active' AND u.is_enabled=1", dbConnection, txOperation);
                cmdIdentity.Parameters.AddWithValue("$hash", arrTokenHash);
                using var rdrIdentity = cmdIdentity.ExecuteReader();
                if (rdrIdentity.Read() && Enum.TryParse<UserRole>(rdrIdentity.GetString(1), out var enRole) && Enum.IsDefined(enRole))
                    return new AuthorizedIdentity(rdrIdentity.GetInt64(0), enRole);
                return null;
            });
        }
        catch (ObjectDisposedException) { }
        if (objIdentity is not null) return objIdentity;
        Deny(dbConnection, txOperation, null, AuditEntity.Sessions, 0);
        throw new UnauthorizedAccessException("Sign in to continue.");
    }
    internal static bool CanAccess(SqliteConnection dbConnection, SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity, RecordKind enKind, long lngRecordId, bool bWrite)
    {
        if (bWrite && objIdentity.enRole == UserRole.Viewer) return false;
        var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
        var strAccessTable = enKind == RecordKind.Contact ? "contact_access" : "organization_access";
        var strColumn = enKind == RecordKind.Contact ? "contact_id" : "organization_id";
        using var cmdAccess = new SqliteCommand(
            $"SELECT count(*) FROM {strTable} r WHERE r.id=$id AND ($admin=1 OR EXISTS(SELECT 1 FROM {strAccessTable} a WHERE a.{strColumn}=r.id AND a.user_id=$user AND a.status='Active' AND ($write=0 OR a.can_write=1)))",
            dbConnection, txOperation);
        cmdAccess.Parameters.AddWithValue("$id", lngRecordId);
        cmdAccess.Parameters.AddWithValue("$admin", objIdentity.enRole == UserRole.Administrator);
        cmdAccess.Parameters.AddWithValue("$user", objIdentity.lngUserId);
        cmdAccess.Parameters.AddWithValue("$write", bWrite);
        return (long)cmdAccess.ExecuteScalar()! == 1;
    }

    internal static void Require(SqliteConnection dbConnection, SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity, RecordKind enKind, long lngRecordId, bool bWrite)
    {
        if (!CanAccess(dbConnection, txOperation, objIdentity, enKind, lngRecordId, bWrite))
            Deny(dbConnection, txOperation, objIdentity.lngUserId,
                enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations, lngRecordId);
    }

    internal static void Administrator(SqliteConnection dbConnection, SqliteTransaction txOperation, AuthorizedIdentity objIdentity)
    {
        if (objIdentity.enRole != UserRole.Administrator)
            Deny(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Users, 0);
    }

    // Call ONLY before a mutation. Commit the denial event before throwing; audit failure still blocks access.
    [DoesNotReturn]
    internal static void Deny(SqliteConnection dbConnection, SqliteTransaction txOperation, long? lngUserId, AuditEntity enEntity, long lngEntityId)
    {
        AuthenticationAudit.Append(dbConnection, txOperation, lngUserId, enEntity, lngEntityId, AuditAction.PermissionDenied);
        txOperation.Commit();
        throw new UnauthorizedAccessException("You do not have permission to perform this operation.");
    }
}
