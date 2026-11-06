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
            var objPolicy = CuiPolicy.ReadRecord(dbConnection, txOperation, enKind, lngEntityId);
            AccessControl.RequirePolicy(dbConnection, txOperation, objIdentity, objPolicy);
            arrSanitized = ImageSanitizer.Sanitize(arrUntrusted);
            // Sanitize has already wiped the untrusted bytes before any BLOB commit.
            using (var cmdRetire = new SqliteCommand("UPDATE profile_images SET status='Inactive' WHERE entity=$entity AND entity_id=$id AND status='Active'", dbConnection, txOperation))
            {
                cmdRetire.Parameters.AddWithValue("$entity", enKind.ToString());
                cmdRetire.Parameters.AddWithValue("$id", lngEntityId);
                cmdRetire.ExecuteNonQuery();
            }
            using (var cmdSave = new SqliteCommand("INSERT INTO profile_images(entity,entity_id,png,status,created_at,user_id,cui_category,distribution_statement,controlling_office,determination_date,distribution_notice) VALUES($entity,$id,$png,'Active',$now,$user,$category,$distribution,$office,$date,$notice)", dbConnection, txOperation))
            {
                CuiPolicy.Bind(cmdSave, objPolicy);
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

    public ProfileImage? ReadContactPicture(long lngContactId) => Read(RecordKind.Contact, lngContactId);
    public ProfileImage? ReadOrganizationLogo(long lngOrganizationId) => Read(RecordKind.Organization, lngOrganizationId);

    private ProfileImage? Read(RecordKind enKind, long lngEntityId)
    {
        using var dbConnection = objDatabase.Open(); using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        AccessControl.Require(dbConnection, txOperation, objIdentity, enKind, lngEntityId, false);
        var objParentPolicy = CuiPolicy.ReadRecord(dbConnection, txOperation, enKind, lngEntityId);
        ProfileImage? objImage = null; var bTransferred = false;
        try
        {
            using (var cmdImage = new SqliteCommand("SELECT id,cui_category,distribution_statement,controlling_office,determination_date,distribution_notice,png,created_at,user_id FROM profile_images WHERE entity=$entity AND entity_id=$id AND status='Active'", dbConnection, txOperation))
            {
                cmdImage.Parameters.AddWithValue("$entity", enKind.ToString()); cmdImage.Parameters.AddWithValue("$id", lngEntityId);
                using var rdrImage = cmdImage.ExecuteReader();
                if (rdrImage.Read())
                {
                    var objPolicy = CuiPolicy.Read(rdrImage, 1);
                    if (objPolicy != objParentPolicy) throw new System.Security.Cryptography.CryptographicException("Image protection could not be verified.");
                    // Check before materializing any decrypted BLOB into a caller-owned buffer.
                    AccessControl.RequirePolicy(dbConnection, txOperation, objIdentity, objPolicy);
                    var lngLength = rdrImage.GetBytes(6, 0, null, 0, 0);
                    if (lngLength is < 1 or > 20971520) throw new System.Security.Cryptography.CryptographicException("Image protection could not be verified.");
                    objImage = CuiPolicy.Apply(new ProfileImage { Id = rdrImage.GetInt64(0), enKind = enKind, lngEntityId = lngEntityId,
                        strCreatedAt = rdrImage.GetString(7), lngUserId = rdrImage.GetInt64(8) }, objPolicy);
                    objImage.arrPng = SensitiveMemory.AllocateBytes(checked((int)lngLength));
                    if (rdrImage.GetBytes(6, 0, objImage.arrPng, 0, objImage.arrPng.Length) != lngLength)
                        throw new IOException("The picture could not be read.");
                }
            }
            objSession.UseTokenHash(arrHash => true);
            SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Images, lngEntityId, AuditAction.AccessVerified);
            SecurityAudit.Commit(dbConnection, txOperation); bTransferred = true; return objImage;
        }
        finally { if (!bTransferred) objImage?.Dispose(); }
    }
}
