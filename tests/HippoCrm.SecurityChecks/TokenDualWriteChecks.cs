using HippoCrm.Core;
using HippoCrm.TestSupport;
using Microsoft.Data.Sqlite;

internal static class TokenDualWriteChecks
{
    internal static void Run()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL retained link fixture!");
        var objAuth = new AuthService(objDatabase);
        using var objAdmin = objAuth.CreateAdministrator("link-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objContact = new Contact { FirstName = "Fixture", Skills = "Alpha, Beta", strTags = "Shared" };
        var lngAudit = Scalar(objDatabase, "SELECT count(*) FROM audit_log");
        objCrm.Save(objContact);
        Check(Text(objDatabase, "SELECT skills||'|'||tags FROM contacts WHERE id=1") == "Alpha, Beta|Shared"
            && Scalar(objDatabase, "SELECT count(*) FROM contact_skills WHERE status='Active'") == 2
            && Scalar(objDatabase, "SELECT count(*) FROM contact_tags WHERE status='Active'") == 1,
            "New contact retains text and creates Active skill and tag links");
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log") == lngAudit + 1, "One existing Created event covers all link writes");
        var lngFirst = Scalar(objDatabase, "SELECT min(skill_id) FROM contact_skills");
        objContact.Skills = "Beta"; objCrm.Save(objContact);
        Check(Text(objDatabase, "SELECT status FROM contact_skills WHERE skill_id=$id", ("$id", lngFirst)) == "Inactive"
            && Scalar(objDatabase, "SELECT count(*) FROM skills") == 2, "Removing a word inactivates its link and retains its word row");
        objContact.Skills = "alpha, Beta"; objCrm.Save(objContact);
        Check(Scalar(objDatabase, "SELECT count(*) FROM contact_skills") == 2 && Text(objDatabase, "SELECT status FROM contact_skills WHERE skill_id=$id", ("$id", lngFirst)) == "Active"
            && Text(objDatabase, "SELECT name FROM skills WHERE id=$id", ("$id", lngFirst)) == "Alpha", "Readding case-only text reactivates the same link and retains first spelling");
        objContact.Skills = "Équipe"; objCrm.Save(objContact);
        var lngUnicode = Scalar(objDatabase, "SELECT skill_id FROM contact_skills WHERE status='Active'");
        objContact.Skills = "équipe"; objCrm.Save(objContact);
        Check(Scalar(objDatabase, "SELECT skill_id FROM contact_skills WHERE status='Active'") == lngUnicode
            && Text(objDatabase, "SELECT name FROM skills WHERE id=$id", ("$id", lngUnicode)) == "Équipe", "Unicode case-only text reuses the word and link");

        // A redundant save must not issue word/link INSERTs or UPDATEs at all.
        Sql(objDatabase, "CREATE TRIGGER fixture_no_link_churn BEFORE UPDATE ON contact_skills BEGIN SELECT RAISE(ABORT,'PRIVATE_VALUE'); END; CREATE TRIGGER fixture_no_word_churn BEFORE INSERT ON skills BEGIN SELECT RAISE(ABORT,'PRIVATE_VALUE'); END");
        objCrm.Save(objContact);
        Check(Scalar(objDatabase, "SELECT count(*) FROM contact_skills WHERE status='Active'") == 1, "Unchanged links and words generate no redundant writes");
        Sql(objDatabase, "DROP TRIGGER fixture_no_link_churn; DROP TRIGGER fixture_no_word_churn");
        var strInvalid = new string('x', 65) + ",Bad\u0001Word";
        objContact.Skills = strInvalid; objCrm.Save(objContact);
        Check(Text(objDatabase, "SELECT skills FROM contacts WHERE id=1") == strInvalid && Text(objDatabase, "SELECT tags FROM contacts WHERE id=1") == "Shared"
            && Scalar(objDatabase, "SELECT count(*) FROM contact_skills WHERE status='Active'") == 0
            && Scalar(objDatabase, "SELECT count(*) FROM contact_tags WHERE status='Active'") == 1,
            "Invalid skills remain in source text without links while valid tags remain linked");
        const string strTagMessage = "Use up to 32 tags, each with 1–64 characters and no control characters.";
        foreach (var (strTags, strMessage, strCase) in new[]
        {
            (string.Join(',', Enumerable.Range(0, 33).Select(iValue => "FIXTURE_" + iValue)), strTagMessage, "count"),
            (new string('x', 65), strTagMessage, "per-tag length"),
            ("PRIVATE_TAG\u0001VALUE", strTagMessage, "control character"),
            (new string('x', 4097), "Keep contact tags under 4,096 characters.", "total length")
        })
        {
            var strRejectedBefore = Snapshot(objDatabase);
            var bRejected = false;
            // Change another parent field as well: rejection must preserve the entire stored row.
            objContact.FirstName = "Unsaved fixture"; objContact.strTags = strTags;
            try { objCrm.Save(objContact); }
            catch (ArgumentException objError) { bRejected = objError.Message == strMessage && objError.InnerException is null; }
            Check(bRejected && Snapshot(objDatabase) == strRejectedBefore,
                "Original contact tag " + strCase + " rejection preserves parent, text, words, links and audit");
        }
        objContact = objCrm.ContactRecord(objContact.Id);
        objContact.Skills = "Alpha\r\nBeta,,alpha,"; objContact.strTags = "Shared"; objCrm.Save(objContact);
        var objOrganization = new Organization { Name = "Fixture organization", Tags = "shared", Services = "SERVICE_ONLY" }; objCrm.Save(objOrganization);
        Check(Scalar(objDatabase, "SELECT count(*) FROM tags WHERE name=$name", ("$name", "Shared")) == 1
            && Scalar(objDatabase, "SELECT tag_id FROM contact_tags WHERE status='Active'") == Scalar(objDatabase, "SELECT tag_id FROM organization_tags WHERE status='Active'"),
            "Contacts and organizations share one tag identity");
        Check(Scalar(objDatabase, "SELECT count(*) FROM organization_tags WHERE status='Active'") == 1
            && Scalar(objDatabase, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('services','organization_services')") == 0,
            "Organization tags dual-write and services have no relational table");
        var strBefore = Snapshot(objDatabase);
        Sql(objDatabase, "CREATE TRIGGER fixture_link_failure BEFORE INSERT ON contact_tags BEGIN SELECT RAISE(ABORT,'PRIVATE_TRIGGER_VALUE'); END");
        var bDenied = false;
        objContact.Skills = "Replacement"; objContact.strTags = "ReplacementTag";
        try { objCrm.Save(objContact); }
        catch (InvalidOperationException objError)
        { bDenied = objError.Message == "Link synchronization failed: contacts, record ID 1." && objError.InnerException is null && !objError.ToString().Contains("PRIVATE_TRIGGER_VALUE", StringComparison.Ordinal); }
        Check(bDenied && Snapshot(objDatabase) == strBefore, "Mid-sync failure is redacted and rolls back parent, source, words, partial links and audit");
        Sql(objDatabase, "DROP TRIGGER fixture_link_failure");
        objContact = objCrm.ContactRecord(objContact.Id);

        var lngViewer = objUsers.CreateUser("link-viewer", secPassword, UserRole.Viewer); objUsers.SetCuiAuthorization(lngViewer, true);
        using var objViewer = objAuth.SignIn("link-viewer", secPassword);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        var strLinksBefore = LinkSnapshot(objDatabase);
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => objViewerCrm.Save(objContact), "Unauthorized saves cannot synchronize links");
        Check(LinkSnapshot(objDatabase) == strLinksBefore, "Denied save leaves every word and link unchanged");

        var strLifecycleBefore = LinkSnapshot(objDatabase);
        objCrm.SetRecordStatus(RecordKind.Contact, objContact.Id, objContact.Revision, RecordStatus.Inactive);
        var objCurrent = objCrm.ContactRecord(objContact.Id);
        objCrm.SetArchived(RecordKind.Contact, objCurrent.Id, objCurrent.Revision, true); objCurrent = objCrm.ContactRecord(objCurrent.Id);
        objCrm.SetArchived(RecordKind.Contact, objCurrent.Id, objCurrent.Revision, false);
        objCrm.SetArchived(RecordKind.Organization, objOrganization.Id, objOrganization.Revision, true); var objCurrentOrg = objCrm.OrganizationRecord(objOrganization.Id);
        objCrm.SetArchived(RecordKind.Organization, objCurrentOrg.Id, objCurrentOrg.Revision, false);
        Check(LinkSnapshot(objDatabase) == strLifecycleBefore, "Status, archive and restore leave links and word rows untouched");
        foreach (var strTable in new[] { "skills", "tags", "contact_skills", "contact_tags", "organization_tags" })
            SecurityPolicyChecks.Reject<SqliteException>(() => Sql(objDatabase, $"DELETE FROM {strTable}"), "Retained token DELETE remains rejected in " + strTable);
        var lstClean = objCrm.CheckTokenConsistency();
        try { Check(lstClean.Count == 0, "Consistency checker ignores invalid words and finds no mismatches after mixed saves"); }
        finally { CuiMemoryPolicy.Release(lstClean); }
        // Deliberate raw fixture corruption is not a production save path.
        Sql(objDatabase, "UPDATE contact_skills SET status='Inactive' WHERE contact_id=1 AND skill_id=(SELECT min(skill_id) FROM contact_skills)");
        Sql(objDatabase, "INSERT INTO skills(name) VALUES($name); INSERT INTO contact_skills(contact_id,skill_id,status) VALUES(1,(SELECT max(id) FROM skills),'Active')", ("$name", "EXTRA_FIXTURE"));
        Sql(objDatabase, "UPDATE organization_tags SET status='Inactive' WHERE organization_id=1");
        var lngAuditBeforeCheck = Scalar(objDatabase, "SELECT count(*) FROM audit_log");
        var lstMismatch = objCrm.CheckTokenConsistency();
        try
        {
            Check(lstMismatch.Count == 3 && lstMismatch.Count(objRow => objRow.strTable == "contacts" && objRow.lngRecordId == 1 && objRow.strColumn == "skills") == 2
                && lstMismatch.Single(objRow => objRow.strTable == "organizations").strKind == "MissingLink"
                && lstMismatch.Where(objRow => objRow.strTable == "contacts").Select(objRow => objRow.strKind).Order().SequenceEqual(new[] { "ExtraActiveLink", "MissingLink" }),
                "Consistency checker reports only table, record ID, column and mismatch kind");
            Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log") == lngAuditBeforeCheck + 1, "Consistency checker uses one protected read audit event");
        }
        finally { CuiMemoryPolicy.Release(lstMismatch); }
        var lstHidden = objViewerCrm.CheckTokenConsistency();
        try { Check(lstHidden.Count == 0, "Viewer cannot learn mismatches on inaccessible records under either record scope"); }
        finally { CuiMemoryPolicy.Release(lstHidden); }
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, true, false);
        var lstVisible = objViewerCrm.CheckTokenConsistency();
        try { Check(lstVisible.Count == 2, "Consistency checker exposes authorized mismatch metadata after a record grant"); }
        finally { CuiMemoryPolicy.Release(lstVisible); }
        // Raw current-schema fixtures intentionally bypass save to exercise a mismatch after batch one.
        Sql(objDatabase, "WITH RECURSIVE fixture_ids(id) AS (SELECT 2 UNION ALL SELECT id+1 FROM fixture_ids WHERE id<502) INSERT INTO organizations(id,name,email,phone,address,services,tags,status,created_at,updated_at) SELECT id,'Fixture','','','','',CASE WHEN id=502 THEN $tag ELSE '' END,'Active','fixture','fixture' FROM fixture_ids", ("$tag", "BATCH_FIXTURE"));
        var lstBatched = objCrm.CheckTokenConsistency();
        try { Check(lstBatched.Any(objRow => objRow.strTable == "organizations" && objRow.lngRecordId == 502 && objRow.strKind == "MissingLink"), "Consistency checker reaches authorized records beyond its first bounded batch"); }
        finally { CuiMemoryPolicy.Release(lstBatched); }
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action NOT IN ('SchemaUpgraded','AdministratorCreated','SignInSucceeded','CuiAuthorized','CuiPolicyGranted','Created','Updated','UserCreated','PermissionDenied','AccessVerified','AccessGranted','LifecycleChanged','Archived','Restored')") == 0,
            "Link operations retain defined audit events without per-word payloads");
        RedactionChecks();
    }

    private static void RedactionChecks()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL redaction policy fixture!");
        using var objAdmin = new AuthService(objDatabase).CreateAdministrator("redaction-owner", secPassword);
        new UserAdministrationService(objDatabase, objAdmin).SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        // Unique fictional canaries must never escape into diagnostics or the review document.
        var strSkill = "PRIVATE_SKILL_" + Guid.NewGuid().ToString("N");
        var strTag = "PRIVATE_TAG_" + Guid.NewGuid().ToString("N");
        var strRejectedTag = strTag + new string('x', 65);
        var strReplacement = "PRIVATE_REPLACEMENT_" + Guid.NewGuid().ToString("N");
        var arrCanaries = new[] { strSkill, strTag, strRejectedTag, strReplacement };
        var lstExceptions = new List<string>();
        var txtOriginalOut = Console.Out; var txtOriginalError = Console.Error;
        using var txtCapturedOut = new StringWriter(); using var txtCapturedError = new StringWriter();
        var bRejected = false; var bSyncRejected = false;
        try
        {
            Console.SetOut(txtCapturedOut); Console.SetError(txtCapturedError);
            var objContact = new Contact { FirstName = "Fixture", Skills = strSkill, strTags = strTag }; objCrm.Save(objContact);
            objContact.strTags = strRejectedTag;
            try { objCrm.Save(objContact); }
            catch (ArgumentException objError) { lstExceptions.Add(objError.ToString()); bRejected = true; }
            objContact.strTags = strReplacement;
            // The canary is generated locally from a fixed prefix plus GUID: no user SQL is composed.
            // SQLite trigger messages deliberately contain a word; synchronization must redact it.
            Sql(objDatabase, "CREATE TRIGGER fixture_redaction BEFORE INSERT ON contact_tags BEGIN SELECT RAISE(ABORT,'" + strSkill + "'); END");
            try { objCrm.Save(objContact); }
            catch (InvalidOperationException objError) { lstExceptions.Add(objError.ToString()); bSyncRejected = objError.InnerException is null; }
        }
        finally { Console.SetOut(txtOriginalOut); Console.SetError(txtOriginalError); }
        var strAudit = Text(objDatabase, "SELECT group_concat(COALESCE(user_id,'')||'|'||COALESCE(subject_user_id,'')||'|'||entity||'|'||entity_id||'|'||action||'|'||created_at||'|'||hex(signature),'~') FROM audit_log");
        var strReviewPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "SKILLS_TAGS_DUAL_WRITE_REVIEW.md");
        var bReviewExists = File.Exists(strReviewPath);
        var strReview = bReviewExists ? File.ReadAllText(strReviewPath) : "";
        Check(bRejected && bSyncRejected && !arrCanaries.Any(strValue => lstExceptions.Any(strError => strError.Contains(strValue, StringComparison.Ordinal))),
            "Validation and forced synchronization exceptions contain no skill or tag canary text");
        Check(!arrCanaries.Any(strValue => strAudit.Contains(strValue, StringComparison.Ordinal)), "Audit events contain no skill or tag canary text");
        Check(!arrCanaries.Any(strValue => txtCapturedOut.ToString().Contains(strValue, StringComparison.Ordinal) || txtCapturedError.ToString().Contains(strValue, StringComparison.Ordinal)),
            "Captured save and rejection output contains no skill or tag canary text");
        Check(bReviewExists && !arrCanaries.Any(strValue => strReview.Contains(strValue, StringComparison.Ordinal)), "Review document contains no skill or tag canary text");
        lstExceptions.Clear(); Array.Clear(arrCanaries);
    }

    private static string Snapshot(Database objDatabase) => LinkSnapshot(objDatabase)
        + Text(objDatabase, "SELECT group_concat(payload,'|') FROM (SELECT id||':'||revision||':'||hex(first_name)||':'||hex(last_name)||':'||hex(middle_names)||':'||hex(prefix)||':'||hex(suffix)||':'||hex(email)||':'||hex(phone)||':'||hex(address)||':'||hex(skills)||':'||hex(tags)||':'||hex(spouse)||':'||hex(children)||':'||hex(family)||':'||hex(friends)||':'||hex(twitter)||':'||hex(instagram)||':'||hex(snapchat)||':'||hex(facebook)||':'||status||':'||availability||':'||archived||':'||hex(created_at)||':'||hex(updated_at)||':'||COALESCE(organization_id,'')||':'||hex(classification)||':'||hex(cui_category)||':'||hex(distribution_statement)||':'||hex(controlling_office)||':'||hex(determination_date)||':'||hex(distribution_notice) AS payload FROM contacts ORDER BY id)")
        + Text(objDatabase, "SELECT group_concat(id||':'||hex(signature),'|') FROM audit_log");
    private static string LinkSnapshot(Database objDatabase)
    {
        var strResult = "";
        foreach (var (strTable, strProjection) in new[] { ("skills", "id||':'||hex(name)||':'||status"), ("tags", "id||':'||hex(name)||':'||status"),
            ("contact_skills", "contact_id||':'||skill_id||':'||status"), ("contact_tags", "contact_id||':'||tag_id||':'||status"), ("organization_tags", "organization_id||':'||tag_id||':'||status") })
            strResult += Text(objDatabase, $"SELECT group_concat(payload,'|') FROM (SELECT {strProjection} AS payload FROM {strTable} ORDER BY 1)") + ";";
        return strResult;
    }
    private static void Sql(Database objDatabase, string strSql, params (string, object?)[] arrParameters) => CuiCategoryChecks.Sql(objDatabase, strSql, arrParameters);
    private static long Scalar(Database objDatabase, string strSql, params (string, object?)[] arrParameters)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = Database.Command(dbConnection, strSql, arrParameters); return Convert.ToInt64(cmdSql.ExecuteScalar()); }
    private static string Text(Database objDatabase, string strSql, params (string, object?)[] arrParameters)
    { using var dbConnection = objDatabase.Open(); using var cmdSql = Database.Command(dbConnection, strSql, arrParameters); return Convert.ToString(cmdSql.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? ""; }
    private static void Check(bool bCondition, string strMessage) => SecurityPolicyChecks.Assert(bCondition, strMessage);
}
