using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed record AnalyticsActivityDay(DateOnly dtDay,bool bActiveOnly,long lngNotes);
public sealed record AnalyticsContactTotal(RecordStatus enStatus, bool bArchived, long lngCount);
public sealed record AnalyticsOrganizationTotal(bool bArchived, long lngCount);
public sealed record AnalyticsPeriodSummary(string strPeriod, bool bActiveOnly, bool bPrevious,
    DateTimeOffset dtStartUtc, DateTimeOffset dtEndUtc, long lngNotes, long lngContacts, long lngOrganizations);
public sealed record AnalyticsContactRow(long lngId, string strName, RecordStatus enStatus, bool bArchived,
    string strOrganizations, DateTimeOffset? dtLastNoteUtc, string strPhone = "", string strEmail = "", string strSkills = "", string strTags = "");
public sealed record AnalyticsSkillRow(long lngId, string strName, RecordStatus enStatus, bool bArchived, string strOrganizations, string strSkills);
public sealed record AnalyticsContractRow(long lngId, string strName, RecordStatus enStatus, bool bArchived,
    string strOrganizations, DateOnly dtDate, long lngValueCents, string strLocation);
public sealed record AnalyticsOrganizationRow(long lngId, string strName, bool bArchived, long lngActiveContacts,
    string strPhone = "", string strEmail = "", string strServices = "", string strTags = "");
public sealed record AnalyticsMemberRow(long lngOrganizationId, long lngContactId, string strName, RecordStatus enStatus, bool bArchived,
    string strPhone = "", string strEmail = "", string strSkills = "", string strTags = "", DateTimeOffset? dtLastNoteUtc = null);
public sealed record AnalyticsMovementRow(long lngContactId, string strName, RecordStatus enStatus, bool bArchived,
    string strOrganization, bool bOrganizationArchived, string strRole, DateOnly dtStart, DateOnly? dtEnd);

public sealed class AnalyticsSnapshot
{
    public DateTimeOffset AsOfUtc { get; }
    public List<AnalyticsActivityDay> DailyActivity { get; } = [];
    public List<AnalyticsContactTotal> ContactTotals { get; } = [];
    public List<AnalyticsOrganizationTotal> OrganizationTotals { get; } = [];
    public List<AnalyticsPeriodSummary> PeriodSummaries { get; } = [];
    public List<AnalyticsContactRow> LastContacted { get; } = [];
    public List<AnalyticsContactRow> RecentContacts { get; } = [];
    public List<AnalyticsContactRow> ContactSearch { get; } = [];
    public List<AnalyticsSkillRow> SkillRoster { get; } = [];
    public List<AnalyticsContractRow> LatestContracts { get; } = [];
    public List<AnalyticsOrganizationRow> OrganizationRoster { get; } = [];
    public List<AnalyticsMemberRow> OrganizationMembers { get; } = [];
    public List<AnalyticsMovementRow> Movement { get; } = [];
    public long LastContactedTotal { get; internal set; }
    public long RecentContactsTotal { get; internal set; }
    public long ContactSearchTotal { get; internal set; }
    public long SkillRosterTotal { get; internal set; }
    public long LatestContractsTotal { get; internal set; }
    public long OrganizationRosterTotal { get; internal set; }
    public long OrganizationMembersTotal { get; internal set; }
    public long MovementTotal { get; internal set; }
    public AnalyticsSnapshot(DateTimeOffset dtAsOfUtc) => AsOfUtc = dtAsOfUtc.ToUniversalTime();
    public void Release()
    {
        DailyActivity.Clear(); ContactTotals.Clear(); OrganizationTotals.Clear(); PeriodSummaries.Clear(); LastContacted.Clear(); SkillRoster.Clear();
        LatestContracts.Clear(); OrganizationRoster.Clear(); OrganizationMembers.Clear(); Movement.Clear();
        RecentContacts.Clear(); ContactSearch.Clear();
        LastContactedTotal = SkillRosterTotal = LatestContractsTotal = OrganizationRosterTotal = OrganizationMembersTotal = MovementTotal = 0;
        RecentContactsTotal = ContactSearchTotal = 0;
    }
}

