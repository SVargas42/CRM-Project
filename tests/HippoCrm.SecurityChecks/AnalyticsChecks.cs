using System.Globalization;
using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class AnalyticsChecks
{
    internal static void Run()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL analytics fixture password!");
        var objAuth = new AuthService(objDatabase);
        using var objAdmin = objAuth.CreateAdministrator("analytics-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var dtNow = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var objFirst = new Organization { Name = "Fixture first" }; objCrm.Save(objFirst);
        var objSecond = new Organization { Name = "Fixture second" }; objCrm.Save(objSecond);
        var objHidden = new Organization { Name = "PRIVATE_ORGANIZATION_CANARY" }; objCrm.Save(objHidden);
        var objContact = new Contact { FirstName = "Fixture one", Skills = new string('s', 600), lstOrganizationIds = [objFirst.Id, objSecond.Id] }; objCrm.Save(objContact);
        var objNever = new Contact { FirstName = "Fixture never", Skills = "Fixture", lstOrganizationIds = [objHidden.Id] }; objCrm.Save(objNever);
        var objInactive = new Contact { FirstName = "Fixture inactive", lstOrganizationIds = [objFirst.Id] }; objCrm.Save(objInactive);
        void Note(long lngContact, DateTimeOffset dtAt) => CuiCategoryChecks.Sql(objDatabase,
            "INSERT INTO interactions(contact_id,body,created_at,user_id) VALUES($contact,'PRIVATE_NOTE_CANARY',$at,$user)",
            ("$contact", lngContact), ("$at", dtAt.ToString("O", CultureInfo.InvariantCulture)), ("$user", objAdmin.UserId));
        Note(objContact.Id, dtNow.AddHours(-24)); // Inclusive start, exclusive end: one window only.
        Note(objContact.Id, dtNow.AddHours(-24).AddTicks(-1));
        Note(objInactive.Id, dtNow.AddHours(-1));
        Note(objContact.Id, dtNow); // Exclusive current end.
        objCrm.AddContract(objContact.Id, new DateOnly(2026, 1, 1), 1m, "", "");
        objCrm.AddContract(objContact.Id, new DateOnly(2026, 1, 1), 2m, "", "");
        objCrm.AddContract(objNever.Id, new DateOnly(2026, 1, 1), 3m, "", "");
        objCrm.AddEmployment(objContact.Id, objFirst.Id, "", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        objCrm.AddEmployment(objContact.Id, objSecond.Id, "", new DateOnly(2025, 1, 1), null);
        objCrm.AddEmployment(objNever.Id, objFirst.Id, "", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        objCrm.AddEmployment(objNever.Id, objHidden.Id, "", new DateOnly(2025, 1, 1), null);
        objCrm.SetRecordStatus(RecordKind.Contact, objInactive.Id, objInactive.Revision, RecordStatus.Inactive);

        var objSnapshot = objCrm.AnalyticsAt(dtNow);
        try
        {
            Check(objSnapshot.LastContactedTotal == 3 && objSnapshot.LastContacted.Count == 3
                && objSnapshot.LastContacted[0].lngId == objNever.Id && objSnapshot.LastContacted[0].dtLastNoteUtc is null,
                "Analytics contacts occur once and never-contacted rows sort first");
            Check(objSnapshot.LastContacted.Single(objRow => objRow.lngId == objContact.Id).strOrganizations == "Fixture first, Fixture second"
                && objSnapshot.SkillRoster.Single(objRow => objRow.lngId == objContact.Id).strSkills.Length == 512,
                "Analytics membership names are sorted and skills are projected to 512 characters");
            var objCurrent = objSnapshot.PeriodSummaries.Single(objRow => objRow.strPeriod == "24 hours" && objRow.bActiveOnly && !objRow.bPrevious);
            Check(objCurrent.lngNotes == 1 && objCurrent.lngContacts == 1 && objCurrent.lngOrganizations == 2,
                "Analytics tick-precise half-open boundaries avoid duplication and multiple organizations count independently");
            Check(objSnapshot.PeriodSummaries.Single(objRow => objRow.strPeriod == "24 hours" && !objRow.bActiveOnly && !objRow.bPrevious).lngNotes == 2
                && objSnapshot.ContactTotals.Single(objRow => objRow.enStatus == RecordStatus.Inactive).lngCount == 1,
                "Analytics Active-only and all-authorized variants preserve contact lifecycle semantics");
            Check(objSnapshot.PeriodSummaries.Count == 12 && objSnapshot.PeriodSummaries.All(objRow=>!objRow.bPrevious)
                && objSnapshot.PeriodSummaries.Single(objRow=>objRow.strPeriod=="Year to date" && objRow.bActiveOnly).dtStartUtc==new DateTimeOffset(dtNow.Year,1,1,0,0,0,TimeSpan.Zero)
                && objSnapshot.PeriodSummaries.Single(objRow=>objRow.strPeriod=="6 months" && objRow.bActiveOnly).dtStartUtc==dtNow.AddMonths(-6),
                "Analytics returns current windows only, without prior-year or cross-period comparisons");
            Check(objSnapshot.DailyActivity.Count==14 && objSnapshot.DailyActivity.Where(objRow=>objRow.bActiveOnly).Sum(objRow=>objRow.lngNotes)==2
                && objSnapshot.DailyActivity.Where(objRow=>!objRow.bActiveOnly).Sum(objRow=>objRow.lngNotes)==3,
                "Seven-day UTC trend respects half-open bounds and explicit retained-record variants");
            Check(objSnapshot.LatestContracts.Single(objRow => objRow.lngId == objContact.Id).lngValueCents == 200 && objSnapshot.MovementTotal == 4,
                "Analytics latest contracts break date ties by ID and Movement retains multiple placements");
            Check(objSnapshot.OrganizationRoster.Single(objRow => objRow.lngId == objFirst.Id).lngActiveContacts == 1,
                "Analytics roster counts exclude Inactive contacts");
        }
        finally { CuiMemoryPolicy.Release(objSnapshot); }
        Check(objSnapshot.LastContacted.Count == 0 && objSnapshot.DailyActivity.Count == 0 && objSnapshot.PeriodSummaries.Count == 0 && objSnapshot.MovementTotal == 0,
            "Analytics shared release clears owned data and totals");
        objCrm.SetArchived(RecordKind.Organization, objSecond.Id, objSecond.Revision, true);
        var lngViewer = objUsers.CreateUser("analytics-viewer", secPassword, UserRole.Viewer); objUsers.SetCuiAuthorization(lngViewer, true);
        foreach (var lngContact in new[] { objContact.Id, objNever.Id }) objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, lngContact, true, false);
        foreach (var lngOrganization in new[] { objFirst.Id, objSecond.Id }) objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, lngOrganization, true, false);
        using var objViewer = objAuth.SignIn("analytics-viewer", secPassword);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        objSnapshot = objViewerCrm.AnalyticsAt(dtNow);
        try
        {
            Check(objSnapshot.LastContactedTotal == 2 && objSnapshot.OrganizationRosterTotal == 2
                && objSnapshot.OrganizationRoster.Single(objRow => objRow.lngId == objFirst.Id).lngActiveContacts == 1,
                "Analytics Viewer totals and roster counts exclude unauthorized contacts and organizations");
            Check(objSnapshot.LastContacted.Single(objRow => objRow.lngId == objNever.Id).strOrganizations == ""
                && objSnapshot.SkillRoster.Any(objRow => objRow.lngId == objNever.Id && objRow.strOrganizations == "")
                && objSnapshot.LatestContracts.Any(objRow => objRow.lngId == objNever.Id && objRow.strOrganizations == ""),
                "Analytics keeps visible contacts in each roster when their organization is hidden");
            Check(objSnapshot.Movement.Count == 2 && objSnapshot.Movement.All(objRow => objRow.lngContactId == objContact.Id)
                && objSnapshot.Movement.Any(objRow => objRow.bOrganizationArchived)
                && objSnapshot.LastContacted.Single(objRow => objRow.lngId == objContact.Id).strOrganizations == "Fixture first",
                "Analytics Movement excludes hidden placements from both rows and threshold while retaining authorized archived organizations");
            Check(!System.Text.Json.JsonSerializer.Serialize(objSnapshot).Contains(objHidden.Name, StringComparison.Ordinal),
                "Analytics snapshot contains no unauthorized organization name anywhere");
        }
        finally { objSnapshot.Release(); }
        var lngBefore = Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='AccessVerified'");
        objSnapshot = objViewerCrm.Analytics(); objSnapshot.Release();
        Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='AccessVerified'") == lngBefore + 1,
            "Direct Analytics call appends exactly one AccessVerified event for all queries");
        var lngUnapproved = objUsers.CreateUser("analytics-unapproved", secPassword, UserRole.Viewer);
        using var objUnapproved = objAuth.SignIn("analytics-unapproved", secPassword);
        var bNeutral = false;
        try { new CrmService(objDatabase, objUnapproved).Analytics(); }
        catch (InvalidOperationException objError) { bNeutral = objError.Message == "Analytics could not be loaded." && objError.InnerException is null; }
        Check(bNeutral, "Analytics denies missing CUI approval without exposing data or exception details");

        // Bounded synthetic rows are inserted only in this isolated memory fixture, never a user workspace.
        CuiCategoryChecks.Sql(objDatabase, """
            WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i+1 FROM n WHERE i<500)
            INSERT INTO contacts(id,first_name,last_name,email,phone,address,skills,spouse,children,family,friends,twitter,instagram,snapchat,facebook,status,availability,created_at,updated_at)
            SELECT 1000+i,'Fixture','','','','','Fixture','','','','','','','','','Active','Unknown','fixture','fixture' FROM n;
            WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i+1 FROM n WHERE i<500)
            INSERT INTO organizations(id,name,email,phone,address,services,tags,status,created_at,updated_at)
            SELECT 1000+i,'Fixture','','','','','','Active','fixture','fixture' FROM n;
            INSERT INTO contact_organizations(contact_id,organization_id,status) SELECT id,$first,'Active' FROM contacts WHERE id>=1000;
            INSERT INTO contact_organizations(contact_id,organization_id,status) SELECT id,$second,'Active' FROM contacts WHERE id>=1000;
            INSERT INTO contracts(contact_id,contract_date,value_cents,location,description) SELECT id,'2026-01-01',0,'','' FROM contacts WHERE id>=1000;
            INSERT INTO employment(contact_id,organization_id,role,start_date,end_date) SELECT id,$first,'','2023-01-01','2023-12-31' FROM contacts WHERE id>=1000;
            INSERT INTO employment(contact_id,organization_id,role,start_date,end_date) SELECT id,$second,'','2024-01-01','2024-12-31' FROM contacts WHERE id>=1000;
            """, ("$first", objFirst.Id), ("$second", objSecond.Id));
        objSnapshot = objCrm.AnalyticsAt(dtNow);
        try
        {
            Check(objSnapshot.LastContacted.Count == 500 && objSnapshot.LastContactedTotal == 504
                && objSnapshot.SkillRoster.Count == 500 && objSnapshot.SkillRosterTotal == 503
                && objSnapshot.LatestContracts.Count == 500 && objSnapshot.LatestContractsTotal == 503
                && objSnapshot.OrganizationRoster.Count == 500 && objSnapshot.OrganizationRosterTotal == 504
                && objSnapshot.OrganizationMembers.Count == 1000 && objSnapshot.OrganizationMembersTotal == 1006
                && objSnapshot.Movement.Count == 500 && objSnapshot.MovementTotal == 1006,
                "Every analytics list enforces its SQL cap independently of its true SQL total");
        }
        finally { objSnapshot.Release(); }
        // A late projection failure must surface only the neutral message; no partial result transfers.
        CuiCategoryChecks.Sql(objDatabase, "INSERT INTO contracts(contact_id,contract_date,value_cents,location,description) VALUES($contact,'PRIVATE_DATE_CANARY',0,'','')", ("$contact", objContact.Id));
        bNeutral = false;
        try { objCrm.AnalyticsAt(dtNow); }
        catch (InvalidOperationException objError) { bNeutral = objError.Message == "Analytics could not be loaded." && objError.InnerException is null; }
        Check(bNeutral, "Analytics late projection failure is neutral and cannot return partial data");
        PolicyChecks();
        VersionTwoChecks();
    }
    private static void VersionTwoChecks()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL analytics v2 password!");
        var objAuth = new AuthService(objDatabase);
        using var objAdmin = objAuth.CreateAdministrator("analytics-v2-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        var dtNow = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var objOrganization = new Organization { Name = "Fixture visible", Phone = "+1 (312) 555-0100", Email = "visible@example.test", Services = "Fixture service", Tags = "Fixture organization tag" };
        var objHiddenOrganization = new Organization { Name = "PRIVATE_V2_ORGANIZATION", Phone = "+1 (414) 555-0100", Email = "hidden-organization@example.test", Services = "PRIVATE_V2_SERVICES", Tags = "PRIVATE_V2_ORGANIZATION_TAG" };
        objCrm.Save(objOrganization); objCrm.Save(objHiddenOrganization);
        var objSkills = new Contact { FirstName = "Bea", LastName = "zulu", Skills = "Fixture skill", Phone = "+1 (312) 555-0101", Email = "skills@example.test", lstOrganizationIds = [objOrganization.Id] };
        var objTags = new Contact { FirstName = "Adam", LastName = "alpha", strTags = "Fixture contact tag", Phone = "+1 (312) 555-0102", Email = "tags@example.test", lstOrganizationIds = [objOrganization.Id] };
        var objRedacted = new Contact { FirstName = "adam", LastName = "ALPHA", Skills = "Fixture other skill", lstOrganizationIds = [objHiddenOrganization.Id] };
        var objNeither = new Contact { FirstName = "Fixture neither", lstOrganizationIds = [objOrganization.Id] };
        var objHiddenContact = new Contact { FirstName = "PRIVATE_V2_CONTACT", Phone = "+1 (415) 555-0100", Email = "hidden-contact@example.test", Skills = "PRIVATE_V2_SKILL", strTags = "PRIVATE_V2_CONTACT_TAG", lstOrganizationIds = [objOrganization.Id] };
        foreach (var objContact in new[] { objSkills, objTags, objRedacted, objNeither, objHiddenContact }) objCrm.Save(objContact);
        void Note(long lngContact, DateTimeOffset dtAt) => CuiCategoryChecks.Sql(objDatabase,
            "INSERT INTO interactions(contact_id,body,created_at,user_id) VALUES($contact,'FICTIONAL v2 note',$at,$user)",
            ("$contact", lngContact), ("$at", dtAt.ToString("O", CultureInfo.InvariantCulture)), ("$user", objAdmin.UserId));
        Note(objSkills.Id, dtNow.AddDays(-2)); Note(objSkills.Id, dtNow.AddDays(-1));
        Note(objTags.Id, dtNow.AddDays(-3)); Note(objRedacted.Id, dtNow.AddDays(-4)); Note(objHiddenContact.Id, dtNow.AddDays(-5));
        var lngViewer = objUsers.CreateUser("analytics-v2-viewer", secPassword, UserRole.Viewer); objUsers.SetCuiAuthorization(lngViewer, true);
        foreach (var objContact in new[] { objSkills, objTags, objRedacted, objNeither }) objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, true, false);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objOrganization.Id, true, false);
        using var objViewer = objAuth.SignIn("analytics-v2-viewer", secPassword);
        var objViewerCrm = new CrmService(objDatabase, objViewer);
        var objSnapshot = objViewerCrm.AnalyticsAt(dtNow);
        try
        {
            var objLast = objSnapshot.LastContacted.Single(objRow => objRow.lngId == objSkills.Id);
            Check(objLast.strPhone == objSkills.Phone && objLast.strEmail == objSkills.Email && objLast.strSkills == objSkills.Skills && objLast.strTags == "",
                "V2 LastContacted projects the saved normalized phone, email, skills and tags");
            Check(objSnapshot.RecentContacts.Select(objRow => objRow.lngId).SequenceEqual(new[] { objSkills.Id, objTags.Id, objRedacted.Id })
                && objSnapshot.RecentContactsTotal == 3 && objSnapshot.RecentContacts[0].dtLastNoteUtc == dtNow.AddDays(-1)
                && objSnapshot.RecentContacts[0] == objLast,
                "V2 RecentContacts returns one row per noted contact in descending last-note order with identical fields");
            Check(objSnapshot.ContactSearch.Select(objRow => objRow.lngId).SequenceEqual(new[] { objTags.Id, objRedacted.Id, objSkills.Id })
                && objSnapshot.ContactSearchTotal == 3 && objSnapshot.ContactSearch.Any(objRow => objRow.lngId == objTags.Id && objRow.strSkills == "" && objRow.strTags == objTags.strTags)
                && !objSnapshot.ContactSearch.Any(objRow => objRow.lngId == objNeither.Id),
                "V2 ContactSearch includes tags-only and skills-only contacts, excludes neither and orders case-insensitive last/first names with ID tie-break");
            Check(objSnapshot.LastContacted[0].lngId == objNeither.Id
                && objSnapshot.SkillRoster.Select(objRow => objRow.lngId).SequenceEqual(new[] { objSkills.Id, objRedacted.Id }),
                "V2 preserves never-contacted-first and unchanged SkillRoster order");
            var objRoster = objSnapshot.OrganizationRoster.Single();
            var objMember = objSnapshot.OrganizationMembers.Single(objRow => objRow.lngContactId == objSkills.Id);
            Check(objRoster.strPhone == objOrganization.Phone && objRoster.strEmail == objOrganization.Email && objRoster.strServices == objOrganization.Services
                && objRoster.strTags == objOrganization.Tags && objRoster.lngActiveContacts == 3
                && objMember.strPhone == objSkills.Phone && objMember.strEmail == objSkills.Email && objMember.strSkills == objSkills.Skills
                && objMember.strTags == "" && objMember.dtLastNoteUtc == dtNow.AddDays(-1),
                "V2 organization and member projections retain correct fields, last note and authorized active-member count");
            Check(new[] { objSnapshot.LastContacted, objSnapshot.RecentContacts, objSnapshot.ContactSearch }
                .All(lstRows => lstRows.Single(objRow => objRow.lngId == objRedacted.Id).strOrganizations == ""),
                "V2 visible contact retains all new lists with hidden organization names omitted");
            var strJson = System.Text.Json.JsonSerializer.Serialize(objSnapshot);
            var arrCanaries = new[] { objHiddenContact.FirstName, objHiddenContact.Phone, objHiddenContact.Email, objHiddenContact.Skills, objHiddenContact.strTags,
                objHiddenOrganization.Name, objHiddenOrganization.Phone, objHiddenOrganization.Email, objHiddenOrganization.Services, objHiddenOrganization.Tags };
            Check(arrCanaries.All(strCanary => !strJson.Contains(System.Text.Json.JsonSerializer.Serialize(strCanary)[1..^1], StringComparison.Ordinal)),
                "V2 Viewer snapshot discloses none of the ungranted contact or organization text fields in any list");
        }
        finally { CuiMemoryPolicy.Release(objSnapshot); }
        Check(Released(objSnapshot), "V2 shared Release clears every snapshot list and total including both new catalogs");

        var strPhones = string.Join('\n', Enumerable.Range(0, 40).Select(iValue => "+1 (312) 555-" + (1000 + iValue).ToString("0000", CultureInfo.InvariantCulture)));
        var strEmails = string.Join('\n', Enumerable.Range(0, 40).Select(iValue => "fixture-" + iValue.ToString(CultureInfo.InvariantCulture) + "@example.test"));
        var strTags = string.Join(',', Enumerable.Range(0, 12).Select(iValue => new string('t', 50) + iValue.ToString(CultureInfo.InvariantCulture)));
        var objLongOrganization = new Organization { Name = new string('n', 600), Phone = strPhones, Email = strEmails, Services = new string('v', 650), Tags = strTags };
        objCrm.Save(objLongOrganization);
        var objLongContact = new Contact { FirstName = new string('c', 600), LastName = "long", Phone = strPhones, Email = strEmails,
            Skills = new string('s', 600), strTags = strTags, lstOrganizationIds = [objLongOrganization.Id] };
        objCrm.Save(objLongContact); Note(objLongContact.Id, dtNow.AddHours(-1));
        objSnapshot = objCrm.AnalyticsAt(dtNow);
        try
        {
            Check(new[] { objSnapshot.LastContacted, objSnapshot.RecentContacts, objSnapshot.ContactSearch }
                .All(lstRows => lstRows.Single(objRow => objRow.lngId == objLongContact.Id) is var objProjected
                    && new[] { objProjected.strName, objProjected.strOrganizations, objProjected.strPhone, objProjected.strEmail, objProjected.strSkills, objProjected.strTags }.All(strValue => strValue.Length == 512)),
                "V2 all contact projection text is truncated to 512 characters before materialization");
            var objRoster = objSnapshot.OrganizationRoster.Single(objRow => objRow.lngId == objLongOrganization.Id);
            var objMember = objSnapshot.OrganizationMembers.Single(objRow => objRow.lngContactId == objLongContact.Id);
            Check(new[] { objRoster.strName, objRoster.strPhone, objRoster.strEmail, objRoster.strServices, objRoster.strTags,
                objMember.strName, objMember.strPhone, objMember.strEmail, objMember.strSkills, objMember.strTags }.All(strValue => strValue.Length == 512),
                "V2 roster and member free-text fields are SQL-bounded to 512 characters");
        }
        finally { objSnapshot.Release(); }
        // Bulk rows are restricted to this isolated memory fixture, using parameterized SQL.
        CuiCategoryChecks.Sql(objDatabase, """
            WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i+1 FROM n WHERE i<500)
            INSERT INTO contacts(id,first_name,last_name,email,phone,address,skills,spouse,children,family,friends,twitter,instagram,snapchat,facebook,status,availability,created_at,updated_at)
            SELECT 1000+i,'Bulk','bulk','','','','Fixture','','','','','','','','','Active','Unknown','fixture','fixture' FROM n;
            INSERT INTO interactions(contact_id,body,created_at,user_id) SELECT id,'FICTIONAL v2 bulk note',$at,$user FROM contacts WHERE id>=1000;
            """, ("$at", dtNow.AddHours(-12).ToString("O", CultureInfo.InvariantCulture)), ("$user", objAdmin.UserId));
        var lngBefore = Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='AccessVerified'");
        objSnapshot = objCrm.Analytics();
        try
        {
            Check(objSnapshot.RecentContacts.Count == 100 && objSnapshot.RecentContactsTotal == 506
                && objSnapshot.RecentContacts[0].lngId == objLongContact.Id
                && objSnapshot.RecentContacts.Skip(1).Select(objRow => objRow.lngId).SequenceEqual(Enumerable.Range(1000, 99).Select(iValue => (long)iValue)),
                "V2 RecentContacts enforces SQL cap 100 and true total with stable chronological and ID ordering");
            Check(objSnapshot.ContactSearch.Count == 500 && objSnapshot.ContactSearchTotal == 506
                // The Administrator also sees the fixture with an empty last name;
                // its sort position precedes the two case-insensitive name ties.
                && objSnapshot.ContactSearch.Take(3).Select(objRow => objRow.lngId).SequenceEqual(new[] { objHiddenContact.Id, objTags.Id, objRedacted.Id }),
                "V2 ContactSearch enforces SQL cap 500 and true total after scoped alphabetical ordering");
            Check(Scalar(objDatabase, "SELECT count(*) FROM audit_log WHERE action='AccessVerified'") == lngBefore + 1,
                "V2 Analytics still appends exactly one AccessVerified event for the bounded current-window queries");
        }
        finally { objSnapshot.Release(); }
        // Two placements qualify for Movement. The malformed second date fails only
        // after the test-only observation point and every new list has been populated.
        objCrm.AddEmployment(objSkills.Id, objOrganization.Id, "", new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31));
        CuiCategoryChecks.Sql(objDatabase, "INSERT INTO employment(contact_id,organization_id,role,start_date,end_date) VALUES($contact,$org,'','2021-01-01','PRIVATE_V2_DATE')",
            ("$contact", objSkills.Id), ("$org", objOrganization.Id));
        FailureProof("late projection");
        CuiCategoryChecks.Sql(objDatabase, "UPDATE employment SET end_date='2021-12-31' WHERE contact_id=$contact AND start_date='2021-01-01'", ("$contact", objSkills.Id));
        CuiCategoryChecks.Sql(objDatabase, "CREATE TRIGGER fixture_v2_deny_read BEFORE INSERT ON audit_log WHEN NEW.action='AccessVerified' BEGIN SELECT RAISE(ABORT,'PRIVATE_V2_AUDIT'); END");
        try { FailureProof("audit append"); }
        finally { CuiCategoryChecks.Sql(objDatabase, "DROP TRIGGER fixture_v2_deny_read"); }
        objDatabase.objCheckpoint.iFailWrite = objDatabase.objCheckpoint.iWrites + 1;
        try { FailureProof("commit prepare"); }
        finally { objDatabase.objCheckpoint.iFailWrite = 0; }
        objDatabase.objCheckpoint.iFailWrite = objDatabase.objCheckpoint.iWrites + 2;
        try { FailureProof("commit confirmation"); }
        finally { objDatabase.objCheckpoint.iFailWrite = 0; }
        objSnapshot = objCrm.AnalyticsAt(dtNow); objSnapshot.Release();
        Check(objDatabase.objCheckpoint.Read().objPrepared is null, "V2 trusted checkpoint recovery remains intact after failed confirmation");

        void FailureProof(string strStep)
        {
            AnalyticsSnapshot? objCaptured = null, objReturned = null;
            var bPopulated = false; var bNeutral = false;
            AnalyticsSnapshotProbe.actCapture = objValue =>
            {
                objCaptured = objValue;
                bPopulated = objValue.RecentContacts.Count == 100 && objValue.RecentContactsTotal == 506
                    && objValue.ContactSearch.Count == 500 && objValue.ContactSearchTotal == 506 && objValue.OrganizationMembers.Count > 0;
            };
            try
            {
                try { objReturned = objCrm.AnalyticsAt(dtNow); }
                catch (InvalidOperationException objError) { bNeutral = objError.Message == "Analytics could not be loaded." && objError.InnerException is null; }
                Check(bPopulated && objCaptured is not null && Released(objCaptured) && objReturned is null && bNeutral,
                    "V2 " + strStep + " failure clears the observed populated partial snapshot and never transfers it to the caller");
            }
            finally { AnalyticsSnapshotProbe.actCapture = null; objReturned?.Release(); objCaptured?.Release(); }
        }
    }
    private static bool Released(AnalyticsSnapshot objSnapshot) => typeof(AnalyticsSnapshot).GetProperties().All(objProperty =>
        objProperty.GetValue(objSnapshot) is not System.Collections.ICollection objList || objList.Count == 0)
        && typeof(AnalyticsSnapshot).GetProperties().Where(objProperty => objProperty.PropertyType == typeof(long))
            .All(objProperty => (long)objProperty.GetValue(objSnapshot)! == 0);
    private static void PolicyChecks()
    {
        using var objDatabase = new Database();
        using var secPassword = TestPasswords.Secret("FICTIONAL analytics policy password!");
        var objAuth = new AuthService(objDatabase);
        using var objAdmin = objAuth.CreateAdministrator("analytics-policy-owner", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin); objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        objUsers.SetCuiRecipientKind(objAdmin.UserId, CuiRecipientKind.DoD, "FICTIONAL affiliation");
        objUsers.SetCuiPolicyAccess(objAdmin.UserId, CuiPolicy.strCtiMarking, "B", true, "FICTIONAL approval");
        var objCrm = new CrmService(objDatabase, objAdmin);
        var objContact = CuiPolicy.Apply(new Contact { FirstName = "PRIVATE_CTI_CANARY", Skills = "PRIVATE_CTI_SKILL" }, CuiCategoryChecks.Designation("B"));
        var objOrganization = CuiPolicy.Apply(new Organization { Name = "PRIVATE_CTI_ORGANIZATION" }, CuiCategoryChecks.Designation("B"));
        objCrm.Save(objContact); objCrm.Save(objOrganization);
        var lngViewer = objUsers.CreateUser("analytics-policy-viewer", secPassword, UserRole.Viewer); objUsers.SetCuiAuthorization(lngViewer, true);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Contact, objContact.Id, true, false);
        objUsers.SetRecordAccess(lngViewer, RecordKind.Organization, objOrganization.Id, true, false);
        using var objViewer = objAuth.SignIn("analytics-policy-viewer", secPassword);
        var objSnapshot = new CrmService(objDatabase, objViewer).Analytics();
        try
        {
            Check(objSnapshot.LastContactedTotal == 0 && objSnapshot.SkillRosterTotal == 0 && objSnapshot.OrganizationRosterTotal == 0
                && objSnapshot.ContactTotals.Count == 0 && objSnapshot.OrganizationTotals.Count == 0
                && objSnapshot.DailyActivity.All(objRow=>objRow.lngNotes==0)
                && objSnapshot.PeriodSummaries.All(objRow => objRow.lngNotes == 0 && objRow.lngContacts == 0 && objRow.lngOrganizations == 0),
                "Analytics record grants cannot bypass CTI category, distribution and recipient approvals");
        }
        finally { objSnapshot.Release(); }
    }
    private static long Scalar(Database objDatabase, string strSql) => SecurityPolicyChecks.Scalar(objDatabase, strSql);
    private static void Check(bool bCondition, string strDescription) => SecurityPolicyChecks.Assert(bCondition, strDescription);
}
