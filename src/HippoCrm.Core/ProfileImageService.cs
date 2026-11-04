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
            using var cmdRead = Database.Command(dbConnection, "SELECT id,png FROM profile_images WHERE entity=$entity AND entity_id=$id AND status='Active'", ("$entity",enKind.ToString()),("$id",lngEntityId));
            cmdRead.Transaction = txRead;
            long lngImageId = 0;
            using (var rdrImage = cmdRead.ExecuteReader())
                if (rdrImage.Read())
                {
                    lngImageId = rdrImage.GetInt64(0);
                    // Inspect the BLOB length before allocating a preview buffer.
                    var lngLength = rdrImage.GetBytes(1,0,null,0,0);
                    if (lngLength is < 1 or > 20 * 1024 * 1024) throw new InvalidOperationException("The picture cannot be displayed.");
                    arrImage = SensitiveMemory.AllocateBytes(checked((int)lngLength));
                    rdrImage.GetBytes(1,0,arrImage,0,arrImage.Length);
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
            ValidateKind(enKind,lngEntityId); ctCancellation.ThrowIfCancellationRequested();
            // Authorize before decoding, without holding the database write lock
            // while a hostile image consumes CPU. Reauthorize before any mutation.
            using (var dbCheck = objDatabase.Open())
            using (var txCheck = dbCheck.BeginTransaction(false))
            {
                var objIdentity = AccessControl.Identity(dbCheck,txCheck,objSession);
                AccessControl.Require(dbCheck,txCheck,objIdentity,enKind,lngEntityId,true);
                RequireEditable(dbCheck,txCheck,enKind,lngEntityId);
                objSession.UseTokenHash(arrHash => true);
                SecurityAudit.Append(dbCheck,txCheck,objIdentity.lngUserId,AuditEntity.Images,0,AuditAction.AccessVerified);
                SecurityAudit.Commit(dbCheck,txCheck);
            }
            arrSanitized = ImageSanitizer.Sanitize(arrUntrusted,ctCancellation);
            ctCancellation.ThrowIfCancellationRequested();
            using var dbConnection = objDatabase.Open(); using var txOperation = dbConnection.BeginTransaction(false);
            var objActor = AccessControl.Identity(dbConnection,txOperation,objSession);
            AccessControl.Require(dbConnection,txOperation,objActor,enKind,lngEntityId,true);
            RequireEditable(dbConnection,txOperation,enKind,lngEntityId);
            using var cmdSame = Database.Command(dbConnection,"SELECT id FROM profile_images WHERE entity=$entity AND entity_id=$id AND status='Active' AND png=$png",("$entity",enKind.ToString()),("$id",lngEntityId),("$png",arrSanitized));
            cmdSame.Transaction=txOperation;
            var objExisting=cmdSame.ExecuteScalar();
            if (objExisting is long lngExisting)
            {
                ctCancellation.ThrowIfCancellationRequested(); objSession.UseTokenHash(arrHash => true);
                SecurityAudit.Append(dbConnection,txOperation,objActor.lngUserId,AuditEntity.Images,lngExisting,AuditAction.ImageUnchanged);
                SecurityAudit.Commit(dbConnection,txOperation); return lngExisting;
            }
            using var cmdRetire=Database.Command(dbConnection,"UPDATE profile_images SET status='Inactive' WHERE entity=$entity AND entity_id=$id AND status='Active'",("$entity",enKind.ToString()),("$id",lngEntityId));
            cmdRetire.Transaction=txOperation; cmdRetire.ExecuteNonQuery();
            using var cmdSave=Database.Command(dbConnection,"INSERT INTO profile_images(entity,entity_id,png,status,created_at,user_id) VALUES($entity,$id,$png,'Active',$now,$user) RETURNING id",("$entity",enKind.ToString()),("$id",lngEntityId),("$png",arrSanitized),("$now",Database.Now),("$user",objActor.lngUserId));
            cmdSave.Transaction=txOperation; var lngVersion=Convert.ToInt64(cmdSave.ExecuteScalar());
            ctCancellation.ThrowIfCancellationRequested(); objSession.UseTokenHash(arrHash => true);
            SecurityAudit.Append(dbConnection,txOperation,objActor.lngUserId,AuditEntity.Images,lngVersion,AuditAction.Updated);
            SecurityAudit.Commit(dbConnection,txOperation); return lngVersion;
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
}
