using System.Globalization;
using HippoCrm.Core;
using HippoCrm.Desktop.Utilities;
namespace HippoCrm.Desktop.ViewModels;

public sealed class AnalyticsDisplayRow : ViewModelBase
{
    private string strFullSummary;
    public long lngId { get; }
    public RecordKind? enKind { get; }
    public string[] arrCells { get; }
    public string strSummary => strFullSummary;
    public AnalyticsDisplayRow(long lngId, RecordKind? enKind, string[] arrCells, string? strSummary = null)
    { this.lngId = lngId; this.enKind = enKind; this.arrCells = arrCells; strFullSummary = strSummary ?? string.Join("; ", arrCells); }
    public void Release() { Array.Clear(arrCells); strFullSummary = ""; Notify(nameof(arrCells)); Notify(nameof(strSummary)); }
}
public sealed class AnalyticsTableViewModel : ViewModelBase
{
    private readonly Func<bool> fnVisible;
    private readonly Func<RecordKind, long, Task>? fnOpen;
    private List<AnalyticsDisplayRow> lstCurrent = [];
    private string strCurrentCount = "";
    public string strTitle { get; }
    public string[] arrHeaders { get; }
    public bool bCanActivate => fnOpen is not null;
    public IReadOnlyList<AnalyticsDisplayRow> lstRows => fnVisible() ? lstCurrent : [];
    public string strCount => fnVisible() ? strCurrentCount : "";
    public bool bEmpty => lstRows.Count == 0;
    public Task tskActivation { get; private set; } = Task.CompletedTask;
    public AnalyticsTableViewModel(string strTitle, Func<bool> fnVisible, Func<RecordKind, long, Task>? fnOpen, params string[] arrHeaders)
    { this.strTitle = strTitle; this.fnVisible = fnVisible; this.fnOpen = fnOpen; this.arrHeaders = arrHeaders; }
    public Task ActivateAsync(AnalyticsDisplayRow? objRow) => tskActivation = objRow is not null && objRow.enKind is { } enKind
        && fnVisible() && lstCurrent.Contains(objRow) && fnOpen is not null ? fnOpen(enKind, objRow.lngId) : Task.CompletedTask;
    internal void Replace(IEnumerable<AnalyticsDisplayRow> lstRows, int iLoaded, long lngTotal)
    {
        foreach (var objRow in lstCurrent) objRow.Release();
        lstCurrent.Clear(); lstCurrent = lstRows.ToList();
        strCurrentCount = $"Showing {lstCurrent.Count} of {iLoaded} loaded ({lngTotal} total)";
        Notify(nameof(lstRows)); Notify(nameof(strCount)); Notify(nameof(bEmpty));
    }
    internal void Clear() { Replace([], 0, 0); strCurrentCount = ""; Notify(nameof(strCount)); }
}
public sealed class AnalyticsContactFilter : ViewModelBase
{
    private readonly Action actChange;
    private readonly Func<bool> fnInclude, fnVisible;
    private bool bCurrentActive = true, bCurrentInactive, bCurrentRetired, bCurrentDeceased;
    private string strCurrentSearch = "";
    public AnalyticsContactFilter(Action actChange, Func<bool> fnInclude, Func<bool> fnVisible)
    { this.actChange = actChange; this.fnInclude = fnInclude; this.fnVisible = fnVisible; }
    public bool bActive { get => bCurrentActive; set { if (Set(ref bCurrentActive, value)) actChange(); } }
    public bool bInactive { get => bCurrentInactive; set { if (Set(ref bCurrentInactive, value)) actChange(); } }
    public bool bRetired { get => bCurrentRetired; set { if (Set(ref bCurrentRetired, value)) actChange(); } }
    public bool bDeceased { get => bCurrentDeceased; set { if (Set(ref bCurrentDeceased, value)) actChange(); } }
    public string strSearch { get => fnVisible() ? strCurrentSearch : ""; set { if (Set(ref strCurrentSearch, value ?? "")) actChange(); } }
    public bool bEnabled => fnVisible();
    public bool bNonActiveEnabled => fnVisible() && fnInclude();
    public bool bShowHint => !fnInclude();
    internal bool Status(RecordStatus enStatus) => enStatus switch
    { RecordStatus.Active => bActive, RecordStatus.Inactive => bInactive, RecordStatus.Retired => bRetired, RecordStatus.Deceased => bDeceased, _ => false };
    internal void Freshness() { Notify(nameof(bEnabled)); Notify(nameof(bNonActiveEnabled)); Notify(nameof(bShowHint)); Notify(nameof(strSearch)); }
    internal void Clear()
    {
        strCurrentSearch = ""; bCurrentActive = true; bCurrentInactive = bCurrentRetired = bCurrentDeceased = false;
        foreach (var strName in new[] { nameof(strSearch), nameof(bActive), nameof(bInactive), nameof(bRetired), nameof(bDeceased) }) Notify(strName);
        Freshness();
    }
}
public sealed class AnalyticsOrganizationChoice : ViewModelBase
{
    public long lngId { get; }
    private string strText;
    public string strCaption => strText;
    public AnalyticsOrganizationChoice(AnalyticsOrganizationRow objRow) { lngId = objRow.lngId; strText = objRow.strName + (objRow.bArchived ? " (Archived)" : ""); }
    internal void Release() { strText = ""; Notify(nameof(strCaption)); }
}
public sealed partial class AnalyticsViewModel
{
    private bool bCurrentInclude, bCurrentDropOpen;
    private string strCurrentDays = "30", strCurrentSearch = "", strCurrentOrganizationSearch = "", strCurrentDaysError = "";
    private int iCurrentSearchType;
    private long? lngSelectedOrganization;
    private List<AnalyticsOrganizationChoice> lstCurrentChoices = [];
    private AnalyticsOrganizationChoice? objCurrentHighlight;
    public AnalyticsContactFilter vmLastFilter { get; private set; } = null!;
    public AnalyticsContactFilter vmRecentFilter { get; private set; } = null!;
    public AnalyticsTableViewModel vmLastContacted { get; private set; } = null!;
    public AnalyticsTableViewModel vmRecentContacts { get; private set; } = null!;
    public AnalyticsTableViewModel vmSearch { get; private set; } = null!;
    public AnalyticsTableViewModel vmMembers { get; private set; } = null!;
    public AnalyticsTableViewModel vmContracts { get; private set; } = null!;
    public AnalyticsTableViewModel vmMovement { get; private set; } = null!;
    public bool bHasData => objSnapshot is not null;
    public bool bIncludeInactive { get => bCurrentInclude; set { if (Set(ref bCurrentInclude, value)) RebuildTables(); } }
    public string strDays { get => bHasData ? strCurrentDays : ""; set { if (Set(ref strCurrentDays, value ?? "")) RebuildTables(); } }
    public string strDaysError => strCurrentDaysError;
    public bool bDaysError => strDaysError.Length > 0;
    public string strFindSearch { get => bHasData ? strCurrentSearch : ""; set { if (Set(ref strCurrentSearch, value ?? "")) RebuildTables(); } }
    public int iSearchType { get => iCurrentSearchType; set { if (value is >= 0 and <= 2 && Set(ref iCurrentSearchType, value)) RebuildTables(); } }
    public bool bSearchEmpty => bHasData && strFindSearch.Trim().Length > 0 && vmSearch.bEmpty;
    public string strOrganizationSearch { get => bHasData ? strCurrentOrganizationSearch : ""; set { if (Set(ref strCurrentOrganizationSearch, value ?? "")) { RebuildChoices(); bOrganizationDropOpen = bHasData; } } }
    public IReadOnlyList<AnalyticsOrganizationChoice> lstOrganizationChoices => bHasData ? lstCurrentChoices : [];
    public AnalyticsOrganizationChoice? objHighlightedOrganization { get => bHasData ? objCurrentHighlight : null; set => Set(ref objCurrentHighlight, value); }
    public bool bOrganizationDropOpen { get => bHasData && bCurrentDropOpen; set => Set(ref bCurrentDropOpen, value); }
    private AnalyticsOrganizationRow? SelectedOrganization => objSnapshot?.OrganizationRoster.FirstOrDefault(objRow => objRow.lngId == lngSelectedOrganization && OrganizationAllowed(objRow.bArchived));
    public bool bSelectOrganization => SelectedOrganization is null;
    public bool bNoMembers => !bSelectOrganization && vmMembers.bEmpty;
    public bool bNoMovement => vmMovement.bEmpty;
    public string strOrganizationPhone => SelectedOrganization?.strPhone ?? "";
    public string strSelectedOrganization => SelectedOrganization?.strName ?? "";
    public string strOrganizationEmail => SelectedOrganization?.strEmail ?? "";
    public string strOrganizationServices => SelectedOrganization?.strServices ?? "";
    public string strOrganizationTags => SelectedOrganization?.strTags ?? "";
    private bool ContactAllowed(RecordStatus enStatus, bool bArchived) => bIncludeInactive || enStatus == RecordStatus.Active && !bArchived;
    private bool OrganizationAllowed(bool bArchived) => bIncludeInactive || !bArchived;
    private void InitializeTables()
    {
        bool Visible() => bHasData;
        vmLastFilter = new(RebuildTables, () => bIncludeInactive, Visible); vmRecentFilter = new(RebuildTables, () => bIncludeInactive, Visible);
        var arrHeaders = new[] { "Name", "Phone", "Email", "Organization", "Skills", "Tags", "Last contact (UTC)" };
        vmLastContacted = new("Not contacted recently", Visible, fnOpenProfile, arrHeaders);
        vmRecentContacts = new("Recent contacts", Visible, fnOpenProfile, arrHeaders);
        vmSearch = new("Find by skill, tag or service", Visible, fnOpenProfile, "Type", "Name", "Matched text", "Organization", "Phone", "Email", "Last contact (UTC)");
        vmMembers = new("Organization members", Visible, fnOpenProfile, "Name", "Status", "Phone", "Email", "Skills", "Tags", "Last contact (UTC)");
        vmContracts = new("Latest contract per contact", Visible, null, "Name", "Organizations", "Date (UTC)", "Value (USD)", "Location", "Current contract");
        vmMovement = new("Movement", Visible, null, "Contact", "Organization", "Role", "Start (UTC)", "End (UTC)");
    }
    public IReadOnlyList<AnalyticsTableViewModel> lstTables => [vmLastContacted, vmRecentContacts, vmSearch, vmMembers, vmContracts, vmMovement];
    private void ClearTables()
    {
        foreach (var vmTable in lstTables) vmTable.Clear();
        foreach (var objChoice in lstCurrentChoices) objChoice.Release();
        lstCurrentChoices.Clear(); objCurrentHighlight = null; lngSelectedOrganization = null;
        strCurrentSearch = strCurrentOrganizationSearch = strCurrentDaysError = ""; strCurrentDays = "30"; bCurrentDropOpen = false;
        vmLastFilter.Clear(); vmRecentFilter.Clear(); NotifyPresentation();
    }
    private static bool Match(string strValue, string strQuery)
    {
        var strTerm = strQuery.Trim();
        if (strTerm.Length == 0 || strValue.Contains(strTerm, StringComparison.OrdinalIgnoreCase)) return true;
        var strCode = SoundexHelper.GetSoundexCode(strTerm);
        return strCode.Length > 0 && strValue.Split([' ', ',', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Any(strWord => SoundexHelper.GetSoundexCode(strWord) == strCode);
    }
    private static string Matched(string strValues, string strKind, string strQuery) => string.Join(", ", strValues.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(strPhrase => Match(strPhrase, strQuery)).Select(strPhrase => strPhrase + " (" + strKind + ")"));
    private static string JoinMatches(string strFirst, string strSecond) => string.Join(", ", new[] { strFirst, strSecond }.Where(strValue => strValue.Length > 0));
    private static string Timestamp(DateTimeOffset? dtValue) => dtValue?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Never";
    private static AnalyticsDisplayRow ContactRow(AnalyticsContactRow objRow) => new(objRow.lngId, RecordKind.Contact,
        [objRow.strName, objRow.strPhone, objRow.strEmail, objRow.strOrganizations, objRow.strSkills, objRow.strTags, Timestamp(objRow.dtLastNoteUtc)]);
    private bool ContactMatch(AnalyticsContactRow objRow, AnalyticsContactFilter vmFilter) => ContactAllowed(objRow.enStatus, objRow.bArchived) && vmFilter.Status(objRow.enStatus)
        && new[] { objRow.strName, objRow.strOrganizations, objRow.strSkills, objRow.strTags }.Any(strValue => Match(strValue, vmFilter.strSearch));
    private void RebuildTables()
    {
        var objData = objSnapshot;
        if (objData is null) { ClearTables(); return; }
        var bValid = strDays.Length is >= 1 and <= 4 && strDays.All(char.IsAsciiDigit)
            && int.TryParse(strDays, NumberStyles.None, CultureInfo.InvariantCulture, out var iDays) && iDays is >= 1 and <= 3650;
        var iThreshold = bValid ? int.Parse(strDays, CultureInfo.InvariantCulture) : 0;
        strCurrentDaysError = bValid ? "" : "Enter a whole number from 1 to 3650.";
        vmLastContacted.Replace(objData.LastContacted.Where(objRow => bValid && ContactMatch(objRow, vmLastFilter)
            && (!objRow.dtLastNoteUtc.HasValue || (objData.AsOfUtc - objRow.dtLastNoteUtc.Value).TotalDays >= iThreshold)).Select(ContactRow), objData.LastContacted.Count, objData.LastContactedTotal);
        vmRecentContacts.Replace(objData.RecentContacts.Where(objRow => ContactMatch(objRow, vmRecentFilter)).Select(ContactRow), objData.RecentContacts.Count, objData.RecentContactsTotal);
        var lstFound = new List<AnalyticsDisplayRow>();
        if (iSearchType is 0 or 2)
            foreach (var objRow in objData.ContactSearch.Where(objRow => ContactAllowed(objRow.enStatus, objRow.bArchived)))
            {
                var strMatched = JoinMatches(Matched(objRow.strSkills, "skill", strFindSearch), Matched(objRow.strTags, "tag", strFindSearch));
                if (strMatched.Length > 0) lstFound.Add(SearchRow(objRow.lngId, RecordKind.Contact, objRow.strName, strMatched, objRow.strOrganizations, objRow.strPhone, objRow.strEmail, Timestamp(objRow.dtLastNoteUtc)));
            }
        if (iSearchType is 1 or 2)
            foreach (var objRow in objData.OrganizationRoster.Where(objRow => OrganizationAllowed(objRow.bArchived)))
            {
                var strMatched = JoinMatches(Matched(objRow.strServices, "service", strFindSearch), Matched(objRow.strTags, "tag", strFindSearch));
                if (strMatched.Length > 0) lstFound.Add(SearchRow(objRow.lngId, RecordKind.Organization, objRow.strName, strMatched, "—", objRow.strPhone, objRow.strEmail, "n/a"));
            }
        vmSearch.Replace(lstFound, (iSearchType == 1 ? 0 : objData.ContactSearch.Count) + (iSearchType == 0 ? 0 : objData.OrganizationRoster.Count),
            (iSearchType == 1 ? 0 : objData.ContactSearchTotal) + (iSearchType == 0 ? 0 : objData.OrganizationRosterTotal));
        if (SelectedOrganization is null) lngSelectedOrganization = null;
        RebuildChoices();
        vmMembers.Replace(objData.OrganizationMembers.Where(objRow => objRow.lngOrganizationId == lngSelectedOrganization && ContactAllowed(objRow.enStatus, objRow.bArchived))
            .Select(objRow => new AnalyticsDisplayRow(objRow.lngContactId, RecordKind.Contact, [objRow.strName, objRow.enStatus + (objRow.bArchived ? " (Archived)" : ""),
                objRow.strPhone, objRow.strEmail, objRow.strSkills, objRow.strTags, Timestamp(objRow.dtLastNoteUtc)])), objData.OrganizationMembers.Count, objData.OrganizationMembersTotal);
        vmContracts.Replace(objData.LatestContracts.Where(objRow => ContactAllowed(objRow.enStatus, objRow.bArchived)).Select(objRow => new AnalyticsDisplayRow(objRow.lngId, null,
            [objRow.strName, objRow.strOrganizations, objRow.dtDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (objRow.lngValueCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US")), objRow.strLocation, "Not tracked yet"])), objData.LatestContracts.Count, objData.LatestContractsTotal);
        vmMovement.Replace(objData.Movement.Where(objRow => ContactAllowed(objRow.enStatus, objRow.bArchived) && OrganizationAllowed(objRow.bOrganizationArchived)).Select(objRow => new AnalyticsDisplayRow(objRow.lngContactId, null,
            [objRow.strName, objRow.strOrganization + (objRow.bOrganizationArchived ? " (Archived)" : ""), objRow.strRole, objRow.dtStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), objRow.dtEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Current"])), objData.Movement.Count, objData.MovementTotal);
        NotifyPresentation();
    }
    private static AnalyticsDisplayRow SearchRow(long lngId, RecordKind enKind, string strName, string strMatch, string strOrganization, string strPhone, string strEmail, string strLast)
    {
        var arrCells = new[] { enKind.ToString(), strName, strMatch.Length > 80 ? strMatch[..79] + "…" : strMatch, strOrganization, strPhone, strEmail, strLast };
        return new(lngId, enKind, arrCells, string.Join("; ", new[] { arrCells[0], strName, strMatch, strOrganization, strPhone, strEmail, strLast }));
    }
    private void RebuildChoices()
    {
        foreach (var objChoice in lstCurrentChoices) objChoice.Release();
        lstCurrentChoices.Clear();
        lstCurrentChoices = objSnapshot?.OrganizationRoster.Where(objRow => OrganizationAllowed(objRow.bArchived)
                && objRow.strName.Contains(strOrganizationSearch.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(objRow => objRow.strName, StringComparer.OrdinalIgnoreCase).ThenBy(objRow => objRow.lngId).Select(objRow => new AnalyticsOrganizationChoice(objRow)).ToList() ?? [];
        objCurrentHighlight = null; Notify(nameof(lstOrganizationChoices)); Notify(nameof(objHighlightedOrganization));
    }
    public void SelectOrganization(AnalyticsOrganizationChoice? objChoice)
    {
        if (!bHasData || objChoice is null || !lstCurrentChoices.Contains(objChoice)) return;
        lngSelectedOrganization = objChoice.lngId; bOrganizationDropOpen = false; RebuildTables();
    }
    private void NotifyPresentation()
    {
        vmLastFilter.Freshness(); vmRecentFilter.Freshness();
        foreach (var strName in new[] { nameof(strDays), nameof(strDaysError), nameof(bDaysError), nameof(bHasData), nameof(strFindSearch), nameof(iSearchType), nameof(bSearchEmpty),
            nameof(strOrganizationSearch), nameof(lstOrganizationChoices), nameof(objHighlightedOrganization), nameof(bOrganizationDropOpen), nameof(bSelectOrganization), nameof(bNoMembers),
            nameof(bNoMovement), nameof(strSelectedOrganization), nameof(strOrganizationPhone), nameof(strOrganizationEmail), nameof(strOrganizationServices), nameof(strOrganizationTags) }) Notify(strName);
    }
}
