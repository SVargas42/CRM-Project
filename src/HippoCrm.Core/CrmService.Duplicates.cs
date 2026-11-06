using System.Text;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public enum DuplicateResolution { Ignore, Link }
public sealed record DuplicateCandidate(long lngId,int iRevision,string strName,string strReason,bool bCanLink);
public sealed record DuplicateDecision(DuplicateResolution enResolution,IReadOnlyList<DuplicateCandidate> lstReviewed,long? lngLinkId=null);

public sealed partial class CrmService
{
    public IReadOnlyList<DuplicateCandidate> PossibleDuplicates(Contact objDraft) => ReadOperation((dbConnection,txOperation,objIdentity) =>
        FindDuplicates(dbConnection,txOperation,objIdentity,RecordKind.Contact,objDraft));
    public IReadOnlyList<DuplicateCandidate> PossibleDuplicates(Organization objDraft) => ReadOperation((dbConnection,txOperation,objIdentity) =>
        FindDuplicates(dbConnection,txOperation,objIdentity,RecordKind.Organization,objDraft));

    private static string Canonical(string? strValue) => string.Join(' ',(strValue??"").Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static HashSet<string> Identifiers(string? strValue,bool bPhone) => (strValue??"")
        .Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
        .Select(strItem => bPhone ? ContactDetailsValidation.Phone(strItem) : ContactDetailsValidation.Email(strItem))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<DuplicateCandidate> FindDuplicates<T>(SqliteConnection dbConnection,SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity,RecordKind enKind,T objDraft)
    {
        ArgumentNullException.ThrowIfNull(objDraft);
        AccessControl.RequireCui(dbConnection,txOperation,objIdentity);
        var objValues=objDraft switch
        {
            Contact objContact => (lngId:objContact.Id,strName:objContact.DisplayName,strEmail:objContact.Email,strPhone:objContact.Phone,strAddress:objContact.Address),
            Organization objOrganization => (lngId:objOrganization.Id,strName:objOrganization.Name,strEmail:objOrganization.Email,strPhone:objOrganization.Phone,strAddress:objOrganization.Address),
            _ => throw new ArgumentException("Choose a valid contact or organization.")
        };
        var lngId=objValues.lngId;
        if(lngId!=0) AccessControl.Require(dbConnection,txOperation,objIdentity,enKind,lngId,false);
        else AccessControl.Administrator(dbConnection,txOperation,objIdentity);
        var strName=Canonical(objValues.strName);
        var strAddress=Canonical(objValues.strAddress);
        var setEmails=Identifiers(objValues.strEmail,false);
        var setPhones=Identifiers(objValues.strPhone,true);
        var strTable=enKind==RecordKind.Contact?"contacts":"organizations";
        var strScope=enKind==RecordKind.Contact?strContactScope:strOrganizationScope;
        var strAlias=enKind==RecordKind.Contact?"c":"o";
        var strNameSql=enKind==RecordKind.Contact?"c.first_name || ' ' || c.last_name":"o.name";
        using var cmdCandidates=ScopedCommand(dbConnection,txOperation,objIdentity,
            $"SELECT {strAlias}.id,{strAlias}.revision,{strNameSql},{strAlias}.email,{strAlias}.phone,{strAlias}.address FROM {strTable} {strAlias} WHERE {strAlias}.id<>$id AND {strScope} ORDER BY {strAlias}.id",("$id",lngId));
        var lstVisible=new List<(long lngId,int iRevision,string strName,string strEmail,string strPhone,string strAddress)>();
        using (var rdrCandidates=cmdCandidates.ExecuteReader())
            while(rdrCandidates.Read()) lstVisible.Add((rdrCandidates.GetInt64(0),rdrCandidates.GetInt32(1),rdrCandidates.GetString(2),rdrCandidates.GetString(3),rdrCandidates.GetString(4),rdrCandidates.GetString(5)));
        var lstMatches=new List<DuplicateCandidate>();
        foreach(var objRow in lstVisible)
        {
            var lstReasons=new List<string>();
            // Shared mailboxes/numbers are evidence to review, never unique identity.
            try
            {
                if(setEmails.Overlaps(Identifiers(objRow.strEmail,false))) lstReasons.Add("Shared email address (may be a shared mailbox)");
                if(setPhones.Overlaps(Identifiers(objRow.strPhone,true))) lstReasons.Add("Shared phone number (may be a family or company number)");
            }
            catch(ArgumentException) { /* Legacy malformed details must not crash a scan or be treated as an identity match. */ }
            if(strName.Length>0 && strName==Canonical(objRow.strName) && strAddress.Length>0 && strAddress==Canonical(objRow.strAddress))
                lstReasons.Add("Matching name and address");
            if(lstReasons.Count==0) continue;
            lstMatches.Add(new(objRow.lngId,objRow.iRevision,objRow.strName,string.Join("; ",lstReasons),
                AccessControl.CanAccess(dbConnection,txOperation,objIdentity,enKind,objRow.lngId,true)));
        }
        return lstMatches;
    }

    private static void ValidateDuplicateDecision<T>(SqliteConnection dbConnection,SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity,RecordKind enKind,T objDraft,DuplicateDecision? objDecision)
    {
        var lstCurrent=FindDuplicates(dbConnection,txOperation,objIdentity,enKind,objDraft);
        if(lstCurrent.Count==0 && objDecision is null) return;
        if(objDecision is null || objDecision.lstReviewed is null || objDecision.lstReviewed.Any(objRow=>objRow is null) || !Enum.IsDefined(objDecision.enResolution)
            || !lstCurrent.Select(objRow=>(objRow.lngId,objRow.iRevision,objRow.strReason,objRow.bCanLink))
                .SequenceEqual(objDecision.lstReviewed.Select(objRow=>(objRow.lngId,objRow.iRevision,objRow.strReason,objRow.bCanLink))))
            throw new ArgumentException("Possible duplicates changed. Review the matches again before saving.");
        if(objDecision.enResolution==DuplicateResolution.Link)
        {
            var objTarget=lstCurrent.SingleOrDefault(objRow=>objRow.lngId==objDecision.lngLinkId);
            if(objTarget is null || !objTarget.bCanLink) throw new ArgumentException("Choose a matching record you can edit before linking.");
            // Must occur before the caller mutates anything: permission denials commit an audit event.
            AccessControl.Require(dbConnection,txOperation,objIdentity,enKind,objTarget.lngId,true);
        }
    }

    private static void RecordDuplicateDecision(SqliteConnection dbConnection,SqliteTransaction txOperation,
        AuthorizedIdentity objIdentity,RecordKind enKind,long lngSavedId,DuplicateDecision? objDecision)
    {
        if(objDecision is null) return;
        if(objDecision.enResolution==DuplicateResolution.Ignore)
        {
            SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,
                enKind==RecordKind.Contact?AuditEntity.Contacts:AuditEntity.Organizations,lngSavedId,AuditAction.DuplicateOverride);
            return;
        }
        var lngOther=objDecision.lngLinkId!.Value;
        using var cmdLink=Database.Command(dbConnection,"""
            INSERT INTO record_links(entity,source_id,target_id,created_at,user_id) VALUES($entity,$source,$target,$now,$user)
            ON CONFLICT(entity,source_id,target_id) DO NOTHING RETURNING id
            """,("$entity",enKind.ToString()),("$source",Math.Min(lngSavedId,lngOther)),("$target",Math.Max(lngSavedId,lngOther)),("$now",Database.Now),("$user",objIdentity.lngUserId));
        cmdLink.Transaction=txOperation; var objLinkId=cmdLink.ExecuteScalar();
        if(objLinkId is null)
        {
            using var cmdExisting=Database.Command(dbConnection,"SELECT id FROM record_links WHERE entity=$entity AND source_id=$source AND target_id=$target",("$entity",enKind.ToString()),("$source",Math.Min(lngSavedId,lngOther)),("$target",Math.Max(lngSavedId,lngOther)));
            cmdExisting.Transaction=txOperation; objLinkId=cmdExisting.ExecuteScalar();
        }
        SecurityAudit.Append(dbConnection,txOperation,objIdentity.lngUserId,AuditEntity.RecordLinks,Convert.ToInt64(objLinkId),AuditAction.DuplicateLinked);
    }

