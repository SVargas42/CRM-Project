using System.Globalization;
using Microsoft.Data.Sqlite;
namespace HippoCrm.Core;

internal static class RetentionStatements
{
    internal static void RevokeSession(SqliteConnection dbConnection, SqliteTransaction txOperation, byte[] arrTokenHash)
    {
        using var cmdRevoke = new SqliteCommand("UPDATE sessions SET status='Inactive' WHERE token_hash=$hash AND status='Active'", dbConnection, txOperation);
        cmdRevoke.Parameters.AddWithValue("$hash", arrTokenHash); cmdRevoke.ExecuteNonQuery();
    }
    internal static void RevokeUserSessions(SqliteConnection dbConnection, SqliteTransaction txOperation, long lngUserId)
    {
        using var cmdRevoke = new SqliteCommand("UPDATE sessions SET status='Inactive' WHERE user_id=$user AND status='Active'", dbConnection, txOperation);
        cmdRevoke.Parameters.AddWithValue("$user", lngUserId); cmdRevoke.ExecuteNonQuery();
    }
    internal static void SetRecordAccess(SqliteConnection dbConnection, SqliteTransaction txOperation, RecordKind enKind,
        long lngUserId, long lngRecordId, bool bAllowRead, bool bAllowWrite)
    {
        if (!Enum.IsDefined(enKind) || (!bAllowRead && bAllowWrite)) throw new ArgumentException("Select valid record permissions.");
        var strTable = enKind == RecordKind.Contact ? "contact_access" : "organization_access";
        var strColumn = enKind == RecordKind.Contact ? "contact_id" : "organization_id";
        var strSql = bAllowRead
            ? $"INSERT INTO {strTable}(user_id,{strColumn},can_write,status) VALUES($user,$record,$write,'Active') ON CONFLICT(user_id,{strColumn}) DO UPDATE SET can_write=excluded.can_write,status='Active'"
            : $"UPDATE {strTable} SET status='Inactive',can_write=0 WHERE user_id=$user AND {strColumn}=$record";
        using var cmdGrant = new SqliteCommand(strSql, dbConnection, txOperation);
        cmdGrant.Parameters.AddWithValue("$user", lngUserId); cmdGrant.Parameters.AddWithValue("$record", lngRecordId);
        cmdGrant.Parameters.AddWithValue("$write", bAllowWrite); cmdGrant.ExecuteNonQuery();
    }
    internal static void ReplaceContactOrganizations(SqliteConnection dbConnection, SqliteTransaction txOperation,
        long lngContactId, IReadOnlyCollection<long> lstOrganizationIds)
    {
        ArgumentNullException.ThrowIfNull(lstOrganizationIds);
        if (lstOrganizationIds.Any(lngId => lngId <= 0)) throw new ArgumentException("Select valid organizations.");
        using (var cmdDeactivate = new SqliteCommand("UPDATE contact_organizations SET status='Inactive' WHERE contact_id=$contact AND status='Active'", dbConnection, txOperation))
        {
            cmdDeactivate.Parameters.AddWithValue("$contact", lngContactId); cmdDeactivate.ExecuteNonQuery();
        }
        foreach (var lngOrganizationId in lstOrganizationIds.Distinct())
        {
            using var cmdAssign = new SqliteCommand("INSERT INTO contact_organizations(contact_id,organization_id,status) VALUES($contact,$organization,'Active') ON CONFLICT(contact_id,organization_id) DO UPDATE SET status='Active'", dbConnection, txOperation);
            cmdAssign.Parameters.AddWithValue("$contact", lngContactId); cmdAssign.Parameters.AddWithValue("$organization", lngOrganizationId); cmdAssign.ExecuteNonQuery();
        }
    }
    internal static void SetBusinessRecordStatus(SqliteConnection dbConnection, SqliteTransaction txOperation,
        RecordKind enKind, long lngRecordId, int iExpectedRevision, RecordStatus enStatus)
    {
        if (!Enum.IsDefined(enKind) || !Enum.IsDefined(enStatus)) throw new ArgumentException("Select a valid record status.");
        var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
        using var cmdStatus = new SqliteCommand($"UPDATE {strTable} SET status=$status,updated_at=$now,revision=revision+1 WHERE id=$id AND revision=$revision RETURNING id", dbConnection, txOperation);
        cmdStatus.Parameters.AddWithValue("$status", enStatus.ToString());
        cmdStatus.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmdStatus.Parameters.AddWithValue("$id", lngRecordId); cmdStatus.Parameters.AddWithValue("$revision", iExpectedRevision);
        if (cmdStatus.ExecuteScalar() is null) throw new ArgumentException("This record changed in another editor. Reload it before saving.");
    }
}
