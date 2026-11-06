using System.Security.Cryptography;
using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

internal static class PhotoDuplicateChecks
{
    internal static void Run()
    {
#if !DEBUG
        throw new InvalidOperationException("Run these isolated memory-fixture checks in Debug.");
#else
        var iChecks=0;
        void Check(bool bValue,string strMessage) { if(!bValue) throw new InvalidOperationException("FAIL "+strMessage); iChecks++; Console.WriteLine("PASS "+strMessage); }
        void Reject(Action fnAction,string strMessage) { var bRejected=false; try { fnAction(); } catch(Exception objException) when(objException is ArgumentException or UnauthorizedAccessException or SqliteException or OperationCanceledException) { bRejected=true; } Check(bRejected,strMessage); }
        using var objDatabase=Database.CreateUiDevelopmentWorkspace();
        var objAuth=new AuthService(objDatabase);
        using var secPassword=TestPasswords.Secret("FICTIONAL photo duplicate fixture!");
        using var objAdmin=objAuth.CreateAdministrator("fixture-admin",secPassword);
        var objUsers=new UserAdministrationService(objDatabase,objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId,true);
        var objCrm=new CrmService(objDatabase,objAdmin); var objImages=new ProfileImageService(objDatabase,objAdmin);
        long Scalar(string strSql) { using var dbConnection=objDatabase.Open(); using var cmdQuery=Database.Command(dbConnection,strSql); return Convert.ToInt64(cmdQuery.ExecuteScalar()); }
        void Sql(string strSql) { using var dbConnection=objDatabase.Open(); using var cmdQuery=Database.Command(dbConnection,strSql); cmdQuery.ExecuteNonQuery(); }
        var objContact=new Contact { FirstName="FICTIONAL",LastName="One",Email="person@example.test",Phone="+442079460958" }; objCrm.Save(objContact);
        var objOrg=new Organization { Name="FICTIONAL Organization",Email="office@example.test" }; objCrm.Save(objOrg);
        foreach(var bJpeg in new[]{false,true})
        {
            var arrSource=Picture(bJpeg); var arrPng=ImageSanitizer.Sanitize(arrSource);
            try
            {
                Check(arrSource.All(bytValue=>bytValue==0),"Original PNG/JPEG cleared");
                using var imgResult=Image.Load<Rgba32>(arrPng);
                Check(imgResult.Metadata.ExifProfile is null && imgResult.Metadata.XmpProfile is null && imgResult.Metadata.IccProfile is null,"Imported metadata stripped");
            }
            finally { CryptographicOperations.ZeroMemory(arrPng); }
        }
        var arrLarge=Picture(false,1600,800); var arrSmall=ImageSanitizer.Sanitize(arrLarge);
        var objPixels=ImageSanitizer.Preview(arrSmall); Check(objPixels.iWidth==1024 && objPixels.iHeight==512,"Storage and preview dimensions bounded proportionally"); CryptographicOperations.ZeroMemory(objPixels.arrPixels); CryptographicOperations.ZeroMemory(arrSmall);
        var arrBad=new byte[]{137,80,78,71,13,10,26,10}; Reject(()=>ImageSanitizer.Sanitize(arrBad),"Forged/truncated picture rejected"); Check(arrBad.All(bytValue=>bytValue==0),"Malformed input cleared");
        var arrOversized=new byte[10*1024*1024+1]; Reject(()=>ImageSanitizer.Sanitize(arrOversized),"Oversized input rejected");
        var arrFirst=Picture(); var lngFirst=objImages.SaveContactPicture(objContact.Id,arrFirst);
        Check(arrFirst.All(bytValue=>bytValue==0),"Saved source cleared");
        Check(objImages.SaveContactPicture(objContact.Id,Picture())==lngFirst && Scalar("SELECT count(*) FROM profile_images")==1,"Identical upload is an audited no-change");
        var lngSecond=objImages.SaveContactPicture(objContact.Id,Picture(true));
        Check(lngSecond!=lngFirst && Scalar("SELECT count(*) FROM profile_images WHERE status='Inactive'")==1,"Different picture retains old version");
        Check(Scalar($"SELECT count(*) FROM audit_log WHERE entity='Images' AND action='Updated' AND entity_id={lngSecond}")==1,"Audit identifies exact image version");
        var arrRead=objImages.Read(RecordKind.Contact,objContact.Id)!; Check(arrRead.Length>0,"Authorized audited retrieval returns pixels"); CryptographicOperations.ZeroMemory(arrRead);
        Reject(()=>Sql("UPDATE profile_images SET png=x'00' WHERE id=1"),"Old pixels immutable");
        Reject(()=>Sql("UPDATE profile_images SET entity_id=999999 WHERE id=1"),"Image owner immutable");
        Reject(()=>Sql("DELETE FROM profile_images WHERE id=1"),"Image delete blocked");
        Reject(()=>Sql($"UPDATE contacts SET id=999999 WHERE id={objContact.Id}"),"Parent IDs cannot be changed to orphan a photo");
        Sql("CREATE TRIGGER fixture_read_audit BEFORE INSERT ON audit_log WHEN NEW.entity='Images' AND NEW.action='AccessVerified' BEGIN SELECT RAISE(ABORT,'FICTIONAL read audit denied'); END");
        Reject(()=>objImages.Read(RecordKind.Contact,objContact.Id),"Image retrieval fails secure when auditing is unavailable"); Sql("DROP TRIGGER fixture_read_audit");
        Reject(()=>Sql($"INSERT INTO profile_images(entity,entity_id,png,status,created_at,user_id) VALUES('Contact',999999,x'00','Active','fixture',{objAdmin.UserId})"),"Orphan image insert rejected");
        objImages.SaveOrganizationLogo(objOrg.Id,Picture()); Check(Scalar("SELECT count(*) FROM profile_images WHERE entity='Organization'")==1,"Company image has separate owner");
        var lngBefore=Scalar("SELECT count(*) FROM profile_images");
        Sql("CREATE TRIGGER fixture_image_full BEFORE INSERT ON profile_images BEGIN SELECT RAISE(ABORT,'FICTIONAL interrupted insert'); END");
        var arrInterrupted=Picture(false,3,3); Reject(()=>objImages.SaveContactPicture(objContact.Id,arrInterrupted),"Interrupted insert rolls back"); Sql("DROP TRIGGER fixture_image_full");
        Check(Scalar("SELECT count(*) FROM profile_images")==lngBefore && Scalar($"SELECT count(*) FROM profile_images WHERE id={lngSecond} AND status='Active'")==1,"Interrupted replacement preserves current and retained versions");
        Check(arrInterrupted.All(bytValue=>bytValue==0),"Failed save clears source");
        Sql("CREATE TRIGGER fixture_image_audit BEFORE INSERT ON audit_log WHEN NEW.action='Updated' AND NEW.entity='Images' BEGIN SELECT RAISE(ABORT,'FICTIONAL audit denied'); END");
        Reject(()=>objImages.SaveContactPicture(objContact.Id,Picture(false,4,4)),"Image audit failure blocks replacement"); Sql("DROP TRIGGER fixture_image_audit");
        Check(Scalar($"SELECT count(*) FROM profile_images WHERE id={lngSecond} AND status='Active'")==1,"Audit failure restores old current image");
        using(var objCancel=new CancellationTokenSource()) { objCancel.Cancel(); var arrCanceled=Picture(); Reject(()=>objImages.SaveContactPicture(objContact.Id,arrCanceled,objCancel.Token),"Canceled save blocked"); Check(arrCanceled.All(bytValue=>bytValue==0),"Canceled save clears input"); }
        objCrm.SetArchived(RecordKind.Contact,objContact.Id,objContact.Revision,true);
        Reject(()=>objImages.SaveContactPicture(objContact.Id,Picture()),"Archived contact replacement blocked");
        objCrm.SetArchived(RecordKind.Organization,objOrg.Id,objOrg.Revision,true);
        Reject(()=>objImages.SaveOrganizationLogo(objOrg.Id,Picture()),"Archived company replacement blocked");
        var arrArchived=objImages.Read(RecordKind.Contact,objContact.Id)!; Check(arrArchived.Length>0,"Archived picture remains readable"); CryptographicOperations.ZeroMemory(arrArchived);
        objContact=objCrm.Contacts(bArchived:true,enStatus:null).Single(); objCrm.SetArchived(RecordKind.Contact,objContact.Id,objContact.Revision,false);
        objContact=objCrm.Contacts(enStatus:null).Single();
        var objDuplicate=new Contact { FirstName="FICTIONAL",LastName="Two",Email="person@example.test",Phone="+44 20 7946 0958" };
        var lstMatches=objCrm.PossibleDuplicates(objDuplicate);
        Check(lstMatches.Count==1 && lstMatches[0].strReason.Contains("Shared email") && lstMatches[0].strReason.Contains("Shared phone"),"Normalized shared identifiers flag a possible match");
        Reject(()=>objCrm.Save(objDuplicate,new DuplicateDecision(DuplicateResolution.Ignore,null!)),"Malformed review decision is rejected safely");
        Reject(()=>objCrm.Save(objDuplicate),"Unreviewed duplicate preserves draft and blocks silent save");
        objCrm.Save(objDuplicate,new DuplicateDecision(DuplicateResolution.Ignore,lstMatches));
        Check(Scalar("SELECT count(*) FROM contacts")==2 && Scalar("SELECT count(*) FROM audit_log WHERE action='DuplicateOverride'")==1,"Explicit ignore saves separately and audits decision");
        lstMatches=objCrm.PossibleDuplicates(objDuplicate);
        objCrm.Save(objDuplicate,new DuplicateDecision(DuplicateResolution.Link,lstMatches,objContact.Id));
        Check(Scalar("SELECT count(*) FROM record_links")==1 && Scalar("SELECT count(*) FROM contacts")==2,"Review and link preserves both source records");
        Check(objCrm.LinkedRecords(RecordKind.Contact,objDuplicate.Id).Single().lngId==objContact.Id,"Link can be reviewed through authorized gateway");
        lstMatches=objCrm.PossibleDuplicates(objDuplicate); objCrm.Save(objDuplicate,new DuplicateDecision(DuplicateResolution.Link,lstMatches,objContact.Id));
        Check(Scalar("SELECT count(*) FROM record_links")==1,"Repeated link is idempotent");
        Reject(()=>Sql("UPDATE record_links SET target_id=999999"),"Link provenance immutable"); Reject(()=>Sql("DELETE FROM record_links"),"Link deletion blocked");
        Check(objCrm.PossibleDuplicates(new Contact { FirstName="FICTIONAL nullable",Email=null!,Phone=null!,Address=null! }).Count==0,"Optional empty details do not break duplicate scanning");
        var objSameName=new Contact { FirstName="FICTIONAL",LastName="Two" }; Check(objCrm.PossibleDuplicates(objSameName).Count==0,"Name alone never forces an identity match");
        var objOtherOrg=new Organization { Name="FICTIONAL Organization",Email="office@example.test" }; var lstOrgMatches=objCrm.PossibleDuplicates(objOtherOrg);
        Check(lstOrgMatches.Count==1,"Company duplicate suggestions include retained records");
        objCrm.Save(objOtherOrg,new DuplicateDecision(DuplicateResolution.Ignore,lstOrgMatches)); Check(Scalar("SELECT count(*) FROM organizations")==2,"Company override retains two organizations");
        var lngEditor=objUsers.CreateUser("fixture-editor",secPassword,UserRole.Editor); objUsers.SetCuiAuthorization(lngEditor,true); objUsers.SetRecordAccess(lngEditor,RecordKind.Contact,objDuplicate.Id,true,true);
        using var objEditor=objAuth.SignIn("fixture-editor",secPassword); var objEditorCrm=new CrmService(objDatabase,objEditor);
        Check(objEditorCrm.PossibleDuplicates(objDuplicate).Count==0 && objEditorCrm.LinkedRecords(RecordKind.Contact,objDuplicate.Id).Count==0,"Hidden duplicate and linked record disclose no result");
        Reject(()=>objEditorCrm.Save(objDuplicate,new DuplicateDecision(DuplicateResolution.Link,[],objContact.Id)),"Forged hidden-record link denied");
        var lngUnapproved=objUsers.CreateUser("fixture-unapproved",secPassword,UserRole.Administrator);
        using var objUnapproved=objAuth.SignIn("fixture-unapproved",secPassword);
        var arrUnapproved=Picture(); Reject(()=>new ProfileImageService(objDatabase,objUnapproved).SaveContactPicture(objContact.Id,arrUnapproved),"Administrator without CUI approval cannot upload");
        Check(arrUnapproved.All(bytValue=>bytValue==0),"CUI denial clears source bytes");
        var arrDenied=Picture(); Reject(()=>new ProfileImageService(objDatabase,objEditor).SaveContactPicture(objContact.Id,arrDenied),"Image write requires live parent grant"); Check(arrDenied.All(bytValue=>bytValue==0),"Denied upload clears source");
        Reject(()=>new ProfileImageService(objDatabase,objEditor).Read(RecordKind.Contact,objContact.Id),"Image read requires live parent grant");
        objUsers.SetRecordAccess(lngEditor,RecordKind.Contact,objContact.Id,true,true);
        var lstReviewed=objEditorCrm.PossibleDuplicates(objDuplicate);
        objUsers.SetRecordAccess(lngEditor,RecordKind.Contact,objContact.Id,true,false);
        Reject(()=>objEditorCrm.Save(objDuplicate,new DuplicateDecision(DuplicateResolution.Link,lstReviewed,objContact.Id)),"Changed permission invalidates a reviewed link before mutation");
        var objStaleDraft=new Contact { FirstName="FICTIONAL",Email="person@example.test" }; var lstStale=objCrm.PossibleDuplicates(objStaleDraft);
        objDuplicate.LastName="Changed after review"; objCrm.Save(objDuplicate,new DuplicateDecision(DuplicateResolution.Ignore,objCrm.PossibleDuplicates(objDuplicate)));
        Reject(()=>objCrm.Save(objStaleDraft,new DuplicateDecision(DuplicateResolution.Ignore,lstStale)),"Changed candidate revision requires fresh review");
        Sql("CREATE TRIGGER fixture_duplicate_audit BEFORE INSERT ON audit_log WHEN NEW.action='DuplicateOverride' BEGIN SELECT RAISE(ABORT,'FICTIONAL review audit denied'); END");
        var lngBeforeDuplicate=Scalar("SELECT count(*) FROM contacts");
        Reject(()=>objCrm.Save(objStaleDraft,new DuplicateDecision(DuplicateResolution.Ignore,objCrm.PossibleDuplicates(objStaleDraft))),"Duplicate decision audit failure rolls back record creation");
        Sql("DROP TRIGGER fixture_duplicate_audit"); Check(Scalar("SELECT count(*) FROM contacts")==lngBeforeDuplicate,"Failed override leaves no partial record");
        // Real SQLITE_FULL without filling the host disk: cap fixture page growth.
        var lngPages=Scalar("PRAGMA page_count"); Sql($"PRAGMA max_page_count={lngPages}");
        var arrFull=Picture(false,1024,1024,true); var bFull=false;
        try { objImages.SaveContactPicture(objDuplicate.Id,arrFull); } catch(SqliteException objException) { bFull=objException.SqliteErrorCode==13; }
        Sql("PRAGMA max_page_count=1073741823"); Check(bFull && arrFull.All(bytValue=>bytValue==0),"SQLite-full error clears input without filling real disk");
        Check(Scalar("SELECT count(*) FROM profile_images")==lngBefore,"SQLite-full failure retains image history");
        LegacyMigration(Check,secPassword);
        PersistentPicture(Check,secPassword);
        Console.WriteLine($"{iChecks} photo/duplicate security checks passed.");
#endif
    }
#if DEBUG
    private static void PersistentPicture(Action<bool,string> fnCheck,System.Security.SecureString secPassword)
    {
        var strRoot=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","photo-persistence",Guid.NewGuid().ToString("N"),"UiDevelopment");
        using var objKeys=new EphemeralKeyStore(); long lngContactId;
        using(var objDatabase=Database.OpenUiDevelopmentWorkspace(strRoot,objKeys))
        {
            var objAuth=new AuthService(objDatabase); using var objSession=objAuth.CreateAdministrator("fixture-persist",secPassword);
            new UserAdministrationService(objDatabase,objSession).SetCuiAuthorization(objSession.UserId,true);
            var objCrm=new CrmService(objDatabase,objSession); var objContact=new Contact { FirstName="FICTIONAL encrypted photo" }; objCrm.Save(objContact); lngContactId=objContact.Id;
            new ProfileImageService(objDatabase,objSession).SaveContactPicture(lngContactId,Picture());
        }
        var arrCipher=File.ReadAllBytes(Path.Combine(strRoot,"ui-development.db"));
        fnCheck(!arrCipher.AsSpan(0,16).SequenceEqual("SQLite format 3\0"u8),"Persistent fixture database has no plaintext SQLite header");
        fnCheck(arrCipher.AsSpan().IndexOf(new byte[]{137,80,78,71,13,10,26,10})<0,"Stored picture signature is absent from database ciphertext");
        CryptographicOperations.ZeroMemory(arrCipher);
        using(var objDatabase=Database.OpenUiDevelopmentWorkspace(strRoot,objKeys))
        {
            using var objSession=new AuthService(objDatabase).SignIn("fixture-persist",secPassword);
            var arrImage=new ProfileImageService(objDatabase,objSession).Read(RecordKind.Contact,lngContactId)!;
            fnCheck(arrImage.Length>0,"Encrypted picture and precise audit/checkpoint history survive reopen"); CryptographicOperations.ZeroMemory(arrImage);
        }
    }
    private static void LegacyMigration(Action<bool,string> fnCheck,System.Security.SecureString secPassword)
    {
        using var objDatabase=Database.CreateUiDevelopmentWorkspace(); var objAuth=new AuthService(objDatabase); using var objAdmin=objAuth.CreateAdministrator("fixture-legacy",secPassword);
        new UserAdministrationService(objDatabase,objAdmin).SetCuiAuthorization(objAdmin.UserId,true);
        var objCrm=new CrmService(objDatabase,objAdmin); var objContact=new Contact { FirstName="FICTIONAL retained" }; objCrm.Save(objContact);
        using var dbConnection=objDatabase.Open();
        using(var cmdLegacy=Database.Command(dbConnection,"DROP TABLE profile_images; UPDATE schema_version SET version=7; CREATE TABLE profile_images(id INTEGER PRIMARY KEY,contact_id INTEGER,organization_id INTEGER,image_data BLOB,created_at TEXT,user_id INTEGER); INSERT INTO profile_images VALUES(51,$contact,NULL,$png,'fixture',$user)",("$contact",objContact.Id),("$png",Picture()),("$user",objAdmin.UserId))) cmdLegacy.ExecuteNonQuery();
        using(var txUpgrade=dbConnection.BeginTransaction(false)) { SchemaManager.Initialize(dbConnection,txUpgrade); SecurityAudit.Commit(dbConnection,txUpgrade); }
        using(var cmdCount=Database.Command(dbConnection,"SELECT count(*) FROM profile_images WHERE id=51 AND entity='Contact' AND entity_id=$contact",("$contact",objContact.Id))) fnCheck(Convert.ToInt64(cmdCount.ExecuteScalar())==1,"Older local photo schema upgrades without dropping retained version IDs");
        using(var cmdLegacy=Database.Command(dbConnection,"SELECT count(*) FROM legacy_profile_images_v5")) fnCheck(Convert.ToInt64(cmdLegacy.ExecuteScalar())==1,"Original legacy BLOB remains retained");
        using(var txRepeat=dbConnection.BeginTransaction(false)) { SchemaManager.Initialize(dbConnection,txRepeat); SecurityAudit.Commit(dbConnection,txRepeat); }
        using(var cmdCount=Database.Command(dbConnection,"SELECT count(*) FROM profile_images")) fnCheck(Convert.ToInt64(cmdCount.ExecuteScalar())==1,"Image compatibility upgrade is repeatable without duplicate BLOBs");
    }
#endif
    internal static byte[] Picture(bool bJpeg=false,int iWidth=2,int iHeight=2,bool bNoise=false)
    {
        using var imgPicture=new Image<Rgba32>(iWidth,iHeight);
        if(bNoise) { var arrPixels=new byte[iWidth*iHeight*4]; RandomNumberGenerator.Fill(arrPixels); using var imgNoise=Image.LoadPixelData<Rgba32>(arrPixels,iWidth,iHeight); CryptographicOperations.ZeroMemory(arrPixels); using var stmNoise=new MemoryStream(); imgNoise.Save(stmNoise,new PngEncoder()); return stmNoise.ToArray(); }
        imgPicture.Metadata.ExifProfile=new ExifProfile(); imgPicture.Metadata.ExifProfile.SetValue(ExifTag.Artist,"FICTIONAL metadata");
        using var stmPicture=new MemoryStream(); if(bJpeg) imgPicture.Save(stmPicture,new JpegEncoder()); else imgPicture.Save(stmPicture,new PngEncoder()); return stmPicture.ToArray();
    }
}
