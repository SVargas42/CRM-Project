using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed class CrmService
{
    private readonly Database objDatabase;
    private readonly Session objSession;
    private const string strContactScope = "($admin=1 OR EXISTS(SELECT 1 FROM contact_access a WHERE a.contact_id=c.id AND a.user_id=$user))";
    private const string strOrganizationScope = "($admin=1 OR EXISTS(SELECT 1 FROM organization_access a WHERE a.organization_id=o.id AND a.user_id=$user))";

    public CrmService(Database objDatabase, Session objSession)
    {
        this.objDatabase = objDatabase; this.objSession = objSession;
        ReadOperation((dbConnection, txOperation, objIdentity) => true);
    }

    public UserRole Role => ReadOperation((dbConnection, txOperation, objIdentity) => objIdentity.enRole);
    public bool CanWriteContact(long lngContactId) => CanWrite(RecordKind.Contact, lngContactId);
    public bool CanWriteOrganization(long lngOrganizationId) => CanWrite(RecordKind.Organization, lngOrganizationId);
    private bool CanWrite(RecordKind enKind, long lngRecordId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
        lngRecordId == 0 ? objIdentity.enRole == UserRole.Administrator : AccessControl.CanAccess(dbConnection, txOperation, objIdentity, enKind, lngRecordId, true));

    private T ReadOperation<T>(Func<SqliteConnection, SqliteTransaction, AuthorizedIdentity, T> fnRead)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        var objResult = fnRead(dbConnection, txOperation, objIdentity);
        // Reads return only after an audit write succeeds, satisfying the workspace's fail-secure policy.
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, AuditEntity.Sessions, 0, AuditAction.AccessVerified);
        txOperation.Commit(); return objResult;
    }

    private long Write(AuditEntity enEntity, long lngEntityId, AuditAction enAction,
        Func<SqliteConnection, SqliteTransaction, AuthorizedIdentity, long> fnChange)
    {
        using var dbConnection = objDatabase.Open();
        using var txOperation = dbConnection.BeginTransaction(false);
        var objIdentity = AccessControl.Identity(dbConnection, txOperation, objSession);
        var lngResult = fnChange(dbConnection, txOperation, objIdentity);
        SecurityAudit.Append(dbConnection, txOperation, objIdentity.lngUserId, enEntity, lngEntityId == 0 ? lngResult : lngEntityId, enAction);
        txOperation.Commit(); return lngResult;
    }

    private static SqliteCommand ScopedCommand(SqliteConnection dbConnection, SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity, string strSql, params (string strName, object? objValue)[] arrParameters)
    {
        var cmdQuery = Database.Command(dbConnection, strSql, arrParameters);
        cmdQuery.Transaction = txOperation;
        cmdQuery.Parameters.AddWithValue("$admin", objIdentity.enRole == UserRole.Administrator);
        cmdQuery.Parameters.AddWithValue("$user", objIdentity.lngUserId);
        return cmdQuery;
    }

    public IReadOnlyList<Contact> Contacts(string strSearch = "", string strSkill = "", Availability? enAvailability = null, bool bArchived = false) =>
        ReadOperation((dbConnection, txOperation, objIdentity) =>
        {
            using var cmdQuery = ScopedCommand(dbConnection, txOperation, objIdentity, $"""
                SELECT c.* FROM contacts c WHERE c.archived=$archived AND {strContactScope}
                AND (instr(lower(first_name || ' ' || last_name),lower($search))>0 OR instr(lower(email),lower($search))>0 OR instr(lower(phone),lower($search))>0)
                AND instr(lower(skills),lower($skill))>0 AND ($availability IS NULL OR availability=$availability)
                ORDER BY last_name COLLATE NOCASE,first_name COLLATE NOCASE
                """, ("$archived", bArchived), ("$search", strSearch?.Trim() ?? ""), ("$skill", strSkill?.Trim() ?? ""), ("$availability", enAvailability?.ToString()));
            using var rdrContacts = cmdQuery.ExecuteReader(); var lstContacts = new List<Contact>();
            while (rdrContacts.Read()) lstContacts.Add(ReadRecord<Contact>(rdrContacts));
            return (IReadOnlyList<Contact>)lstContacts;
        });

    public IReadOnlyList<Organization> Organizations(string strSearch = "", bool bArchived = false) =>
        ReadOperation((dbConnection, txOperation, objIdentity) =>
        {
            using var cmdQuery = ScopedCommand(dbConnection, txOperation, objIdentity,
                $"SELECT o.* FROM organizations o WHERE archived=$archived AND {strOrganizationScope} AND instr(lower(name || ' ' || services || ' ' || tags),lower($search))>0 ORDER BY name COLLATE NOCASE",
                ("$archived", bArchived), ("$search", strSearch?.Trim() ?? ""));
            using var rdrOrganizations = cmdQuery.ExecuteReader(); var lstOrganizations = new List<Organization>();
            while (rdrOrganizations.Read()) lstOrganizations.Add(ReadRecord<Organization>(rdrOrganizations));
            return (IReadOnlyList<Organization>)lstOrganizations;
        });

    public long Save(Contact objContact)
    {
        ArgumentNullException.ThrowIfNull(objContact);
        if (string.IsNullOrWhiteSpace(objContact.DisplayName)) throw new ArgumentException("Enter a contact name.");
        if (!Enum.IsDefined(objContact.Status) || !Enum.IsDefined(objContact.Availability)) throw new ArgumentException("Select a valid status and availability.");
        objContact.FirstName = objContact.FirstName.Trim(); objContact.LastName = objContact.LastName.Trim();
        var bExisting = objContact.Id != 0;
        objContact.Id = SaveRecord(RecordKind.Contact, objContact, objContact.Id,objContact.Revision);
        if(bExisting) objContact.Revision++; return objContact.Id;
    }
    public long Save(Organization objOrganization)
    {
        ArgumentNullException.ThrowIfNull(objOrganization);
        if (string.IsNullOrWhiteSpace(objOrganization.Name)) throw new ArgumentException("Enter an organization name.");
        if (!Enum.IsDefined(objOrganization.Status)) throw new ArgumentException("Select a valid status.");
        objOrganization.Name = objOrganization.Name.Trim();
        var bExisting = objOrganization.Id != 0;
        objOrganization.Id = SaveRecord(RecordKind.Organization, objOrganization, objOrganization.Id,objOrganization.Revision);
        if(bExisting) objOrganization.Revision++; return objOrganization.Id;
    }
    private long SaveRecord<T>(RecordKind enKind, T objRecord, long lngRecordId,int iRevision)
    {
        var strTable = enKind == RecordKind.Contact ? "contacts" : "organizations";
        var enEntity = enKind == RecordKind.Contact ? AuditEntity.Contacts : AuditEntity.Organizations;
        return Write(enEntity, lngRecordId, lngRecordId == 0 ? AuditAction.Created : AuditAction.Updated, (dbConnection, txOperation, objIdentity) =>
        {
            if (lngRecordId == 0) AccessControl.Administrator(dbConnection, txOperation, objIdentity);
            else AccessControl.Require(dbConnection, txOperation, objIdentity, enKind, lngRecordId, true);
            var arrFields = typeof(T).GetProperties().Where(objProperty => objProperty.CanWrite && objProperty.Name is not ("Id" or "Revision")).ToArray();
            var arrColumns = arrFields.Select(objProperty => Column(objProperty.Name)).ToArray();
            var strSql = lngRecordId == 0
                ? $"INSERT INTO {strTable}({string.Join(',', arrColumns)},created_at,updated_at) VALUES({string.Join(',', arrColumns.Select(strColumn => "$" + strColumn))},$now,$now) RETURNING id"
                : $"UPDATE {strTable} SET {string.Join(',', arrColumns.Select(strColumn => strColumn + "=$" + strColumn))},updated_at=$now,revision=revision+1 WHERE id=$id AND revision=$revision RETURNING id";
            using var cmdSave = Database.Command(dbConnection, strSql, ("$now", Database.Now), ("$id", lngRecordId),("$revision",iRevision));
            cmdSave.Transaction = txOperation;
            foreach (var objProperty in arrFields)
            {
                var objValue = objProperty.GetValue(objRecord);
                cmdSave.Parameters.AddWithValue("$" + Column(objProperty.Name), objProperty.PropertyType.IsEnum ? objValue!.ToString()! : objValue ?? "");
            }
            return Convert.ToInt64(cmdSave.ExecuteScalar() ?? throw new ArgumentException("This record changed in another editor. Reload it before saving."), CultureInfo.InvariantCulture);
        });
    }

    private static void RequireActiveContact(SqliteConnection dbConnection, SqliteTransaction txOperation, long lngContactId)
    {
        using var cmdActive = Database.Command(dbConnection, "SELECT count(*) FROM contacts WHERE id=$id AND archived=0", ("$id", lngContactId));
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
        using var cmdEnd = Database.Command(dbConnection, "UPDATE employment SET end_date=$end WHERE id=$id AND end_date IS NULL AND start_date<=$end AND contact_id IN (SELECT id FROM contacts WHERE archived=0) RETURNING id", ("$end", Date(dtEnd)), ("$id", lngEmploymentId));
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

    public Dashboard Dashboard() => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        int Count(string strSql, params (string strName, object? objValue)[] arrParameters)
        {
            using var cmdCount = ScopedCommand(dbConnection, txOperation, objIdentity, strSql, arrParameters);
            return Convert.ToInt32(cmdCount.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        var dtNow = DateTimeOffset.UtcNow;
        var strCutoff = dtNow.AddDays(-30).ToString("O", CultureInfo.InvariantCulture);
        var lstWeeks = new List<WeeklyActivity>();
        for (var iWeek = 3; iWeek >= 0; iWeek--)
        {
            var dtStart = new DateTimeOffset(dtNow.UtcDateTime.Date.AddDays(-7 * (iWeek + 1) + 1), TimeSpan.Zero);
            var dtEnd = dtStart.AddDays(7);
            lstWeeks.Add(new(dtStart.ToString("MMM d", CultureInfo.InvariantCulture), Count($"SELECT count(*) FROM interactions i JOIN contacts c ON c.id=i.contact_id WHERE {strContactScope} AND i.created_at >= $start AND i.created_at < $end",
                ("$start", dtStart.ToString("O", CultureInfo.InvariantCulture)), ("$end", dtEnd.ToString("O", CultureInfo.InvariantCulture)))));
        }
        return new Dashboard(Count($"SELECT count(*) FROM contacts c WHERE archived=0 AND {strContactScope}"),
            Count($"SELECT count(*) FROM organizations o WHERE archived=0 AND {strOrganizationScope}"),
            Count($"SELECT count(*) FROM contacts c WHERE archived=0 AND status='Active' AND availability='Available' AND {strContactScope}"),
            Count($"SELECT count(*) FROM contacts c WHERE archived=1 AND {strContactScope}"),
            Count($"SELECT count(*) FROM interactions i JOIN contacts c ON c.id=i.contact_id WHERE {strContactScope} AND i.created_at >= $cutoff", ("$cutoff", strCutoff)),
            Count($"SELECT count(*) FROM contacts c WHERE archived=0 AND status='Active' AND {strContactScope} AND NOT EXISTS(SELECT 1 FROM interactions i WHERE i.contact_id=c.id AND i.created_at >= $cutoff)", ("$cutoff", strCutoff)), lstWeeks);
    });

    private static string Date(DateOnly dtDate) => dtDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Column(string strName) => Regex.Replace(strName, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
    private static T ReadRecord<T>(SqliteDataReader rdrRecord) where T : new()
    {
        var objRecord = new T();
        foreach (var objProperty in typeof(T).GetProperties().Where(objProperty => objProperty.CanWrite))
        {
            var objValue = rdrRecord[Column(objProperty.Name)];
            objProperty.SetValue(objRecord, objProperty.PropertyType.IsEnum ? Enum.Parse(objProperty.PropertyType, (string)objValue)
                : objProperty.PropertyType == typeof(bool) ? Convert.ToInt64(objValue, CultureInfo.InvariantCulture) != 0
                : Convert.ChangeType(objValue, objProperty.PropertyType, CultureInfo.InvariantCulture));
        }
        return objRecord;
    }
}
