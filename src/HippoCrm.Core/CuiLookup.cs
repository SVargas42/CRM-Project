using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed record CuiLookupPage(IReadOnlyList<Contact> lstContacts, IReadOnlyList<Organization> lstOrganizations);

public sealed partial class CrmService
{
    // Fixed model properties only, never caller-supplied identifiers. Lookup rows
    // must not hydrate family/social fields or silently save shortened text.
    private static string LookupColumns<T>(string strAlias) => string.Join(',', typeof(T).GetProperties()
        .Where(objProperty => objProperty.CanWrite && objProperty.Name != nameof(Contact.lstOrganizationIds))
        .Select(objProperty =>
        {
            var strColumn = Column(objProperty.Name);
            if (objProperty.Name == nameof(Contact.lngOrganizationId)) return "NULL AS " + strColumn;
            if (objProperty.PropertyType != typeof(string)) return strAlias + "." + strColumn;
            var bVisible = CuiPolicy.arrPolicyProperties.Contains(objProperty.Name) || objProperty.Name is nameof(Contact.strClassification) or nameof(Contact.strTags) or nameof(Contact.FirstName)
                or nameof(Contact.MiddleNames) or nameof(Contact.LastName) or nameof(Contact.Prefix) or nameof(Contact.Suffix)
                or nameof(Contact.Email) or nameof(Contact.Phone) or nameof(Contact.Address) or nameof(Contact.Skills)
                or nameof(Organization.Name) or nameof(Organization.Services) or nameof(Organization.Tags);
            return (CuiPolicy.arrPolicyProperties.Contains(objProperty.Name) ? $"{strAlias}.{strColumn}"
                : bVisible ? $"substr({strAlias}.{strColumn},1,{CuiMemoryPolicy.iMaximumFieldLength})" : "''") + " AS " + strColumn;
        }));

