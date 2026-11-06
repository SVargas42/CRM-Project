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
    public bool[] arrUntrimmedColumns { get; internal set; } = [];
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
        foreach (var objRow in lstCurrent) objRow.arrUntrimmedColumns = arrHeaders.Select(strHeader => strHeader is "Phone" or "Email").ToArray();
        strCurrentCount = $"Showing {lstCurrent.Count} of {iLoaded} loaded ({lngTotal} total)";
        Notify(nameof(lstRows)); Notify(nameof(strCount)); Notify(nameof(bEmpty));
    }
    internal void Clear() { Replace([], 0, 0); strCurrentCount = ""; Notify(nameof(strCount)); }
}
public sealed class AnalyticsOrganizationChoice : ViewModelBase
{
    public long lngId { get; }
    private string strText;
    public string strCaption => strText;
    public AnalyticsOrganizationChoice(AnalyticsOrganizationRow objRow) { lngId = objRow.lngId; strText = objRow.strName + (objRow.bArchived ? " (archived)" : ""); }
    internal void Release() { strText = ""; Notify(nameof(strCaption)); }
}
public sealed partial class AnalyticsViewModel
{
    private bool bCurrentInclude, bCurrentDropOpen;
    private string strCurrentDays = "30", strCurrentSearch = "", strCurrentOrganizationSearch = "", strCurrentDaysError = "";
    private int iCurrentSearchType, iCurrentStatus, iCurrentPeriod = 2;
    private bool bCurrentContractsExpanded, bCurrentMovementExpanded;
    private string strCurrentListFilter = "";
    private long? lngSelectedOrganization;
    private List<AnalyticsOrganizationChoice> lstCurrentChoices = [];
    private AnalyticsOrganizationChoice? objCurrentHighlight;
    public AnalyticsTableViewModel vmLastContacted { get; private set; } = null!;
    public AnalyticsTableViewModel vmRecentContacts { get; private set; } = null!;
    public AnalyticsTableViewModel vmSearch { get; private set; } = null!;
    public AnalyticsTableViewModel vmMembers { get; private set; } = null!;
    public AnalyticsTableViewModel vmContracts { get; private set; } = null!;
    public AnalyticsTableViewModel vmMovement { get; private set; } = null!;
    public bool bHasData => objSnapshot is not null;
    public bool bIncludeInactive { get => bCurrentInclude; set { if (Set(ref bCurrentInclude, value)) { if (!value) { iCurrentStatus = 0; Notify(nameof(iStatus)); } RebuildTables(); } } }
    public string strListFilter { get => bHasData ? strCurrentListFilter : ""; set { if (Set(ref strCurrentListFilter, value ?? "")) RebuildTables(); } }
    public int iStatus { get => iCurrentStatus; set { if (value is >= 0 and <= 4 && (bIncludeInactive || value == 0) && Set(ref iCurrentStatus, value)) RebuildTables(); } }
    public bool bOtherStatusesEnabled => bHasData && bIncludeInactive;
    public bool bShowStatusHint => !bIncludeInactive;
    public IReadOnlyList<string> lstPeriods { get; } = ["24 hours", "7 days", "30 days", "6 months", "1 year", "Year to date"];
    public int iPeriod { get => iCurrentPeriod; set { if (value is >= 0 and < 6 && Set(ref iCurrentPeriod, value)) NotifyCards(); } }
    public bool bContractsExpanded { get => bHasData && bCurrentContractsExpanded; set => Set(ref bCurrentContractsExpanded, value); }
    public bool bMovementExpanded { get => bHasData && bCurrentMovementExpanded; set => Set(ref bCurrentMovementExpanded, value); }
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
    public string strSelectedOrganization => SelectedOrganization is { } objOrganization ? objOrganization.strName + (objOrganization.bArchived ? " (archived)" : "") : "";
    public string strOrganizationEmail => SelectedOrganization?.strEmail ?? "";
    public string strOrganizationServices => SelectedOrganization?.strServices ?? "";
    public string strOrganizationTags => SelectedOrganization?.strTags ?? "";
    private bool ContactAllowed(RecordStatus enStatus, bool bArchived) => (bIncludeInactive || enStatus == RecordStatus.Active && !bArchived)
        && (iStatus == 4 || (int)enStatus == iStatus);
    private bool OrganizationAllowed(bool bArchived) => bIncludeInactive || !bArchived;
    private void InitializeTables()
    {
        bool Visible() => bHasData;
        var arrHeaders = new[] { "Name", "Phone", "Email", "Organization", "Last contact (UTC)" };
        vmLastContacted = new("Not contacted recently", Visible, fnOpenProfile, arrHeaders);
        vmRecentContacts = new("Recent contacts", Visible, fnOpenProfile, arrHeaders);
        vmSearch = new("Find by skill, tag or service", Visible, fnOpenProfile, "Type", "Name", "Matched words", "Organization", "Phone", "Email");
        vmMembers = new("Organization members", Visible, fnOpenProfile, "Name", "Phone", "Email", "Last contact (UTC)");
        vmContracts = new("Latest contract per contact", Visible, fnOpenProfile, "Name", "Organization", "Date (UTC)", "Value (USD)", "Location", "Current contract");
        vmMovement = new("Movement", Visible, fnOpenProfile, "Contact", "Organization", "Role", "Start (UTC)", "End (UTC)");
    }
    public IReadOnlyList<AnalyticsTableViewModel> lstTables => [vmLastContacted, vmRecentContacts, vmSearch, vmMembers, vmContracts, vmMovement];
    private void ClearTables()
    {
        foreach (var vmTable in lstTables) vmTable.Clear();
        foreach (var objChoice in lstCurrentChoices) objChoice.Release();
        lstCurrentChoices.Clear(); objCurrentHighlight = null; lngSelectedOrganization = null;
        strCurrentSearch = strCurrentOrganizationSearch = strCurrentDaysError = ""; strCurrentDays = "30"; bCurrentDropOpen = false;
        strCurrentListFilter = ""; iCurrentStatus = 0; iCurrentPeriod = 2;
        bCurrentContractsExpanded = bCurrentMovementExpanded = false; NotifyPresentation();
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
        [objRow.strName, objRow.strPhone, objRow.strEmail, objRow.strOrganizations, Timestamp(objRow.dtLastNoteUtc)]);
    private bool ListMatch(params string[] arrValues) => arrValues.Any(strValue => Match(strValue, strListFilter));
    private bool ContactMatch(AnalyticsContactRow objRow) => ContactAllowed(objRow.enStatus, objRow.bArchived)
        && ListMatch(objRow.strName, objRow.strOrganizations, objRow.strPhone, objRow.strEmail);
    private void RebuildTables()
    {
        var objData = objSnapshot;
        if (objData is null) { ClearTables(); return; }
        var bValid = strDays.Length is >= 1 and <= 4 && strDays.All(char.IsAsciiDigit)
            && int.TryParse(strDays, NumberStyles.None, CultureInfo.InvariantCulture, out var iDays) && iDays is >= 1 and <= 3650;
        var iThreshold = bValid ? int.Parse(strDays, CultureInfo.InvariantCulture) : 0;
        strCurrentDaysError = bValid ? "" : "Enter a whole number from 1 to 3650.";
        vmLastContacted.Replace(objData.LastContacted.Where(objRow => bValid && ContactMatch(objRow)
            && (!objRow.dtLastNoteUtc.HasValue || (objData.AsOfUtc - objRow.dtLastNoteUtc.Value).TotalDays >= iThreshold)).Select(ContactRow), objData.LastContacted.Count, objData.LastContactedTotal);
        vmRecentContacts.Replace(objData.RecentContacts.Where(ContactMatch).Select(ContactRow), objData.RecentContacts.Count, objData.RecentContactsTotal);
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
                if (strMatched.Length > 0) lstFound.Add(SearchRow(objRow.lngId, RecordKind.Organization, objRow.strName + (objRow.bArchived ? " (archived)" : ""), strMatched, "—", objRow.strPhone, objRow.strEmail, "n/a"));
            }
        vmSearch.Replace(lstFound, (iSearchType == 1 ? 0 : objData.ContactSearch.Count) + (iSearchType == 0 ? 0 : objData.OrganizationRoster.Count),
            (iSearchType == 1 ? 0 : objData.ContactSearchTotal) + (iSearchType == 0 ? 0 : objData.OrganizationRosterTotal));
        if (SelectedOrganization is null) lngSelectedOrganization = null;
        RebuildChoices();
        vmMembers.Replace(objData.OrganizationMembers.Where(objRow => objRow.lngOrganizationId == lngSelectedOrganization && ContactAllowed(objRow.enStatus, objRow.bArchived)
                && ListMatch(objRow.strName, SelectedOrganization?.strName ?? "", objRow.strPhone, objRow.strEmail))
            .Select(objRow => new AnalyticsDisplayRow(objRow.lngContactId, RecordKind.Contact, [objRow.strName,
                objRow.strPhone, objRow.strEmail, Timestamp(objRow.dtLastNoteUtc)])), objData.OrganizationMembers.Count, objData.OrganizationMembersTotal);
        vmContracts.Replace(objData.LatestContracts.Where(objRow => ContactAllowed(objRow.enStatus, objRow.bArchived)).Select(objRow => new AnalyticsDisplayRow(objRow.lngId, RecordKind.Contact,
            [objRow.strName, objRow.strOrganizations, objRow.dtDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (objRow.lngValueCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US")), objRow.strLocation, "Not tracked yet"])), objData.LatestContracts.Count, objData.LatestContractsTotal);
        vmMovement.Replace(objData.Movement.Where(objRow => ContactAllowed(objRow.enStatus, objRow.bArchived) && OrganizationAllowed(objRow.bOrganizationArchived)).Select(objRow => new AnalyticsDisplayRow(objRow.lngContactId, RecordKind.Contact,
            [objRow.strName, objRow.strOrganization + (objRow.bOrganizationArchived ? " (archived)" : ""), objRow.strRole, objRow.dtStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), objRow.dtEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Current"])), objData.Movement.Count, objData.MovementTotal);
        NotifyPresentation();
    }
    private static AnalyticsDisplayRow SearchRow(long lngId, RecordKind enKind, string strName, string strMatch, string strOrganization, string strPhone, string strEmail, string strLast)
    {
        var arrCells = new[] { enKind.ToString(), strName, strMatch.Length > 80 ? strMatch[..79] + "…" : strMatch, strOrganization, strPhone, strEmail };
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
        foreach (var strName in new[] { nameof(strDays), nameof(strDaysError), nameof(bDaysError), nameof(bHasData), nameof(strFindSearch), nameof(iSearchType), nameof(bSearchEmpty),
            nameof(strListFilter), nameof(iStatus), nameof(iPeriod), nameof(bOtherStatusesEnabled), nameof(bShowStatusHint), nameof(bContractsExpanded), nameof(bMovementExpanded),
            nameof(strOrganizationSearch), nameof(lstOrganizationChoices), nameof(objHighlightedOrganization), nameof(bOrganizationDropOpen), nameof(bSelectOrganization), nameof(bNoMembers),
            nameof(bNoMovement), nameof(strSelectedOrganization), nameof(strOrganizationPhone), nameof(strOrganizationEmail), nameof(strOrganizationServices), nameof(strOrganizationTags) }) Notify(strName);
        NotifyCards();
    }
    private bool DaysValid(out int iDays) => int.TryParse(strDays, NumberStyles.None, CultureInfo.InvariantCulture, out iDays)
        && strDays.Length is >= 1 and <= 4 && strDays.All(char.IsAsciiDigit) && iDays is >= 1 and <= 3650;
    public string strNotContactedTitle => "Not contacted " + (DaysValid(out var iDays) ? iDays.ToString(CultureInfo.InvariantCulture) : "N") + "+ days";
    public string strNotContactedCount => objSnapshot is { LastContacted.Count: > 0 } objData && DaysValid(out var iDays)
        ? Number(objData.LastContacted.LongCount(objRow => ContactAllowed(objRow.enStatus, objRow.bArchived) && (!objRow.dtLastNoteUtc.HasValue || (objData.AsOfUtc - objRow.dtLastNoteUtc.Value).TotalDays >= iDays))) : "No data";
    public string strNeverContactedCount => objSnapshot is { LastContacted.Count: > 0 } objData
        ? Number(objData.LastContacted.LongCount(objRow => ContactAllowed(objRow.enStatus, objRow.bArchived) && !objRow.dtLastNoteUtc.HasValue)) : "No data";
    public bool bContactCardsCapped => objSnapshot is { } objData && objData.LastContactedTotal > objData.LastContacted.Count;
    public bool bCardsIgnoreText => bHasData && strListFilter.Trim().Length > 0;
    private AnalyticsPeriodSummary? Period(bool bPrevious) => objSnapshot is { } objData
        && objData.ContactTotals.Sum(objRow => objRow.lngCount) + objData.OrganizationTotals.Sum(objRow => objRow.lngCount) > 0
        ? objData.PeriodSummaries.FirstOrDefault(objRow => objRow.strPeriod == lstPeriods[iPeriod] && objRow.bActiveOnly == !bIncludeInactive && objRow.bPrevious == bPrevious) : null;
    public string strNotesCount => Period(false) is { } objRow ? Number(objRow.lngNotes) : "No data";
    public string strOrganizationsReachedCount => Period(false) is { } objRow ? Number(objRow.lngOrganizations) : "No data";
    public string strNotesPrevious => Period(true) is { } objRow ? "was " + Number(objRow.lngNotes) : "";
    public string strOrganizationsPrevious => Period(true) is { } objRow ? "was " + Number(objRow.lngOrganizations) : "";
    public string strNotesChange => Period(false) is { } objNow && Period(true) is { } objBefore
        ? objBefore.lngNotes == 0 ? "n/a" : ((objNow.lngNotes - (decimal)objBefore.lngNotes) * 100m / objBefore.lngNotes).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "";
    private static string Number(long lngValue) => lngValue.ToString(CultureInfo.InvariantCulture);
    private void NotifyCards()
    {
        foreach (var strName in new[] { nameof(strNotContactedTitle), nameof(strNotContactedCount), nameof(strNeverContactedCount), nameof(bContactCardsCapped), nameof(bCardsIgnoreText),
            nameof(strNotesCount), nameof(strOrganizationsReachedCount), nameof(strNotesPrevious), nameof(strOrganizationsPrevious), nameof(strNotesChange) }) Notify(strName);
    }
}
