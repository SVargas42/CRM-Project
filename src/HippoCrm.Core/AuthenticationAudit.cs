using Microsoft.Data.Sqlite;
namespace HippoCrm.Core;

internal static class AuthenticationAudit
{
    internal static void Append(SqliteConnection dbConnection, SqliteTransaction txOperation, long? lngActorId,
        AuditEntity enEntity, long lngEntityId, AuditAction enAction, long? lngSubjectId = null) =>
        SecurityAudit.Append(dbConnection, txOperation, lngActorId, enEntity, lngEntityId, enAction, lngSubjectId);
}
