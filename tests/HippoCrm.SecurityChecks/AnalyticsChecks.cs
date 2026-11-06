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
            var objPrevious = objSnapshot.PeriodSummaries.Single(objRow => objRow.strPeriod == "24 hours" && objRow.bActiveOnly && objRow.bPrevious);
            Check(objCurrent.lngNotes == 1 && objPrevious.lngNotes == 1 && objCurrent.lngContacts == 1 && objCurrent.lngOrganizations == 2,
                "Analytics tick-precise half-open boundaries avoid duplication and multiple organizations count independently");
            Check(objSnapshot.PeriodSummaries.Single(objRow => objRow.strPeriod == "24 hours" && !objRow.bActiveOnly && !objRow.bPrevious).lngNotes == 2
                && objSnapshot.ContactTotals.Single(objRow => objRow.enStatus == RecordStatus.Inactive).lngCount == 1,
                "Analytics Active-only and all-authorized variants preserve contact lifecycle semantics");
            Check(objSnapshot.PeriodSummaries.Count == 24
                && objSnapshot.PeriodSummaries.Single(objRow => objRow.strPeriod == "Year to date" && objRow.bPrevious && objRow.bActiveOnly).dtStartUtc == new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
                && objSnapshot.PeriodSummaries.Single(objRow => objRow.strPeriod == "Year to date" && objRow.bPrevious && objRow.bActiveOnly).dtEndUtc == dtNow.AddYears(-1)
                && objSnapshot.PeriodSummaries.Single(objRow => objRow.strPeriod == "6 months" && !objRow.bPrevious && objRow.bActiveOnly).dtStartUtc == dtNow.AddMonths(-6),
                "Analytics captures all variants with calendar month and prior-year YTD boundaries");
            Check(objSnapshot.LatestContracts.Single(objRow => objRow.lngId == objContact.Id).lngValueCents == 200 && objSnapshot.MovementTotal == 4,
                "Analytics latest contracts break date ties by ID and Movement retains multiple placements");
            Check(objSnapshot.OrganizationRoster.Single(objRow => objRow.lngId == objFirst.Id).lngActiveContacts == 1,
                "Analytics roster counts exclude Inactive contacts");
        }
        finally { CuiMemoryPolicy.Release(objSnapshot); }
        Check(objSnapshot.LastContacted.Count == 0 && objSnapshot.PeriodSummaries.Count == 0 && objSnapshot.MovementTotal == 0,
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
    }
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
                && objSnapshot.PeriodSummaries.All(objRow => objRow.lngNotes == 0 && objRow.lngContacts == 0 && objRow.lngOrganizations == 0),
                "Analytics record grants cannot bypass CTI category, distribution and recipient approvals");
        }
        finally { objSnapshot.Release(); }
    }
    private static long Scalar(Database objDatabase, string strSql) => SecurityPolicyChecks.Scalar(objDatabase, strSql);
    private static void Check(bool bCondition, string strDescription) => SecurityPolicyChecks.Assert(bCondition, strDescription);
}
