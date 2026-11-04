using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

internal static class PreCommitSecurityChecks
{
    private static int iPassed;
    internal static void Run()
    {
        ImageChecks();
        var strRoot = Path.Combine(Environment.CurrentDirectory, "artifacts", "precommit-review", "runs", Guid.NewGuid().ToString("N"));
        using var objKeys = new FixtureKeys();
        var objDatabase = new Database(Path.Combine(strRoot, "checks.db"), objKeys);
        var objAuth = new AuthService(objDatabase);
        using var secPassword = TestPasswords.Secret("FICTIONAL review passphrase 2026!");
        using var objAdmin = objAuth.CreateAdministrator("fixture-admin", secPassword);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objVisible = new Contact { FirstName = "FICTIONAL visible" };
        var objHidden = new Contact { FirstName = "FICTIONAL hidden" };
        var objInactive = new Contact { FirstName = "FICTIONAL inactive", Status = RecordStatus.Inactive };
        var objArchived = new Contact { FirstName = "FICTIONAL archived", Archived = true };
        objCrm.Save(objVisible); objCrm.Save(objHidden); objCrm.Save(objInactive); objCrm.Save(objArchived);
        var objOrg = new Organization { Name = "FICTIONAL visible organization" };
        var objHiddenOrg = new Organization { Name = "FICTIONAL hidden organization" };
        objCrm.Save(objOrg); objCrm.Save(objHiddenOrg);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        var lngViewer = objUsers.CreateUser("fixture-viewer", secPassword, UserRole.Viewer);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objVisible.Id, true, false);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objOrg.Id, true, false);
        using var objViewer = objAuth.SignIn("fixture-viewer", secPassword);

        var dbUnused = new CrmDbContext(objDatabase);
        var dbUnusedConnection = (SqliteConnection)typeof(CrmDbContext).GetField("dbEncryptedConnection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dbUnused)!;
        dbUnused.Dispose();
        Check(dbUnusedConnection.State == System.Data.ConnectionState.Closed, "An unused EF context closes its encrypted connection on disposal");
        var dbUnusedAsync = new CrmDbContext(objDatabase);
        var dbUnusedAsyncConnection = (SqliteConnection)typeof(CrmDbContext).GetField("dbEncryptedConnection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dbUnusedAsync)!;
        dbUnusedAsync.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Check(dbUnusedAsyncConnection.State == System.Data.ConnectionState.Closed, "Async disposal closes an unused encrypted connection");

        using (var dbContext = new CrmDbContext(objDatabase))
        {
            Reject<UnauthorizedAccessException>(() => dbContext.dbContacts.Count(), "Direct EF reads cannot bypass the audited gateway");
            Reject<UnauthorizedAccessException>(() => dbContext.dbContacts.CountAsync().GetAwaiter().GetResult(), "Async EF reads cannot bypass the audited gateway");
            Check(dbContext.ReadAuthorizedContacts(objViewer).Single().Id == objVisible.Id, "EF contact reads honor record grants");
            Check(dbContext.ReadAuthorizedOrganizations(objViewer).Single().Id == objOrg.Id, "EF organization reads honor record grants");
            Check(dbContext.ReadAuthorizedContacts(objAdmin).Count == 2, "EF filters exclude inactive and archived contacts");
            // Negative tests: these commands must be rejected before execution.
            Reject<UnauthorizedAccessException>(() => dbContext.dbContacts.ExecuteDelete(), "EF bulk deletion is blocked before execution");
            Reject<UnauthorizedAccessException>(() => dbContext.Database.ExecuteSqlRaw("DELETE FROM contacts"), "Raw EF SQL cannot bypass the audited gateway");
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objVisible.Id, false, false);
            Check(dbContext.ReadAuthorizedContacts(objViewer).Count == 0, "EF reads observe revoked grants without recreating the context");
            objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objVisible.Id, true, false);
            Sql(objDatabase, "CREATE TRIGGER fixture_deny_read_audit BEFORE INSERT ON audit_log WHEN NEW.action='AccessVerified' BEGIN SELECT RAISE(ABORT,'Fixture audit failure'); END");
            Reject<SqliteException>(() => dbContext.ReadAuthorizedContacts(objViewer), "EF reads fail closed when their audit cannot be written");
            Sql(objDatabase, "DROP TRIGGER fixture_deny_read_audit");
            objAuth.SignOut(objViewer);
            Reject<UnauthorizedAccessException>(() => dbContext.ReadAuthorizedContacts(objViewer), "EF reads reject revoked or disposed sessions");
        }
        using (var dbContext = new CrmDbContext(objDatabase))
        {
            var objNew = new Contact { FirstName = "FICTIONAL EF create" };
            dbContext.dbContacts.Add(objNew);
            Reject<InvalidOperationException>(() => dbContext.SaveChanges(), "Ordinary EF SaveChanges cannot commit unaudited writes");
            dbContext.SaveAuthorizedChanges(objAdmin);
            Check(!dbContext.ChangeTracker.Entries().Any(), "EF writes leave no cross-session tracked-record cache");
            Check(objNew.Id > 0, "EF cache clearing preserves generated database IDs");
            Check(Scalar(objDatabase, "SELECT count(*) FROM contacts") == 5, "Authorized EF creation commits its record");
            Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE entity='Contacts' AND action='Created' AND length(signature)=32") >= 5, "EF creation emits a signed audit event");
            dbContext.dbContacts.Remove(objNew);
            Reject<InvalidOperationException>(() => dbContext.SaveAuthorizedChanges(objAdmin), "Tracked EF deletion is rejected");
            Check(Scalar(objDatabase, "SELECT count(*) FROM contacts") == 5, "Rejected EF deletion preserves the record");
        }
        using (var dbContext = new CrmDbContext(objDatabase))
        {
            dbContext.dbOrganizations.Add(new Organization { Name = "FICTIONAL rolled-back EF create" });
            Sql(objDatabase, "CREATE TRIGGER fixture_deny_create_audit BEFORE INSERT ON audit_log WHEN NEW.action='Created' BEGIN SELECT RAISE(ABORT,'Fixture audit failure'); END");
            Reject<SqliteException>(() => dbContext.SaveAuthorizedChanges(objAdmin), "EF mutation fails closed when its audit cannot be written");
            Sql(objDatabase, "DROP TRIGGER fixture_deny_create_audit");
            Check(Scalar(objDatabase, "SELECT count(*) FROM organizations") == 2, "Failed EF audit rolls back the business record");
        }

        var objImages = new ProfileImageService(objDatabase, objAdmin);
        var arrPicture = FixtureImage(false); objImages.SaveContactPicture(objVisible.Id, arrPicture);
        Check(arrPicture.All(bytValue => bytValue == 0), "Image service clears imported bytes after encrypted storage");
        objImages.SaveContactPicture(objVisible.Id, FixtureImage(true));
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images WHERE status='Active'") == 1, "Replacement image has one active BLOB version");
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images WHERE status='Inactive'") == 1, "Replacement image retains the prior BLOB version");
        objImages.SaveOrganizationLogo(objOrg.Id, FixtureImage(false));
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images WHERE entity='Organization' AND status='Active'") == 1, "Organization logos are stored in encrypted BLOBs");
        Sql(objDatabase, "CREATE TRIGGER fixture_deny_image_audit BEFORE INSERT ON audit_log WHEN NEW.entity='Images' BEGIN SELECT RAISE(ABORT,'Fixture audit failure'); END");
        var arrRejected = FixtureImage(false);
        Reject<SqliteException>(() => objImages.SaveContactPicture(objVisible.Id, arrRejected), "Image writes fail closed when auditing fails");
        Check(arrRejected.All(bytValue => bytValue == 0), "Failed image transactions still wipe their imported bytes");
        Sql(objDatabase, "DROP TRIGGER fixture_deny_image_audit");
        Check(Scalar(objDatabase, "SELECT count(*) FROM profile_images") == 3, "Failed image audit rolls back both BLOB insertion and status changes");
        using var objReadOnlySession = objAuth.SignIn("fixture-viewer", secPassword);
        var arrDenied = FixtureImage(false);
        Reject<UnauthorizedAccessException>(() => new ProfileImageService(objDatabase, objReadOnlySession).SaveContactPicture(objVisible.Id, arrDenied), "Viewer cannot upload a contact image");
        Check(arrDenied.All(bytValue => bytValue == 0), "Authorization rejection wipes the original image buffer");
        Console.WriteLine($"{iPassed} pre-commit security checks passed.");
    }

    private static void ImageChecks()
    {
        foreach (var bJpeg in new[] { false, true })
        {
            var arrInput = FixtureImage(bJpeg);
            byte[]? arrPng = null;
            try
            {
                arrPng = ImageSanitizer.Sanitize(arrInput);
                Check(arrInput.All(bytValue => bytValue == 0), "Sanitizer clears the original PNG/JPEG input");
                Check(arrPng.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Sanitizer outputs a standard PNG");
                using var imgResult = Image.Load<Rgba32>(arrPng);
                Check(imgResult.Width == 2 && imgResult.Height == 2, "Sanitizer preserves expected image dimensions");
                Check(imgResult.Metadata.ExifProfile is null && imgResult.Metadata.IccProfile is null && imgResult.Metadata.IptcProfile is null && imgResult.Metadata.XmpProfile is null, "Sanitized output contains no imported metadata profiles");
            }
            finally { SensitiveMemory.Clear(arrInput); SensitiveMemory.Clear(arrPng); }
        }
        var arrInvalid = Encoding.ASCII.GetBytes("FICTIONAL not an image");
        Reject<ArgumentException>(() => ImageSanitizer.Sanitize(arrInvalid), "Non-image magic numbers are rejected");
        Check(arrInvalid.All(bytValue => bytValue == 0), "Header rejection wipes untrusted input");
        var arrTruncated = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        Reject<Exception>(() => ImageSanitizer.Sanitize(arrTruncated), "A forged header is not accepted as a valid image");
        Check(arrTruncated.All(bytValue => bytValue == 0), "Decoder failure wipes untrusted input");
        var arrOversized = new byte[10 * 1024 * 1024 + 1]; arrOversized[0] = 42;
        Reject<ArgumentException>(() => ImageSanitizer.Sanitize(arrOversized), "Oversized image input is rejected");
        Check(arrOversized.All(bytValue => bytValue == 0), "Oversized input is wiped on rejection");

        var typAllocator = typeof(ImageSanitizer).GetNestedType("PinnedClearingAllocator", BindingFlags.NonPublic)!;
        using var objAllocator = (IDisposable)Activator.CreateInstance(typAllocator, nonPublic: true)!;
        var mthAllocate = typAllocator.GetMethod("Allocate")!.MakeGenericMethod(typeof(byte));
        using var objOwner = (IMemoryOwner<byte>)mthAllocate.Invoke(objAllocator, [16, SixLabors.ImageSharp.Memory.AllocationOptions.None])!;
        var objMemory = objOwner.Memory; objMemory.Span.Fill(42); objAllocator.Dispose();
        Check(objMemory.Span.ToArray().All(bytValue => bytValue == 0), "Image allocator disposal wipes outstanding decoder buffers");
        Reject<ObjectDisposedException>(() => _ = objOwner.Memory, "Disposed image memory cannot be reused");
    }

    private static byte[] FixtureImage(bool bJpeg)
    {
        // Generated fictional pixels/metadata only; never reads a user's image file.
        using var imgFixture = new Image<Rgba32>(2, 2, new Rgba32(30, 50, 70, 255));
        imgFixture.Metadata.ExifProfile = new ExifProfile();
        imgFixture.Metadata.ExifProfile.SetValue(ExifTag.Software, "FICTIONAL metadata marker");
        using var stmFixture = new MemoryStream();
        if (bJpeg) imgFixture.Save(stmFixture, new JpegEncoder()); else imgFixture.Save(stmFixture, new PngEncoder());
        return stmFixture.ToArray();
    }
    private static void Sql(Database objDatabase, string strSql)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = new SqliteCommand(strSql, dbConnection); cmdSql.ExecuteNonQuery(); }
    private static long Scalar(Database objDatabase, string strSql)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = new SqliteCommand(strSql, dbConnection); return Convert.ToInt64(cmdSql.ExecuteScalar()); }
    private static void Check(bool bCondition, string strDescription)
    {
        if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
        iPassed++; Console.WriteLine("PASS " + strDescription);
    }
    private static void Reject<T>(Action actOperation, string strDescription) where T : Exception
    {
        try { actOperation(); } catch (T) { Check(true, strDescription); return; }
        throw new InvalidOperationException("FAIL " + strDescription);
    }
    private sealed class FixtureKeys : IDatabaseKeyStore, IDisposable
    {
        private readonly Dictionary<string, byte[]> dicKeys = new();
        public void EnsureKey(string strIdentity, bool bCreate)
        {
            if (dicKeys.ContainsKey(strIdentity)) return;
            if (!bCreate) throw new CryptographicException("Missing fixture key.");
            var arrKey = SensitiveMemory.AllocateBytes(32); RandomNumberGenerator.Fill(arrKey); dicKeys.Add(strIdentity, arrKey);
        }
        public byte[] ReadKey(string strIdentity)
        { var arrCopy = SensitiveMemory.AllocateBytes(32); dicKeys[strIdentity].CopyTo(arrCopy, 0); return arrCopy; }
        public void Dispose() { foreach (var arrKey in dicKeys.Values) SensitiveMemory.Clear(arrKey); }
    }
}