public sealed partial class CrmService
{
    public AnalyticsSnapshot Analytics() => AnalyticsAt(DateTimeOffset.UtcNow);

    // A deterministic internal boundary for fixtures; production captures its as-of clock exactly once.
    internal AnalyticsSnapshot AnalyticsAt(DateTimeOffset dtAsOfUtc)
    {
        try
        {
            return ReadOperation((dbConnection, txOperation, objIdentity) =>
            {
                var objSnapshot = new AnalyticsSnapshot(dtAsOfUtc);
                var bTransferred = false;
                SqliteCommand Query(string strSql, params (string, object?)[] arrParameters) =>
                    ScopedCommand(dbConnection, txOperation, objIdentity, strSql, arrParameters);
                long Count(string strSql, params (string, object?)[] arrParameters)
                { using var cmdCount = Query(strSql, arrParameters); return Convert.ToInt64(cmdCount.ExecuteScalar(), CultureInfo.InvariantCulture); }
                try
                {
                    AccessControl.RequireCui(dbConnection, txOperation, objIdentity);
                    // All normal note writers use UTC O (+00:00), including Debug history insertion.
                    // Exact same-format text bounds preserve sub-millisecond precision. Mixed formats
                    // must be repaired explicitly rather than silently sorting/comparing incorrectly.
                    if (Count($"SELECT count(*) FROM interactions i JOIN contacts c ON c.id=i.contact_id AND {strContactScope} WHERE length(i.created_at)<>33 OR i.created_at NOT GLOB '????-??-??T??:??:??.???????+00:00'") != 0)
                        throw new InvalidOperationException("Analytics could not be loaded.");

                    using (var cmdTotals = Query($"SELECT c.status,c.archived,count(*) FROM contacts c WHERE {strContactScope} GROUP BY c.status,c.archived ORDER BY c.status,c.archived LIMIT 8"))
                    using (var rdrTotals = cmdTotals.ExecuteReader())
                        while (rdrTotals.Read()) objSnapshot.ContactTotals.Add(new(Enum.Parse<RecordStatus>(rdrTotals.GetString(0)), rdrTotals.GetInt64(1) != 0, rdrTotals.GetInt64(2)));
                    var strOrganizations = $"FROM (SELECT 1) root JOIN organizations o ON {strOrganizationScope}";
                    using (var cmdTotals = Query($"SELECT o.archived,count(*) {strOrganizations} GROUP BY o.archived ORDER BY o.archived LIMIT 2"))
                    using (var rdrTotals = cmdTotals.ExecuteReader())
                        while (rdrTotals.Read()) objSnapshot.OrganizationTotals.Add(new(rdrTotals.GetInt64(0) != 0, rdrTotals.GetInt64(1)));

                    var dtNow = objSnapshot.AsOfUtc;
                    var dtYear = new DateTimeOffset(dtNow.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    foreach (var (strPeriod, dtStart) in new[] { ("24 hours", dtNow.AddHours(-24)), ("7 days", dtNow.AddDays(-7)),
                        ("30 days", dtNow.AddDays(-30)), ("6 months", dtNow.AddMonths(-6)), ("1 year", dtNow.AddYears(-1)), ("Year to date", dtYear) })
                    foreach (var bActive in new[] { true, false })
                    {
                        var dtEnd = dtNow;
                        var dtBegin = dtStart;
                        var arrParameters = new (string, object?)[] { ("$start", AnalyticsUtc(dtBegin)), ("$end", AnalyticsUtc(dtEnd)), ("$active", bActive) };
                        var strNotes = $"FROM interactions i JOIN contacts c ON c.id=i.contact_id AND {strContactScope}";
                        var strWindow = "WHERE ($active=0 OR (c.status='Active' AND c.archived=0)) AND i.created_at >= $start AND i.created_at < $end";
                        long lngNotes; long lngContacts;
                        using (var cmdPeriod = Query($"SELECT count(*),count(DISTINCT c.id) {strNotes} {strWindow}", arrParameters))
                        using (var rdrPeriod = cmdPeriod.ExecuteReader())
                        { rdrPeriod.Read(); lngNotes = rdrPeriod.GetInt64(0); lngContacts = rdrPeriod.GetInt64(1); }
                        // Separate organization aggregation avoids multiplying the note count for multi-membership contacts.
                        var lngOrganizations = Count($"SELECT count(DISTINCT o.id) {strNotes} LEFT JOIN contact_organizations co ON co.contact_id=c.id AND co.status='Active' LEFT JOIN organizations o ON o.id=co.organization_id AND {strOrganizationScope} AND ($active=0 OR o.archived=0) {strWindow}", arrParameters);
                        objSnapshot.PeriodSummaries.Add(new(strPeriod, bActive, false, dtBegin, dtEnd, lngNotes, lngContacts, lngOrganizations));
                    }

                    // One bounded aggregation supplies a seven-day UTC trend. It
                    // includes retained records only in the explicitly all-authorized variant.
                    var dtFirstDay=new DateTimeOffset(dtNow.UtcDateTime.Date.AddDays(-6),TimeSpan.Zero);
                    var dicDays=new Dictionary<(string strDay,bool bActive),long>();
                    using(var cmdDays=Query($"""
                        SELECT substr(i.created_at,1,10),v.active_only,count(*) FROM interactions i
                        JOIN contacts c ON c.id=i.contact_id AND {strContactScope}
                        CROSS JOIN (SELECT 1 AS active_only UNION ALL SELECT 0) v
                        WHERE i.created_at >= $start AND i.created_at < $end
                            AND (v.active_only=0 OR (c.status='Active' AND c.archived=0))
                        GROUP BY substr(i.created_at,1,10),v.active_only
                        """,("$start",AnalyticsUtc(dtFirstDay)),("$end",AnalyticsUtc(dtNow))))
                    using(var rdrDays=cmdDays.ExecuteReader())
                        while(rdrDays.Read()) dicDays[(rdrDays.GetString(0),rdrDays.GetInt64(1)!=0)]=rdrDays.GetInt64(2);
                    for(var iDay=0;iDay<7;iDay++)
                    foreach(var bActive in new[]{true,false})
                    {
                        var dtDay=DateOnly.FromDateTime(dtFirstDay.UtcDateTime).AddDays(iDay);
                        objSnapshot.DailyActivity.Add(new(dtDay,bActive,dicDays.GetValueOrDefault((dtDay.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),bActive))));
                    }

                    var strNames = $"COALESCE((SELECT substr(group_concat(q.name, ', '),1,512) FROM (SELECT DISTINCT o.name AS name FROM contact_organizations co LEFT JOIN organizations o ON o.id=co.organization_id AND o.archived=0 AND {strOrganizationScope} WHERE co.contact_id=c.id AND co.status='Active' AND o.id IS NOT NULL ORDER BY o.name COLLATE NOCASE,o.name) q),'')";
                    var strFields = $"c.id,substr({strContactNameSearch},1,512),c.status,c.archived,{strNames}";
                    var strContacts = $"FROM contacts c WHERE {strContactScope}";
                    var strLast = "(SELECT max(i.created_at) FROM interactions i WHERE i.contact_id=c.id)";
                    // Keep the old strFields/SkillRoster projection unchanged. Every added
                    // free-text column is bounded in SQL before it reaches managed memory.
                    var strContactDetails = "substr(c.phone,1,512),substr(c.email,1,512),substr(c.skills,1,512),substr(c.tags,1,512)";
                    var strContactProjection = $"{strFields},{strLast} AS last_note,{strContactDetails}";
                    objSnapshot.LastContactedTotal = Count($"SELECT count(*) {strContacts}");
                    using (var cmdRows = Query($"SELECT {strContactProjection} {strContacts} ORDER BY last_note IS NOT NULL,last_note,c.id LIMIT 500"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.LastContacted.Add(ReadAnalyticsContact(rdrRows));
                    var strRecent = strContacts + " AND EXISTS(SELECT 1 FROM interactions i WHERE i.contact_id=c.id)";
                    objSnapshot.RecentContactsTotal = Count($"SELECT count(*) {strRecent}");
                    using (var cmdRows = Query($"SELECT {strContactProjection} {strRecent} ORDER BY last_note DESC,c.id LIMIT 100"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.RecentContacts.Add(ReadAnalyticsContact(rdrRows));
                    var strSearch = strContacts + " AND (length(trim(COALESCE(c.skills,'')))>0 OR length(trim(COALESCE(c.tags,'')))>0)";
                    objSnapshot.ContactSearchTotal = Count($"SELECT count(*) {strSearch}");
                    using (var cmdRows = Query($"SELECT {strContactProjection} {strSearch} ORDER BY c.last_name COLLATE NOCASE,c.first_name COLLATE NOCASE,c.id LIMIT 500"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.ContactSearch.Add(ReadAnalyticsContact(rdrRows));
                    var strSkills = strContacts + " AND length(trim(COALESCE(c.skills,'')))>0";
                    objSnapshot.SkillRosterTotal = Count($"SELECT count(*) {strSkills}");
                    using (var cmdRows = Query($"SELECT {strFields},substr(c.skills,1,512) {strSkills} ORDER BY c.id LIMIT 500"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.SkillRoster.Add(new(rdrRows.GetInt64(0), rdrRows.GetString(1), Enum.Parse<RecordStatus>(rdrRows.GetString(2)), rdrRows.GetInt64(3) != 0, rdrRows.GetString(4), rdrRows.GetString(5)));
                    var strContracts = $"FROM contacts c JOIN contracts k ON k.id=(SELECT k2.id FROM contracts k2 WHERE k2.contact_id=c.id ORDER BY k2.contract_date DESC,k2.id DESC LIMIT 1) WHERE {strContactScope}";
                    objSnapshot.LatestContractsTotal = Count($"SELECT count(*) {strContracts}");
                    using (var cmdRows = Query($"SELECT {strFields},k.contract_date,k.value_cents,substr(k.location,1,512) {strContracts} ORDER BY k.contract_date DESC,k.id DESC,c.id LIMIT 500"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.LatestContracts.Add(new(rdrRows.GetInt64(0), rdrRows.GetString(1), Enum.Parse<RecordStatus>(rdrRows.GetString(2)), rdrRows.GetInt64(3) != 0, rdrRows.GetString(4), AnalyticsDate(rdrRows.GetString(5)), rdrRows.GetInt64(6), rdrRows.GetString(7)));

                    objSnapshot.OrganizationRosterTotal = Count($"SELECT count(*) {strOrganizations}");
                    using (var cmdRows = Query($"SELECT o.id,substr(o.name,1,512),o.archived,count(DISTINCT c.id),substr(o.phone,1,512),substr(o.email,1,512),substr(o.services,1,512),substr(o.tags,1,512) {strOrganizations} LEFT JOIN contact_organizations co ON co.organization_id=o.id AND co.status='Active' LEFT JOIN contacts c ON c.id=co.contact_id AND c.status='Active' AND c.archived=0 AND {strContactScope} GROUP BY o.id,o.name,o.archived ORDER BY o.name COLLATE NOCASE,o.id LIMIT 500"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.OrganizationRoster.Add(new(rdrRows.GetInt64(0), rdrRows.GetString(1), rdrRows.GetInt64(2) != 0, rdrRows.GetInt64(3), rdrRows.GetString(4), rdrRows.GetString(5), rdrRows.GetString(6), rdrRows.GetString(7)));
                    var strMembers = $"FROM contacts c JOIN contact_organizations co ON co.contact_id=c.id AND co.status='Active' JOIN organizations o ON o.id=co.organization_id AND {strOrganizationScope} WHERE {strContactScope}";
                    objSnapshot.OrganizationMembersTotal = Count($"SELECT count(*) {strMembers}");
                    using (var cmdRows = Query($"SELECT o.id,c.id,substr({strContactNameSearch},1,512),c.status,c.archived,{strContactDetails},{strLast} {strMembers} ORDER BY o.id,c.id LIMIT 1000"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.OrganizationMembers.Add(new(rdrRows.GetInt64(0), rdrRows.GetInt64(1), rdrRows.GetString(2), Enum.Parse<RecordStatus>(rdrRows.GetString(3)), rdrRows.GetInt64(4) != 0,
                            rdrRows.GetString(5), rdrRows.GetString(6), rdrRows.GetString(7), rdrRows.GetString(8), AnalyticsTimestamp(rdrRows, 9)));
                    var strOtherOrganizationScope = strOrganizationScope.Replace("o.", "o2.", StringComparison.Ordinal);
                    // Hidden placements contribute neither rows nor the multiple-placement threshold.
                    var strMovement = $"FROM employment e JOIN contacts c ON c.id=e.contact_id AND {strContactScope} JOIN organizations o ON o.id=e.organization_id AND {strOrganizationScope} WHERE (SELECT count(*) FROM employment e2 JOIN organizations o2 ON o2.id=e2.organization_id AND {strOtherOrganizationScope} WHERE e2.contact_id=c.id)>1";
                    objSnapshot.MovementTotal = Count($"SELECT count(*) {strMovement}");
                    using (var cmdRows = Query($"SELECT c.id,substr({strContactNameSearch},1,512),c.status,c.archived,substr(o.name,1,512),o.archived,substr(e.role,1,512),e.start_date,e.end_date {strMovement} ORDER BY c.id,e.start_date,e.id LIMIT 500"))
                    using (var rdrRows = cmdRows.ExecuteReader())
                        while (rdrRows.Read()) objSnapshot.Movement.Add(new(rdrRows.GetInt64(0), rdrRows.GetString(1), Enum.Parse<RecordStatus>(rdrRows.GetString(2)), rdrRows.GetInt64(3) != 0, rdrRows.GetString(4), rdrRows.GetInt64(5) != 0, rdrRows.GetString(6), AnalyticsDate(rdrRows.GetString(7)), rdrRows.IsDBNull(8) ? null : AnalyticsDate(rdrRows.GetString(8))));
                    bTransferred = true; return objSnapshot;
                }
                finally { if (!bTransferred) objSnapshot.Release(); }
            });
        }
        catch (Exception) { throw new InvalidOperationException("Analytics could not be loaded."); }
    }
    private static string AnalyticsUtc(DateTimeOffset dtValue) => dtValue.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset? AnalyticsTimestamp(SqliteDataReader rdrRow, int iColumn) => rdrRow.IsDBNull(iColumn)
        ? null : DateTimeOffset.ParseExact(rdrRow.GetString(iColumn), "O", CultureInfo.InvariantCulture);
    private static AnalyticsContactRow ReadAnalyticsContact(SqliteDataReader rdrRow) => new(rdrRow.GetInt64(0), rdrRow.GetString(1),
        Enum.Parse<RecordStatus>(rdrRow.GetString(2)), rdrRow.GetInt64(3) != 0, rdrRow.GetString(4), AnalyticsTimestamp(rdrRow, 5),
        rdrRow.GetString(6), rdrRow.GetString(7), rdrRow.GetString(8), rdrRow.GetString(9));
    private static DateOnly AnalyticsDate(string strValue) => DateOnly.ParseExact(strValue, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