    public Organization OrganizationRecord(long lngOrganizationId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Organization, lngOrganizationId, false);
        using var cmdRecord = ScopedCommand(dbConnection, txOperation, objIdentity, $"SELECT o.* FROM organizations o WHERE o.id=$id AND {strOrganizationScope}", ("$id", lngOrganizationId));
        using var rdrRecord = cmdRecord.ExecuteReader();
        return rdrRecord.Read() ? ReadRecord<Organization>(rdrRecord) : throw new UnauthorizedAccessException();
    });
    // Signed permission-event sequence, global to the workspace. Reads do not advance it.
    // Identity verification also checks enabled/session/CUI/role state on every call.
    public long CuiEpoch() => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        using var cmdEpoch = Database.Command(dbConnection, "SELECT coalesce(max(id),0) FROM audit_log WHERE action IN ('RoleChanged','AccessGranted','AccessRevoked','CuiAuthorized','CuiRevoked','CuiPolicyGranted','CuiPolicyRevoked','CuiDesignationChanged','CuiRecipientChanged','AccountEnabled','AccountDisabled','PasswordReset','PasswordChanged')");
        cmdEpoch.Transaction = txOperation; return (long)cmdEpoch.ExecuteScalar()!;
    });

    public void ValidateCuiAccess(long? lngContactId, bool bContactWrite, long? lngOrganizationId, bool bOrganizationWrite) =>
        ReadOperation((dbConnection, txOperation, objIdentity) =>
        {
            AccessControl.RequireCui(dbConnection, txOperation, objIdentity);
            if (lngContactId is > 0) AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId.Value, bContactWrite);
            if (lngOrganizationId is > 0) AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Organization, lngOrganizationId.Value, bOrganizationWrite);
            if ((lngContactId == 0 && bContactWrite) || (lngOrganizationId == 0 && bOrganizationWrite))
                AccessControl.Administrator(dbConnection, txOperation, objIdentity);
            return true;
        });

    public Contact ContactRecord(long lngContactId) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Contact, lngContactId, false);
        using var cmdContact = ScopedCommand(dbConnection, txOperation, objIdentity, $"SELECT c.* FROM contacts c WHERE c.id=$id AND {strContactScope}", ("$id", lngContactId));
        using var rdrContact = cmdContact.ExecuteReader();
        if (!rdrContact.Read()) throw new UnauthorizedAccessException();
        var objContact = ReadRecord<Contact>(rdrContact); rdrContact.Close();
        using var cmdLinks = ScopedCommand(dbConnection, txOperation, objIdentity,
            $"SELECT o.id FROM contact_organizations co JOIN organizations o ON o.id=co.organization_id WHERE co.contact_id=$id AND co.status='Active' AND o.archived=0 AND {strOrganizationScope} ORDER BY o.id", ("$id", lngContactId));
        using var rdrLinks = cmdLinks.ExecuteReader();
        while (rdrLinks.Read()) objContact.lstOrganizationIds.Add(rdrLinks.GetInt64(0));
        objContact.lngOrganizationId = objContact.lstOrganizationIds.Count == 0 ? null : objContact.lstOrganizationIds[0];
        return objContact;
    });

    public CuiLookupPage LookupPage(int iOffset = 0, CancellationToken ctCancellation = default, long? lngOrganizationId = null, bool bArchived = false, bool bOrganizationsArchived = false) => ReadOperation((dbConnection, txOperation, objIdentity) =>
    {
        if (iOffset < 0) throw new ArgumentOutOfRangeException(nameof(iOffset));
        ctCancellation.ThrowIfCancellationRequested();
        if (lngOrganizationId.HasValue) AccessControl.Require(dbConnection, txOperation, objIdentity, RecordKind.Organization, lngOrganizationId.Value, false);
        var lstContacts = new List<Contact>(); var lstOrganizations = new List<Organization>();
        var bTransferred = false;
        try
        {
        using var cmdContacts = ScopedCommand(dbConnection, txOperation, objIdentity, $"""
            SELECT c.id,c.revision,c.status,c.availability,
                substr(c.prefix,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.first_name,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.middle_names,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.last_name,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.suffix,1,{CuiMemoryPolicy.iMaximumFieldLength}),
                substr(c.email,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.phone,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.address,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.skills,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(c.tags,1,{CuiMemoryPolicy.iMaximumFieldLength}),
                c.cui_category,c.distribution_statement,c.controlling_office,c.determination_date,c.distribution_notice
            FROM contacts c WHERE c.archived=$archived AND {strContactScope}
                AND ($organization IS NULL OR EXISTS(SELECT 1 FROM contact_organizations co WHERE co.contact_id=c.id AND co.organization_id=$organization AND co.status='Active'))
            ORDER BY c.id LIMIT $limit OFFSET $offset
            """, ("$limit", CuiMemoryPolicy.iPageSize), ("$offset", iOffset), ("$organization", lngOrganizationId), ("$archived", bArchived));
        using var rdrContacts = cmdContacts.ExecuteReader();
        while (rdrContacts.Read())
        {
            ctCancellation.ThrowIfCancellationRequested();
            lstContacts.Add(CuiPolicy.Apply(new Contact { Id = rdrContacts.GetInt64(0), Revision = rdrContacts.GetInt32(1), Status = Enum.Parse<RecordStatus>(rdrContacts.GetString(2)),
                Availability = Enum.Parse<Availability>(rdrContacts.GetString(3)), Prefix = rdrContacts.GetString(4), FirstName = rdrContacts.GetString(5),
                MiddleNames = rdrContacts.GetString(6), LastName = rdrContacts.GetString(7), Suffix = rdrContacts.GetString(8), Email = rdrContacts.GetString(9),
                Phone = rdrContacts.GetString(10), Address = rdrContacts.GetString(11), Skills = rdrContacts.GetString(12), strTags = rdrContacts.GetString(13) }, CuiPolicy.Read(rdrContacts, 14)));
        }
        rdrContacts.Close();
        // Relationship IDs are scoped too; no organization names or full profiles are copied.
        foreach (var objContact in lstContacts)
        {
            ctCancellation.ThrowIfCancellationRequested();
            using var cmdLinks = ScopedCommand(dbConnection, txOperation, objIdentity,
                $"SELECT o.id FROM contact_organizations co JOIN organizations o ON o.id=co.organization_id WHERE co.contact_id=$id AND co.status='Active' AND o.archived=0 AND {strOrganizationScope} ORDER BY o.id LIMIT $limit",
                ("$id", objContact.Id), ("$limit", CuiMemoryPolicy.iPageSize));
            using var rdrLinks = cmdLinks.ExecuteReader();
            while (rdrLinks.Read()) { ctCancellation.ThrowIfCancellationRequested(); objContact.lstOrganizationIds.Add(rdrLinks.GetInt64(0)); }
        }
        using var cmdOrganizations = ScopedCommand(dbConnection, txOperation, objIdentity, $"""
            SELECT o.id,o.revision,o.status,substr(o.name,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(o.email,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(o.phone,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(o.address,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(o.services,1,{CuiMemoryPolicy.iMaximumFieldLength}),substr(o.tags,1,{CuiMemoryPolicy.iMaximumFieldLength}),
                o.cui_category,o.distribution_statement,o.controlling_office,o.determination_date,o.distribution_notice
            FROM organizations o WHERE o.archived=$organizationsArchived AND {strOrganizationScope} ORDER BY o.id LIMIT $limit OFFSET $offset
            """, ("$limit", CuiMemoryPolicy.iPageSize), ("$offset", iOffset), ("$organizationsArchived", bOrganizationsArchived));
        using var rdrOrganizations = cmdOrganizations.ExecuteReader();
        while (rdrOrganizations.Read())
        {
            ctCancellation.ThrowIfCancellationRequested();
            lstOrganizations.Add(CuiPolicy.Apply(new Organization { Id = rdrOrganizations.GetInt64(0), Revision = rdrOrganizations.GetInt32(1), Status = Enum.Parse<RecordStatus>(rdrOrganizations.GetString(2)),
                Archived = bOrganizationsArchived, Name = rdrOrganizations.GetString(3), Email = rdrOrganizations.GetString(4), Phone = rdrOrganizations.GetString(5), Address = rdrOrganizations.GetString(6),
                Services = rdrOrganizations.GetString(7), Tags = rdrOrganizations.GetString(8) }, CuiPolicy.Read(rdrOrganizations, 9)));
        }
        ctCancellation.ThrowIfCancellationRequested();
        var objPage = new CuiLookupPage(lstContacts, lstOrganizations); bTransferred = true; return objPage;
        }
        finally { if (!bTransferred) { CuiMemoryPolicy.Release(lstContacts); CuiMemoryPolicy.Release(lstOrganizations); } }
    });
}
