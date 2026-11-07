using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

// This service owns input bytes and clears them even on denial/cancellation.
public sealed class ProfileImageService(Database objDatabase, Session objSession)
{
    public long SaveContactPicture(long lngContactId, byte[] arrUntrusted, CancellationToken ctCancellation = default) =>
        Save(RecordKind.Contact, lngContactId, arrUntrusted, ctCancellation);
    public long SaveOrganizationLogo(long lngOrganizationId, byte[] arrUntrusted, CancellationToken ctCancellation = default) =>
        Save(RecordKind.Organization, lngOrganizationId, arrUntrusted, ctCancellation);

    public byte[]? Read(RecordKind enKind, long lngEntityId, CancellationToken ctCancellation = default)
    {
        ValidateKind(enKind, lngEntityId);
        byte[]? arrImage = null;
        try
        {
            ctCancellation.ThrowIfCancellationRequested();
            using var dbConnection = objDatabase.Open();
            using var txRead = dbConnection.BeginTransaction(false);
            var objIdentity = AccessControl.Identity(dbConnection, txRead, objSession);
            AccessControl.Require(dbConnection, txRead, objIdentity, enKind, lngEntityId, false);
            using var cmdRead = Database.Command(dbConnection, "SELECT id,cui_category,distribution_statement,controlling_office,determination_date,distribution_notice,png FROM profile_images WHERE entity=$entity AND entity_id=$id AND status='Active'", ("$entity",enKind.ToString()),("$id",lngEntityId));
            cmdRead.Transaction = txRead;
            var objParentPolicy = CuiPolicy.ReadRecord(dbConnection, txRead, enKind, lngEntityId);
            long lngImageId = 0;
            using (var rdrImage = cmdRead.ExecuteReader())
                if (rdrImage.Read())
                {
                    lngImageId = rdrImage.GetInt64(0);
                    var objImagePolicy = CuiPolicy.Read(rdrImage, 1);
                    if (objImagePolicy != objParentPolicy) throw new CryptographicException("Image protection could not be verified.");
                    AccessControl.RequirePolicy(dbConnection, txRead, objIdentity, objImagePolicy);
                    // Inspect the BLOB length before allocating a preview buffer.
                    var lngLength = rdrImage.GetBytes(6,0,null,0,0);
                    if (lngLength is < 1 or > 20 * 1024 * 1024) throw new InvalidOperationException("The picture cannot be displayed.");
                    arrImage = SensitiveMemory.AllocateBytes(checked((int)lngLength));
                    rdrImage.GetBytes(6,0,arrImage,0,arrImage.Length);
                }
            if(arrImage is not null)
            {
                var objPreview=ImageSanitizer.Preview(arrImage);
                SensitiveMemory.Clear(objPreview.arrPixels);
            }
            ctCancellation.ThrowIfCancellationRequested(); objSession.UseTokenHash(arrHash => true);
            SecurityAudit.Append(dbConnection,txRead,objIdentity.lngUserId,AuditEntity.Images,lngImageId,AuditAction.AccessVerified);
            SecurityAudit.Commit(dbConnection,txRead);
            ctCancellation.ThrowIfCancellationRequested(); objSession.UseTokenHash(arrHash => true);
            return arrImage;
        }
        catch { SensitiveMemory.Clear(arrImage); throw; }
    }

