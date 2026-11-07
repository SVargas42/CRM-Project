using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed partial class CrmService
{
    private readonly Database objDatabase;
    private readonly Session objSession;
    // Normalize optional name parts and both CRLF/LF encodings for cross-platform lookup.
    private const string strContactNameSearch = "replace(replace(replace(trim(c.prefix || ' ' || c.first_name || ' ' || replace(replace(replace(c.middle_names,char(13)||char(10),' '),char(13),' '),char(10),' ') || ' ' || c.last_name || ' ' || c.suffix),'  ',' '),'  ',' '),'  ',' ')";
    private static readonly string strContactScope = "($cui=1 AND c.classification='CUI' AND c.status IN ('Active','Inactive','Retired','Deceased')) AND " + CuiPolicy.SqlScope("c") + " AND (($admin=1 AND c.cui_category='CUI') OR EXISTS(SELECT 1 FROM contact_access a WHERE a.contact_id=c.id AND a.user_id=$user AND a.status='Active'))";
    private static readonly string strOrganizationScope = "($cui=1 AND o.classification='CUI' AND o.status IN ('Active','Inactive','Retired','Deceased')) AND " + CuiPolicy.SqlScope("o") + " AND (($admin=1 AND o.cui_category='CUI') OR EXISTS(SELECT 1 FROM organization_access a WHERE a.organization_id=o.id AND a.user_id=$user AND a.status='Active'))";

    public CrmService(Database objDatabase, Session objSession)
    {
        this.objDatabase = objDatabase; this.objSession = objSession;
        ReadOperation((dbConnection, txOperation, objIdentity) => true);
    }

    public UserRole Role => ReadOperation((dbConnection, txOperation, objIdentity) => objIdentity.enRole);
    public bool CanCreateRecords => ReadOperation((dbConnection, txOperation, objIdentity) => objIdentity.enRole == UserRole.Administrator && objIdentity.bCuiAuthorized);
    public bool CanWriteContact(long lngContactId) => CanWrite(RecordKind.Contact, lngContactId);
    public bool CanWriteOrganization(long lngOrganizationId) => CanWrite(RecordKind.Organization, lngOrganizationId);
    private bool CanWrite(RecordKind enKind, long lngRecordId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
        lngRecordId == 0 ? objIdentity.enRole == UserRole.Administrator && objIdentity.bCuiAuthorized : AccessControl.CanAccess(dbConnection, txOperation, objIdentity, enKind, lngRecordId, true));

    private T ReadOperation<T>(Func<SqliteConnection, SqliteTransaction, AuthorizedIdentity, T> fnRead)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        var objResult = fnRead(dbConnection, txOperation, objIdentity);
        var bTransferred = false;
        try
        {
        objSession.UseTokenHash(arrHash => true);
        // Reads return only after an audit write succeeds, satisfying the workspace's fail-secure policy.
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Sessions, 0, AuditAction.AccessVerified);
        SecurityAudit.Commit(dbConnection, txOperation); bTransferred = true; return objResult;
        }
        finally { if (!bTransferred) CuiMemoryPolicy.Release(objResult); }
    }

    private long Write(AuditEntity enEntity, long lngEntityId, AuditAction enAction,
        Func<SqliteConnection, SqliteTransaction, AuthorizedIdentity, long> fnChange)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        var lngResult = fnChange(dbConnection, txOperation, objIdentity);
        // Recheck after work without committing a denial inside an already-mutated transaction.
        objSession.UseTokenHash(arrHash => true);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, enEntity, lngEntityId == 0 ? lngResult : lngEntityId, enAction);
        SecurityAudit.Commit(dbConnection, txOperation); return lngResult;
    }

    private static SqliteCommand ScopedCommand(SqliteConnection dbConnection, SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity, string strSql, params (string strName, object? objValue)[] arrParameters)
    {
        var cmdQuery = Database.Command(dbConnection, strSql, arrParameters);
        cmdQuery.Transaction = txOperation;
        cmdQuery.Parameters.AddWithValue("$admin", objIdentity.enRole == UserRole.Administrator);
        cmdQuery.Parameters.AddWithValue("$user", objIdentity.lngUserId);
        cmdQuery.Parameters.AddWithValue("$cui", objIdentity.bCuiAuthorized);
        return cmdQuery;
    }

    public IReadOnlyList<Contact> Contacts(string strSearch = "", string strSkill = "", Availability? enAvailability = null, bool bArchived = false, RecordStatus? enStatus = RecordStatus.Active, int? iLimit = null, int iOffset = 0) =>
        ReadOperation((dbConnection, txOperation, objIdentity) =>
        {
            ValidateStatus(enStatus);
            if (iLimit is < 1 or > CuiMemoryPolicy.iPageSize) throw new ArgumentOutOfRangeException(nameof(iLimit));
            if (iOffset < 0) throw new ArgumentOutOfRangeException(nameof(iOffset));
            if (enAvailability.HasValue && !Enum.IsDefined(enAvailability.Value)) throw new ArgumentException("Select valid availability.");
            using var cmdQuery = ScopedCommand(dbConnection, txOperation, objIdentity, $"""
                SELECT {(iLimit.HasValue ? LookupColumns<Contact>("c") : "c.*")} FROM contacts c WHERE c.archived=$archived AND {strContactScope}
                AND ($status IS NULL OR c.status=$status)
                AND (instr(lower(first_name || ' ' || last_name),lower($search))>0 OR instr(lower({strContactNameSearch}),lower($search))>0 OR instr(lower(email),lower($search))>0 OR instr(lower(phone),lower($search))>0)
                AND instr(lower(skills),lower($skill))>0 AND ($availability IS NULL OR availability=$availability)
                ORDER BY last_name COLLATE NOCASE,first_name COLLATE NOCASE,c.id LIMIT $limit OFFSET $offset
                """, ("$limit", iLimit ?? -1), ("$offset", iOffset), ("$archived", bArchived), ("$search", strSearch?.Trim() ?? ""), ("$skill", strSkill?.Trim() ?? ""), ("$availability", enAvailability?.ToString()), ("$status", enStatus?.ToString()));
            using var rdrContacts = cmdQuery.ExecuteReader(); var lstContacts = new List<Contact>();
            while (rdrContacts.Read()) lstContacts.Add(ReadRecord<Contact>(rdrContacts));
            rdrContacts.Close();
            var dicContacts = lstContacts.ToDictionary(objContact => objContact.Id);
            using var cmdOrganizations = ScopedCommand(dbConnection, txOperation, objIdentity,
                $"SELECT co.contact_id,co.organization_id FROM contact_organizations co JOIN contacts c ON c.id=co.contact_id JOIN organizations o ON o.id=co.organization_id WHERE co.status='Active' AND o.archived=0 AND {strContactScope} AND {strOrganizationScope} ORDER BY co.organization_id");
            using var rdrOrganizations = cmdOrganizations.ExecuteReader();
            while (rdrOrganizations.Read())
                if (dicContacts.TryGetValue(rdrOrganizations.GetInt64(0), out var objContact))
                    objContact.lstOrganizationIds.Add(rdrOrganizations.GetInt64(1));
            foreach (var objContact in lstContacts)
                objContact.lngOrganizationId = objContact.lstOrganizationIds.Count == 0 ? null : objContact.lstOrganizationIds[0];
            return (IReadOnlyList<Contact>)lstContacts;
        });

    public bool HasMoreContacts(string strSearch = "", string strSkill = "", Availability? enAvailability = null,
        bool bArchived = false, RecordStatus? enStatus = RecordStatus.Active, int iOffset = 0) =>
        ReadOperation((dbConnection, txOperation, objIdentity) =>
        {
            ValidateStatus(enStatus);
            if (iOffset < 0) throw new ArgumentOutOfRangeException(nameof(iOffset));
            if (enAvailability.HasValue && !Enum.IsDefined(enAvailability.Value)) throw new ArgumentException("Select valid availability.");
            using var cmdQuery = ScopedCommand(dbConnection, txOperation, objIdentity, $"""
                SELECT EXISTS(SELECT 1 FROM contacts c WHERE c.archived=$archived AND {strContactScope}
                    AND ($status IS NULL OR c.status=$status)
                    AND (instr(lower(first_name || ' ' || last_name),lower($search))>0 OR instr(lower({strContactNameSearch}),lower($search))>0 OR instr(lower(email),lower($search))>0 OR instr(lower(phone),lower($search))>0)
                    AND instr(lower(skills),lower($skill))>0 AND ($availability IS NULL OR availability=$availability)
                    LIMIT 1 OFFSET $offset)
                """, ("$offset", iOffset), ("$archived", bArchived), ("$search", strSearch?.Trim() ?? ""),
                ("$skill", strSkill?.Trim() ?? ""), ("$availability", enAvailability?.ToString()), ("$status", enStatus?.ToString()));
            cmdQuery.Transaction = txOperation;
            return Convert.ToInt64(cmdQuery.ExecuteScalar()) != 0;
        });

    // Retain the validated legacy status argument for callers; organization visibility uses archived only.
    public IReadOnlyList<Organization> Organizations(string strSearch = "", bool bArchived = false, RecordStatus? enStatus = RecordStatus.Active, int? iLimit = null, int iOffset = 0) =>
        ReadOperation((dbConnection, txOperation, objIdentity) =>
        {
            ValidateStatus(enStatus);
            if (iLimit is < 1 or > CuiMemoryPolicy.iPageSize) throw new ArgumentOutOfRangeException(nameof(iLimit));
            if (iOffset < 0) throw new ArgumentOutOfRangeException(nameof(iOffset));
            using var cmdQuery = ScopedCommand(dbConnection, txOperation, objIdentity,
                $"SELECT {(iLimit.HasValue ? LookupColumns<Organization>("o") : "o.*")} FROM organizations o WHERE archived=$archived AND {strOrganizationScope} AND instr(lower(name || ' ' || services || ' ' || tags),lower($search))>0 ORDER BY name COLLATE NOCASE,o.id LIMIT $limit OFFSET $offset",
                ("$limit", iLimit ?? -1), ("$offset", iOffset), ("$archived", bArchived), ("$search", strSearch?.Trim() ?? ""), ("$status", enStatus?.ToString()));
            using var rdrOrganizations = cmdQuery.ExecuteReader(); var lstOrganizations = new List<Organization>();
            while (rdrOrganizations.Read()) lstOrganizations.Add(ReadRecord<Organization>(rdrOrganizations));
            return (IReadOnlyList<Organization>)lstOrganizations;
        });

    public bool HasMoreOrganizations(string strSearch = "", bool bArchived = false, int iOffset = 0) =>
        ReadOperation((dbConnection, txOperation, objIdentity) =>
        {
            if (iOffset < 0) throw new ArgumentOutOfRangeException(nameof(iOffset));
            using var cmdQuery = ScopedCommand(dbConnection, txOperation, objIdentity,
                $"SELECT EXISTS(SELECT 1 FROM organizations o WHERE o.archived=$archived AND {strOrganizationScope} AND instr(lower(name || ' ' || services || ' ' || tags),lower($search))>0 LIMIT 1 OFFSET $offset)",
                ("$offset", iOffset), ("$archived", bArchived), ("$search", strSearch?.Trim() ?? ""));
            cmdQuery.Transaction = txOperation;
            return Convert.ToInt64(cmdQuery.ExecuteScalar()) != 0;
        });

    public IReadOnlyList<Organization> AllOrganizations(RecordStatus? enStatus = RecordStatus.Active, bool bNamesOnly = false) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        ValidateStatus(enStatus);
        using var cmdQuery = ScopedCommand(dbConnection, txOperation, objIdentity,
            $"SELECT {(bNamesOnly ? "o.id,substr(o.name,1,512) AS name,o.cui_category,o.distribution_statement,o.controlling_office,o.determination_date,o.distribution_notice" : "o.*")} FROM organizations o WHERE o.archived=0 AND {strOrganizationScope} ORDER BY name COLLATE NOCASE", ("$status", enStatus?.ToString()));
        using var rdrOrganizations = cmdQuery.ExecuteReader(); var lstOrganizations = new List<Organization>();
        while (rdrOrganizations.Read()) lstOrganizations.Add(bNamesOnly ? CuiPolicy.Apply(new Organization { Id = rdrOrganizations.GetInt64(0), Name = rdrOrganizations.GetString(1) }, CuiPolicy.Read(rdrOrganizations, 2)) : ReadRecord<Organization>(rdrOrganizations));
        return (IReadOnlyList<Organization>)lstOrganizations;
    });

    public TokenChoiceCatalog TokenChoices(RecordStatus enStatus = RecordStatus.Active) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        ValidateStatus(enStatus);
        var setTags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var setSkills = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var setServices = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        static void AddValues(ISet<string> setValues, string? strValues)
        {
            foreach (var strValue in (strValues ?? "").Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                setValues.Add(strValue);
        }

        using (var cmdContacts = ScopedCommand(dbConnection, txOperation, objIdentity,
            $"SELECT c.tags,c.skills FROM contacts c WHERE c.archived=0 AND c.status=$status AND {strContactScope}", ("$status", enStatus.ToString())))
        using (var rdrContacts = cmdContacts.ExecuteReader())
            while (rdrContacts.Read()) { AddValues(setTags, rdrContacts.GetString(0)); AddValues(setSkills, rdrContacts.GetString(1)); }

        using (var cmdOrganizations = ScopedCommand(dbConnection, txOperation, objIdentity,
            $"SELECT o.tags,o.services FROM organizations o WHERE o.archived=0 AND {strOrganizationScope}", ("$status", enStatus.ToString())))
        using (var rdrOrganizations = cmdOrganizations.ExecuteReader())
            while (rdrOrganizations.Read()) { AddValues(setTags, rdrOrganizations.GetString(0)); AddValues(setServices, rdrOrganizations.GetString(1)); }

        // Keep both scoped queries in this one audited read; only tags cross record types.
        return new TokenChoiceCatalog(setTags.ToArray(), setSkills.ToArray(), setServices.ToArray());
    });

    // Contact detail screens need these related lists together. One authorized, audited read
    // avoids reopening and re-keying the encrypted database once for every panel.
    public ContactEditorData ContactEditor(long lngContactId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, false);
        var bCanEdit = AccessControl.CanAccess(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, true);

        using var cmdOrganizations = ScopedCommand(dbConnection, txOperation, objIdentity,
            $"SELECT o.id,substr(o.name,1,512) AS name,o.cui_category,o.distribution_statement,o.controlling_office,o.determination_date,o.distribution_notice FROM organizations o WHERE o.archived=0 AND {strOrganizationScope} ORDER BY name COLLATE NOCASE");
        using var rdrOrganizations = cmdOrganizations.ExecuteReader(); var lstOrganizations = new List<Organization>();
        while (rdrOrganizations.Read()) lstOrganizations.Add(CuiPolicy.Apply(new Organization { Id = rdrOrganizations.GetInt64(0), Name = rdrOrganizations.GetString(1) }, CuiPolicy.Read(rdrOrganizations, 2)));
        rdrOrganizations.Close();

        using var cmdInteractions = Database.Command(dbConnection,
            "SELECT i.id,i.body,i.created_at,u.username FROM interactions i JOIN users u ON u.id=i.user_id WHERE i.contact_id=$id ORDER BY i.created_at DESC,i.id DESC",
            ("$id", lngContactId));
        cmdInteractions.Transaction = txOperation;
        using var rdrInteractions = cmdInteractions.ExecuteReader(); var lstInteractions = new List<Interaction>();
        while (rdrInteractions.Read()) lstInteractions.Add(new(rdrInteractions.GetInt64(0), lngContactId,
            rdrInteractions.GetString(1), DateTimeOffset.Parse(rdrInteractions.GetString(2), CultureInfo.InvariantCulture), rdrInteractions.GetString(3)));
        rdrInteractions.Close();

        using var cmdEmployment = ScopedCommand(dbConnection, txOperation, objIdentity,
            $"SELECT e.id,e.organization_id,o.name,e.role,e.start_date,e.end_date FROM employment e JOIN organizations o ON e.organization_id=o.id WHERE e.contact_id=$id AND {strOrganizationScope} ORDER BY start_date DESC,e.id DESC",
            ("$id", lngContactId));
        using var rdrEmployment = cmdEmployment.ExecuteReader(); var lstEmployment = new List<Employment>();
        while (rdrEmployment.Read()) lstEmployment.Add(new(rdrEmployment.GetInt64(0), lngContactId, rdrEmployment.GetInt64(1),
            rdrEmployment.GetString(2), rdrEmployment.GetString(3), DateOnly.Parse(rdrEmployment.GetString(4), CultureInfo.InvariantCulture),
            rdrEmployment.IsDBNull(5) ? null : DateOnly.Parse(rdrEmployment.GetString(5), CultureInfo.InvariantCulture)));
        rdrEmployment.Close();

        using var cmdContracts = Database.Command(dbConnection,
            "SELECT id,contract_date,value_cents,location,description FROM contracts WHERE contact_id=$id ORDER BY contract_date DESC,id DESC",
            ("$id", lngContactId));
        cmdContracts.Transaction = txOperation;
        using var rdrContracts = cmdContracts.ExecuteReader(); var lstContracts = new List<Contract>();
        while (rdrContracts.Read()) lstContracts.Add(new(rdrContracts.GetInt64(0), lngContactId,
            DateOnly.Parse(rdrContracts.GetString(1), CultureInfo.InvariantCulture), rdrContracts.GetInt64(2) / 100m,
            rdrContracts.GetString(3), rdrContracts.GetString(4)));

        return new ContactEditorData(bCanEdit, lstOrganizations, lstInteractions, lstEmployment, lstContracts);
    });

    public long Save(Contact objContact, DuplicateDecision? objDecision = null)
    {
        ArgumentNullException.ThrowIfNull(objContact);
        CuiPolicy.ValidateRecord(objContact);
        if (string.IsNullOrWhiteSpace(objContact.FirstName) && string.IsNullOrWhiteSpace(objContact.LastName))
            throw new ArgumentException("Enter a first or last name.");
        if (!Enum.IsDefined(objContact.Status) || !Enum.IsDefined(objContact.Availability)) throw new ArgumentException("Select a valid status and availability.");
        objContact.FirstName = (objContact.FirstName ?? "").Trim(); objContact.LastName = (objContact.LastName ?? "").Trim();
        objContact.Prefix = (objContact.Prefix ?? "").Trim(); objContact.Suffix = (objContact.Suffix ?? "").Trim();
        var arrMiddleNames = objContact.lstMiddleNameValues.Select(strName => strName.Trim()).Where(strName => strName.Length != 0).ToArray();
        if (arrMiddleNames.Any(strName => strName.Any(char.IsControl))) throw new ArgumentException("Middle names must be entered one per field.");
        objContact.MiddleNames = arrMiddleNames.Length == 0 ? "" : string.Join(Environment.NewLine, arrMiddleNames) + Environment.NewLine;

        objContact.strTags ??= "";
        if (objContact.strTags.Length > 4096) throw new ArgumentException("Keep contact tags under 4,096 characters.");
        var arrTags = objContact.lstTags.ToArray();
        if (arrTags.Length > 32 || arrTags.Any(strTag => strTag.Length > 64 || strTag.Any(char.IsControl)))
            throw new ArgumentException("Use up to 32 tags, each with 1–64 characters and no control characters.");
        objContact.strTags = string.Join(", ", arrTags);
        // Retain compatibility with existing callers that still provide a single assignment.
        if (objContact.lstOrganizationIds.Count == 0 && objContact.lngOrganizationId is { } lngLegacyOrganization)
            objContact.lstOrganizationIds.Add(lngLegacyOrganization);
        if (objContact.lstOrganizationIds.Any(lngOrganization => lngOrganization <= 0))
            throw new ArgumentException("Select valid organizations.");
        objContact.lstOrganizationIds = objContact.lstOrganizationIds.Distinct().Order().ToList();
        objContact.lngOrganizationId = objContact.lstOrganizationIds.Count == 0 ? null : objContact.lstOrganizationIds[0];
        var bExisting = objContact.Id != 0;
        var strEmail = ContactDetailsValidation.Emails(objContact.Email);
        var strPhone = ContactDetailsValidation.Phones(objContact.Phone);
        objContact.Email = strEmail; objContact.Phone = strPhone;
        objContact.Id = SaveRecord(RecordKind.Contact, objContact, objContact.Id, objContact.Revision, objDecision);
        if (bExisting) objContact.Revision++; return objContact.Id;
    }
    public long Save(Organization objOrganization, DuplicateDecision? objDecision = null)
    {
        ArgumentNullException.ThrowIfNull(objOrganization);
        CuiPolicy.ValidateRecord(objOrganization);
        if (string.IsNullOrWhiteSpace(objOrganization.Name)) throw new ArgumentException("Enter an organization name.");
        if (!Enum.IsDefined(objOrganization.Status)) throw new ArgumentException("Select a valid status.");
        objOrganization.Name = objOrganization.Name.Trim();
        var bExisting = objOrganization.Id != 0;
        var strEmail = ContactDetailsValidation.Emails(objOrganization.Email);
        var strPhone = ContactDetailsValidation.Phones(objOrganization.Phone);
        objOrganization.Email = strEmail; objOrganization.Phone = strPhone;
        objOrganization.Id = SaveRecord(RecordKind.Organization, objOrganization, objOrganization.Id,objOrganization.Revision, objDecision);
        if(bExisting) objOrganization.Revision++; return objOrganization.Id;
    }
    private long SaveRecord<T>(RecordKind enKind, T objRecord, long lngRecordId,int iRevision, DuplicateDecision? objDecision)
    {
        var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
        var enEntity = enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations;
        return Write(enEntity, lngRecordId, lngRecordId == 0 ? AuditAction.Created : AuditAction.Updated, (dbConnection, txOperation, objIdentity) =>
        {
            AccessControl.RequireCui(dbConnection, txOperation, objIdentity);
            if (lngRecordId == 0) AccessControl.Administrator(dbConnection, txOperation, objIdentity);
            else AccessControl.Require(dbConnection, txOperation, objIdentity, enKind, lngRecordId, true);
            var objPolicy = ((CuiProtectedRecord)(object)objRecord!).objCuiDesignation;
            AccessControl.RequirePolicy(dbConnection, txOperation, objIdentity, objPolicy);
            if (lngRecordId != 0 && objPolicy != CuiPolicy.ReadRecord(dbConnection, txOperation, enKind, lngRecordId))
                throw new ArgumentException("Use the authorized CUI designation command to change record protection.");
            ValidateDuplicateDecision(dbConnection, txOperation, objIdentity, enKind, objRecord, objDecision);
            List<long>? lstPersistedOrganizations = null;
            var bArchived = enKind == RecordKind.Contact ? ((Contact)(object)objRecord!).Archived : ((Organization)(object)objRecord!).Archived;
            var enStatus = enKind == RecordKind.Contact ? ((Contact)(object)objRecord!).Status : ((Organization)(object)objRecord!).Status;
            if (lngRecordId == 0 && bArchived) throw new ArgumentException("Save the record before archiving it.");
            if (lngRecordId != 0)
            {
                using var cmdArchived = new SqliteCommand($"SELECT archived FROM {strTable} WHERE id=$id", dbConnection, txOperation);
                cmdArchived.Parameters.AddWithValue("$id", lngRecordId);
                if (Convert.ToInt64(cmdArchived.ExecuteScalar(), CultureInfo.InvariantCulture) != (bArchived ? 1 : 0))
                    throw new ArgumentException("Use the archive or restore command to change record retention.");
                if (bArchived && enStatus == RecordStatus.Active) throw new ArgumentException("Restore visibility before activating this record.");
            }
            if (enKind == RecordKind.Contact)
            {
                var objContact = (Contact)(object)objRecord!;
                using var cmdPrevious = Database.Command(dbConnection, "SELECT organization_id FROM contact_organizations WHERE contact_id=$id AND status='Active'", ("$id", lngRecordId));
                cmdPrevious.Transaction = txOperation;
                var setPreviousOrganizations = new HashSet<long>();
                using (var rdrPrevious = cmdPrevious.ExecuteReader())
                    while (rdrPrevious.Read()) setPreviousOrganizations.Add(rdrPrevious.GetInt64(0));
                // Preserve hidden links server-side; never disclose their IDs to
                // an editor or interpret their absence as permission to remove.
                lstPersistedOrganizations = objContact.lstOrganizationIds.ToList();
                foreach (var lngPrevious in setPreviousOrganizations)
                    if (!lstPersistedOrganizations.Contains(lngPrevious) && !AccessControl.CanAccess(dbConnection, txOperation, objIdentity, RecordKind.Organization, lngPrevious, false))
                        lstPersistedOrganizations.Add(lngPrevious);
                // Preserve unchanged restricted links; verify authorization for every changed assignment.
                foreach (var lngAssigned in lstPersistedOrganizations.Except(setPreviousOrganizations)
                    .Concat(setPreviousOrganizations.Except(lstPersistedOrganizations)))
                    AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Organization, lngAssigned, false);
            }
            var arrFields = typeof(T).GetProperties().Where(objProperty => objProperty.CanWrite && objProperty.Name is not ("Id" or "Revision")
                && objProperty.Name != nameof(Contact.lstOrganizationIds)
                && (lngRecordId == 0 || objProperty.Name is not ("Status" or "Archived"))).ToArray();
            var arrColumns = arrFields.Select(objProperty => Column(objProperty.Name)).ToArray();
            var strSql = lngRecordId == 0
                ? $"INSERT INTO {strTable}({string.Join(',', arrColumns)},created_at,updated_at) VALUES({string.Join(',', arrColumns.Select(strColumn => "$" + strColumn))},$now,$now) RETURNING id"
                : $"UPDATE {strTable} SET {string.Join(',', arrColumns.Select(strColumn => strColumn + "=$" + strColumn))},updated_at=$now WHERE id=$id AND revision=$revision RETURNING id";
            if (lngRecordId != 0)
                RetentionStatements.SetBusinessRecordStatus(dbConnection, txOperation, enKind, lngRecordId, iRevision, enStatus);
            using var cmdSave = new SqliteCommand(strSql, dbConnection, txOperation);
            cmdSave.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            cmdSave.Parameters.AddWithValue("$id", lngRecordId); cmdSave.Parameters.AddWithValue("$revision", iRevision + 1);
            foreach (var objProperty in arrFields)
            {
                var objValue = objProperty.GetValue(objRecord);
                if (objProperty.Name == nameof(Contact.lngOrganizationId))
                    objValue = lstPersistedOrganizations is { Count: > 0 } ? lstPersistedOrganizations[0] : null;
                cmdSave.Parameters.AddWithValue("$" + Column(objProperty.Name), objProperty.PropertyType.IsEnum ? objValue!.ToString()!
                    : objProperty.Name == nameof(Contact.lngOrganizationId) ? objValue ?? DBNull.Value : objValue ?? "");
            }
            var lngSavedId = Convert.ToInt64(cmdSave.ExecuteScalar() ?? throw new ArgumentException("This record changed in another editor. Reload it before saving."), CultureInfo.InvariantCulture);
            if (lngRecordId == 0) CuiPolicy.GrantDesignatorAccess(dbConnection, txOperation, objIdentity, enKind, lngSavedId, objPolicy);
            if (enKind == RecordKind.Contact)
            {
                RetentionStatements.ReplaceContactOrganizations(dbConnection, txOperation, lngSavedId,
                    lstPersistedOrganizations!);
            }
            var objSavedContact = objRecord as Contact;
            TokenLinks.Synchronize(dbConnection, txOperation, enKind, lngSavedId,
                objSavedContact?.Skills, objSavedContact is not null ? objSavedContact.strTags : ((Organization)(object)objRecord!).Tags);
            RecordDuplicateDecision(dbConnection, txOperation, objIdentity, enKind, lngSavedId, objDecision);
            return lngSavedId;
        });
    }

    public void SetRecordStatus(RecordKind enKind, long lngRecordId, int iExpectedRevision, RecordStatus enStatus)
    {
        if (!Enum.IsDefined(enKind)) throw new ArgumentException("Select a valid record type.");
        ValidateStatus(enStatus);
        Write(enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations, lngRecordId, AuditAction.LifecycleChanged,
            (dbConnection, txOperation, objIdentity) =>
            {
                AccessControl.Require(dbConnection, txOperation, objIdentity, enKind, lngRecordId, true);
                var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
                using var cmdArchived = new SqliteCommand($"SELECT archived FROM {strTable} WHERE id=$id", dbConnection, txOperation);
                cmdArchived.Parameters.AddWithValue("$id", lngRecordId);
                if (enStatus == RecordStatus.Active && Convert.ToInt64(cmdArchived.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new ArgumentException("Restore visibility before activating this record.");
                RetentionStatements.SetBusinessRecordStatus(dbConnection, txOperation, enKind, lngRecordId, iExpectedRevision, enStatus);
                return lngRecordId;
            });
    }

    public void SetArchived(RecordKind enKind, long lngRecordId, int iExpectedRevision, bool bArchived)
    {
        if (!Enum.IsDefined(enKind)) throw new ArgumentException("Select a valid record type.");
        Write(enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations, lngRecordId,
            bArchived ? AuditAction.Archived : AuditAction.Restored, (dbConnection, txOperation, objIdentity) =>
            {
                AccessControl.Require(dbConnection, txOperation, objIdentity, enKind, lngRecordId, true);
                var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
                using var cmdCurrent = new SqliteCommand($"SELECT status FROM {strTable} WHERE id=$id", dbConnection, txOperation);
                cmdCurrent.Parameters.AddWithValue("$id", lngRecordId);
                if (cmdCurrent.ExecuteScalar() is not string strStatus || !Enum.TryParse<RecordStatus>(strStatus, out var enStatus) || !Enum.IsDefined(enStatus))
                    throw new ArgumentException("The record status could not be verified.");
                if (bArchived && enStatus == RecordStatus.Active) enStatus = RecordStatus.Inactive;
                RetentionStatements.SetBusinessRecordStatus(dbConnection, txOperation, enKind, lngRecordId, iExpectedRevision, enStatus, bArchived);
                return lngRecordId;
            });
    }

    private static void ValidateStatus(RecordStatus? enStatus)
    {
        if (enStatus.HasValue && !Enum.IsDefined(enStatus.Value)) throw new ArgumentException("Select a valid lifecycle status.");
    }

    private static void RequireActiveContact(SqliteConnection dbConnection, SqliteTransaction txOperation, long lngContactId)
    {
        using var cmdActive = Database.Command(dbConnection, "SELECT count(*) FROM contacts WHERE id=$id AND archived=0 AND status='Active'", ("$id", lngContactId));
        cmdActive.Transaction = txOperation;
        if ((long)cmdActive.ExecuteScalar()! != 1) throw new ArgumentException("Restore this contact before adding records.");
    }

    public void AddInteraction(long lngContactId, string strBody)
    {
        if (string.IsNullOrWhiteSpace(strBody) || strBody.Length > 20000) throw new ArgumentException("Enter a note with 1–20,000 characters.");
        Write(AuditEntity.Interactions, 0, AuditAction.Created, (dbConnection, txOperation, objIdentity) =>
        {
            AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, true);
            RequireActiveContact(dbConnection, txOperation, lngContactId);
            using var cmdInsert = Database.Command(dbConnection,
                "INSERT INTO interactions(contact_id,body,created_at,user_id) VALUES($contact,$body,$now,$user) RETURNING id",
                ("$contact", lngContactId), ("$body", strBody.Trim()), ("$now", Database.Now), ("$user", objIdentity.lngUserId));
            cmdInsert.Transaction = txOperation; return (long)cmdInsert.ExecuteScalar()!;
        });
    }
    public IReadOnlyList<Interaction> Interactions(long lngContactId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, false);
        using var cmdRead = Database.Command(dbConnection, "SELECT i.id,i.body,i.created_at,u.username FROM interactions i JOIN users u ON u.id=i.user_id WHERE contact_id=$id ORDER BY i.created_at DESC,i.id DESC", ("$id", lngContactId));
        cmdRead.Transaction = txOperation;
        using var rdrNotes = cmdRead.ExecuteReader(); var lstNotes = new List<Interaction>();
        while (rdrNotes.Read()) lstNotes.Add(new(rdrNotes.GetInt64(0), lngContactId, rdrNotes.GetString(1), DateTimeOffset.Parse(rdrNotes.GetString(2), CultureInfo.InvariantCulture), rdrNotes.GetString(3)));
        return (IReadOnlyList<Interaction>)lstNotes;
    });

    public void AddEmployment(long lngContactId, long lngOrganizationId, string strRole, DateOnly dtStart, DateOnly? dtEnd)
    {
        if (dtEnd < dtStart) throw new ArgumentException("Employment end must be on or after its start.");
        Write(AuditEntity.Employment, 0, AuditAction.Created, (dbConnection, txOperation, objIdentity) =>
        {
            AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, true);
            AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Organization, lngOrganizationId, false);
            RequireActiveContact(dbConnection, txOperation, lngContactId);
            using var cmdOrganization = Database.Command(dbConnection, "SELECT count(*) FROM organizations WHERE id=$id AND archived=0", ("$id", lngOrganizationId));
            cmdOrganization.Transaction = txOperation;
            if ((long)cmdOrganization.ExecuteScalar()! != 1) throw new ArgumentException("Select an active organization.");
            using var cmdOverlap = Database.Command(dbConnection, "SELECT count(*) FROM employment WHERE contact_id=$id AND start_date <= $end AND COALESCE(end_date,'9999-12-31') >= $start",
                ("$id", lngContactId), ("$start", Date(dtStart)), ("$end", dtEnd.HasValue ? Date(dtEnd.Value) : "9999-12-31"));
            cmdOverlap.Transaction = txOperation;
            if ((long)cmdOverlap.ExecuteScalar()! > 0) throw new ArgumentException("Employment dates overlap an existing entry.");
            using var cmdInsert = Database.Command(dbConnection,
                "INSERT INTO employment(contact_id,organization_id,role,start_date,end_date) VALUES($contact,$org,$role,$start,$end) RETURNING id",
                ("$contact", lngContactId), ("$org", lngOrganizationId), ("$role", strRole?.Trim() ?? ""), ("$start", Date(dtStart)), ("$end", dtEnd.HasValue ? Date(dtEnd.Value) : null));
            cmdInsert.Transaction = txOperation; return (long)cmdInsert.ExecuteScalar()!;
        });
    }
    public void EndEmployment(long lngEmploymentId, DateOnly dtEnd) => Write(AuditEntity.Employment, lngEmploymentId, AuditAction.Ended, (dbConnection, txOperation, objIdentity) =>
    {
        using var cmdParent = Database.Command(dbConnection, "SELECT contact_id,organization_id FROM employment WHERE id=$id", ("$id", lngEmploymentId));
        cmdParent.Transaction = txOperation;
        using var rdrParent = cmdParent.ExecuteReader();
        if (!rdrParent.Read()) { rdrParent.Close(); AccessControl.Deny(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Employment, lngEmploymentId); }
        var lngContactId = rdrParent.GetInt64(0); var lngOrganizationId = rdrParent.GetInt64(1); rdrParent.Close();
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, true);
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Organization, lngOrganizationId, false);
        using var cmdEnd = Database.Command(dbConnection, "UPDATE employment SET end_date=$end WHERE id=$id AND end_date IS NULL AND start_date<=$end AND contact_id IN (SELECT id FROM contacts WHERE archived=0 AND status='Active') RETURNING id", ("$end", Date(dtEnd)), ("$id", lngEmploymentId));
        cmdEnd.Transaction = txOperation; return (long)(cmdEnd.ExecuteScalar() ?? throw new ArgumentException("Select a current placement and a valid end date."));
    });

    public IReadOnlyList<Employment> EmploymentHistory(long lngContactId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, false);
        using var cmdHistory = ScopedCommand(dbConnection, txOperation, objIdentity,
            $"SELECT e.id,e.organization_id,o.name,e.role,e.start_date,e.end_date FROM employment e JOIN organizations o ON e.organization_id=o.id WHERE e.contact_id=$id AND {strOrganizationScope} ORDER BY start_date DESC,e.id DESC", ("$id", lngContactId));
        using var rdrHistory = cmdHistory.ExecuteReader(); var lstHistory = new List<Employment>();
        while (rdrHistory.Read()) lstHistory.Add(new(rdrHistory.GetInt64(0), lngContactId, rdrHistory.GetInt64(1), rdrHistory.GetString(2), rdrHistory.GetString(3), DateOnly.Parse(rdrHistory.GetString(4), CultureInfo.InvariantCulture), rdrHistory.IsDBNull(5) ? null : DateOnly.Parse(rdrHistory.GetString(5), CultureInfo.InvariantCulture)));
        return (IReadOnlyList<Employment>)lstHistory;
    });

    public void AddContract(long lngContactId, DateOnly dtDate, decimal decValue, string strLocation, string strDescription)
    {
        if (decValue < 0 || decValue > 1_000_000_000_000m || decimal.Round(decValue, 2) != decValue) throw new ArgumentException("Enter a contract value from 0 to 1 trillion with up to two decimal places.");
        Write(AuditEntity.Contracts, 0, AuditAction.Created, (dbConnection, txOperation, objIdentity) =>
        {
            AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, true);
            RequireActiveContact(dbConnection, txOperation, lngContactId);
            using var cmdInsert = Database.Command(dbConnection, "INSERT INTO contracts(contact_id,contract_date,value_cents,location,description) VALUES($contact,$date,$value,$location,$description) RETURNING id",
                ("$contact", lngContactId), ("$date", Date(dtDate)), ("$value", (long)(decValue * 100)), ("$location", strLocation?.Trim() ?? ""), ("$description", strDescription?.Trim() ?? ""));
            cmdInsert.Transaction = txOperation; return (long)cmdInsert.ExecuteScalar()!;
        });
    }
    public IReadOnlyList<Contract> Contracts(long lngContactId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, false);
        using var cmdRead = Database.Command(dbConnection, "SELECT id,contract_date,value_cents,location,description FROM contracts WHERE contact_id=$id ORDER BY contract_date DESC,id DESC", ("$id", lngContactId));
        cmdRead.Transaction = txOperation;
        using var rdrContracts = cmdRead.ExecuteReader(); var lstContracts = new List<Contract>();
        while (rdrContracts.Read()) lstContracts.Add(new(rdrContracts.GetInt64(0), lngContactId, DateOnly.Parse(rdrContracts.GetString(1), CultureInfo.InvariantCulture), rdrContracts.GetInt64(2) / 100m, rdrContracts.GetString(3), rdrContracts.GetString(4)));
        return (IReadOnlyList<Contract>)lstContracts;
    });

    public Dashboard Dashboard(RecordStatus enStatus = RecordStatus.Active) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        ValidateStatus(enStatus);
        int Count(string strSql, params (string strName, object? objValue)[] arrParameters)
        {
            using var cmdCount = ScopedCommand(dbConnection, txOperation, objIdentity, strSql, arrParameters);
            cmdCount.Parameters.AddWithValue("$lifecycle", enStatus.ToString());
            return Convert.ToInt32(cmdCount.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        var dtNow = DateTimeOffset.UtcNow;
        var strCutoff = dtNow.AddDays(-30).ToString("O", CultureInfo.InvariantCulture);
        var lstWeeks = new List<WeeklyActivity>();
        for (var iWeek = 3; iWeek >= 0; iWeek--)
        {
            var dtStart = new DateTimeOffset(dtNow.UtcDateTime.Date.AddDays(-7 * (iWeek + 1) + 1), TimeSpan.Zero);
            var dtEnd = dtStart.AddDays(7);
            lstWeeks.Add(new(dtStart.ToString("MMM d", CultureInfo.InvariantCulture), Count($"SELECT count(*) FROM interactions i JOIN contacts c ON c.id=i.contact_id WHERE c.archived=0 AND c.status=$lifecycle AND {strContactScope} AND i.created_at >= $start AND i.created_at < $end",
                ("$start", dtStart.ToString("O", CultureInfo.InvariantCulture)), ("$end", dtEnd.ToString("O", CultureInfo.InvariantCulture)))));
        }
        return new Dashboard(Count($"SELECT count(*) FROM contacts c WHERE archived=0 AND c.status=$lifecycle AND {strContactScope}"),
            Count($"SELECT count(*) FROM organizations o WHERE archived=0 AND {strOrganizationScope}"),
            Count($"SELECT count(*) FROM contacts c WHERE archived=0 AND status=$lifecycle AND availability='Available' AND {strContactScope}"),
            Count($"SELECT count(*) FROM contacts c WHERE archived=1 AND {strContactScope}"),
            Count($"SELECT count(*) FROM interactions i JOIN contacts c ON c.id=i.contact_id WHERE c.archived=0 AND c.status=$lifecycle AND {strContactScope} AND i.created_at >= $cutoff", ("$cutoff", strCutoff)),
            Count($"SELECT count(*) FROM contacts c WHERE archived=0 AND status=$lifecycle AND {strContactScope} AND NOT EXISTS(SELECT 1 FROM interactions i WHERE i.contact_id=c.id AND i.created_at >= $cutoff)", ("$cutoff", strCutoff)), lstWeeks);
    });

    private static string Date(DateOnly dtDate) => dtDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Column(string strName) => strName == nameof(Contact.lngOrganizationId) ? "organization_id"
        : CuiPolicy.arrPolicyProperties.Contains(strName) ? CuiPolicy.PolicyColumn(strName)
        : strName == nameof(Contact.strClassification) ? "classification"
        : strName == nameof(Contact.strTags) ? "tags"
        : Regex.Replace(strName, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
    private static T ReadRecord<T>(SqliteDataReader rdrRecord) where T : new()
    {
        var objRecord = new T();
        foreach (var objProperty in typeof(T).GetProperties().Where(objProperty => objProperty.CanWrite
            && objProperty.Name != nameof(Contact.lstOrganizationIds)))
        {
            var objValue = rdrRecord[Column(objProperty.Name)];
            objProperty.SetValue(objRecord, objProperty.PropertyType.IsEnum ? Enum.Parse(objProperty.PropertyType, (string)objValue)
                : objProperty.PropertyType == typeof(bool) ? Convert.ToInt64(objValue, CultureInfo.InvariantCulture) != 0
                : objValue is DBNull ? null
                : Convert.ChangeType(objValue, Nullable.GetUnderlyingType(objProperty.PropertyType) ?? objProperty.PropertyType, CultureInfo.InvariantCulture));
        }
        return objRecord;
    }
}