    public IReadOnlyList<DuplicateCandidate> LinkedRecords(RecordKind enKind,long lngRecordId) => ReadOperation((dbConnection,txOperation,objIdentity) =>
    {
        if(!Enum.IsDefined(enKind)) throw new ArgumentException("Choose a valid record.");
        AccessControl.Require(dbConnection,txOperation,objIdentity,enKind,lngRecordId,false);
        var strTable=enKind==RecordKind.Contact?"contacts":"organizations";
        var strName=enKind==RecordKind.Contact?"r.first_name || ' ' || r.last_name":"r.name";
        var strVisible=(enKind==RecordKind.Contact?strContactScope:strOrganizationScope).Replace(enKind==RecordKind.Contact?"c.":"o.","r.");
        using var cmdLinks=ScopedCommand(dbConnection,txOperation,objIdentity,$"SELECT r.id,r.revision,{strName} FROM record_links l JOIN {strTable} r ON r.id=CASE WHEN l.source_id=$id THEN l.target_id ELSE l.source_id END WHERE l.entity=$entity AND (l.source_id=$id OR l.target_id=$id) AND {strVisible} ORDER BY r.id",("$id",lngRecordId),("$entity",enKind.ToString()));
        cmdLinks.Transaction=txOperation;
        var lstRows=new List<(long lngId,int iRevision,string strName)>();
        using(var rdrLinks=cmdLinks.ExecuteReader()) while(rdrLinks.Read()) lstRows.Add((rdrLinks.GetInt64(0),rdrLinks.GetInt32(1),rdrLinks.GetString(2)));
        return (IReadOnlyList<DuplicateCandidate>)lstRows.Where(objRow=>AccessControl.CanAccess(dbConnection,txOperation,objIdentity,enKind,objRow.lngId,false))
            .Select(objRow=>new DuplicateCandidate(objRow.lngId,objRow.iRevision,objRow.strName,"Linked for review; histories and permissions remain separate",false)).ToArray();
    });
}