    private long Save(RecordKind enKind, long lngEntityId, byte[] arrUntrusted, CancellationToken ctCancellation)
    {
        ArgumentNullException.ThrowIfNull(arrUntrusted);
        byte[]? arrSanitized = null;
        try
        {
            ValidateKind(enKind, lngEntityId);
            ctCancellation.ThrowIfCancellationRequested();
            // Check access before image decoding, then recheck within the write transaction.
            CuiDesignation objPolicy;
            using (var dbCheck = objDatabase.Open())
            using (var txCheck = dbCheck.BeginTransaction(false))
            {
                var objIdentity = AccessControl.Identity(dbCheck, txCheck, objSession);
                AccessControl.Require(dbCheck, txCheck, objIdentity, enKind, lngEntityId, true);
                objPolicy = CuiPolicy.ReadRecord(dbCheck, txCheck, enKind, lngEntityId);
                AccessControl.RequirePolicy(dbCheck, txCheck, objIdentity, objPolicy);
                RequireEditable(dbCheck, txCheck, enKind, lngEntityId);
                objSession.UseTokenHash(arrHash => true);
                SecurityAudit.Append(dbCheck, txCheck, objIdentity.lngUserId, AuditEntity.Images, 0, AuditAction.AccessVerified);
                SecurityAudit.Commit(dbCheck, txCheck);
            }
            arrSanitized = ImageSanitizer.Sanitize(arrUntrusted, ctCancellation);
            ctCancellation.ThrowIfCancellationRequested();
            using var dbConnection = objDatabase.Open();
            using var txOperation = dbConnection.BeginTransaction(false);
            var objActor = AccessControl.Identity(dbConnection, txOperation, objSession);
            AccessControl.Require(dbConnection, txOperation, objActor, enKind, lngEntityId, true);
            var objCurrentPolicy = CuiPolicy.ReadRecord(dbConnection, txOperation, enKind, lngEntityId);
            AccessControl.RequirePolicy(dbConnection, txOperation, objActor, objCurrentPolicy);
            if (objCurrentPolicy != objPolicy) throw new ArgumentException("Record protection changed while choosing the picture. Try again.");
            RequireEditable(dbConnection, txOperation, enKind, lngEntityId);
            using (var cmdSame = Database.Command(dbConnection,
                "SELECT id FROM profile_images WHERE entity=$entity AND entity_id=$id AND status='Active' AND png=$png",
                ("$entity", enKind.ToString()), ("$id", lngEntityId), ("$png", arrSanitized)))
            {
                cmdSame.Transaction = txOperation;
                if (cmdSame.ExecuteScalar() is long lngExisting)
                {
                    ctCancellation.ThrowIfCancellationRequested(); objSession.UseTokenHash(arrHash => true);
                    SecurityAudit.Append(dbConnection, txOperation, objActor.lngUserId, AuditEntity.Images, lngExisting, AuditAction.ImageUnchanged);
                    SecurityAudit.Commit(dbConnection, txOperation); return lngExisting;
                }
            }
            using (var cmdRetire = Database.Command(dbConnection,
                "UPDATE profile_images SET status='Inactive' WHERE entity=$entity AND entity_id=$id AND status='Active'",
                ("$entity", enKind.ToString()), ("$id", lngEntityId)))
            { cmdRetire.Transaction = txOperation; cmdRetire.ExecuteNonQuery(); }
            using (var cmdSave = Database.Command(dbConnection,
                "INSERT INTO profile_images(entity,entity_id,png,status,created_at,user_id,cui_category,distribution_statement,controlling_office,determination_date,distribution_notice) VALUES($entity,$id,$png,'Active',$now,$user,$category,$distribution,$office,$date,$notice) RETURNING id",
                ("$entity", enKind.ToString()), ("$id", lngEntityId), ("$png", arrSanitized), ("$now", Database.Now), ("$user", objActor.lngUserId)))
            {
                cmdSave.Transaction = txOperation; CuiPolicy.Bind(cmdSave, objCurrentPolicy);
                var lngVersion = Convert.ToInt64(cmdSave.ExecuteScalar());
                ctCancellation.ThrowIfCancellationRequested(); objSession.UseTokenHash(arrHash => true);
                SecurityAudit.Append(dbConnection, txOperation, objActor.lngUserId, AuditEntity.Images, lngVersion, AuditAction.Updated);
                SecurityAudit.Commit(dbConnection, txOperation); return lngVersion;
            }
        }
        finally { SensitiveMemory.Clear(arrSanitized); CryptographicOperations.ZeroMemory(arrUntrusted); }
    }

    private static void ValidateKind(RecordKind enKind,long lngEntityId)
    { if (!Enum.IsDefined(enKind) || lngEntityId<=0) throw new ArgumentException("Save a valid record before choosing a picture."); }
    private static void RequireEditable(SqliteConnection dbConnection,SqliteTransaction txOperation,RecordKind enKind,long lngEntityId)
    {
        var strTable=enKind==RecordKind.Contact ? "contacts" : "organizations";
        using var cmdParent=Database.Command(dbConnection,$"SELECT archived FROM {strTable} WHERE id=$id",("$id",lngEntityId)); cmdParent.Transaction=txOperation;
        if (Convert.ToInt64(cmdParent.ExecuteScalar())!=0) throw new ArgumentException("Restore this record before changing its picture.");
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
