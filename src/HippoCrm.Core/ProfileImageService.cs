using System.Globalization;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

namespace HippoCrm.Core;

// The only supported ingestion path writes sanitized pixels into SQLCipher;
// it never creates a plaintext image file. Existing image versions are retained.
public sealed class ProfileImageService(Database objDatabase, Session objSession)
{
    public void SaveContactPicture(long lngContactId, byte[] arrUntrusted) =>
        Save(RecordKind.Contact, lngContactId, arrUntrusted);

    public void SaveOrganizationLogo(long lngOrganizationId, byte[] arrUntrusted) =>
        Save(RecordKind.Organization, lngOrganizationId, arrUntrusted);

    private void Save(RecordKind enKind, long lngEntityId, byte[] arrUntrusted)
    {
        ArgumentNullException.ThrowIfNull(arrUntrusted);
        byte[]? arrSanitized = null;
        var gchUntrusted = default(GCHandle);
        try
        {
            gchUntrusted = GCHandle.Alloc(arrUntrusted, GCHandleType.Pinned);
            using var dbConnection = objDatabase.Open();
            using var txOperation = dbConnection.BeginTransaction(deferred: false);
            var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
            AccessControl.Require(dbConnection, txOperation, objIdentity, enKind, lngEntityId, true);
            arrSanitized = ImageSanitizer.Sanitize(arrUntrusted);
            // Sanitize has already wiped the untrusted bytes before any BLOB commit.
            using (var cmdRetire = new SqliteCommand("UPDATE profile_images SET status='Inactive' WHERE entity=$entity AND entity_id=$id AND status='Active'", dbConnection, txOperation))
            {
                cmdRetire.Parameters.AddWithValue("$entity", enKind.ToString());
                cmdRetire.Parameters.AddWithValue("$id", lngEntityId);
                cmdRetire.ExecuteNonQuery();
            }
            using (var cmdSave = new SqliteCommand("INSERT INTO profile_images(entity,entity_id,png,status,created_at,user_id) VALUES($entity,$id,$png,'Active',$now,$user)", dbConnection, txOperation))
            {
                cmdSave.Parameters.AddWithValue("$entity", enKind.ToString());
                cmdSave.Parameters.AddWithValue("$id", lngEntityId);
                cmdSave.Parameters.AddWithValue("$png", arrSanitized);
                cmdSave.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                cmdSave.Parameters.AddWithValue("$user", objIdentity.lngUserId);
                cmdSave.ExecuteNonQuery();
            }
            SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Images, lngEntityId, AuditAction.Updated);
            SecurityAudit.Commit(dbConnection, txOperation);
        }
        finally
        {
            try { SensitiveMemory.Clear(arrSanitized); }
            finally
            {
                try { SensitiveMemory.Clear(arrUntrusted); }
                finally { if (gchUntrusted.IsAllocated) gchUntrusted.Free(); }
            }
        }
    }
}
